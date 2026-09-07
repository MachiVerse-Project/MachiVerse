using System.Collections.Concurrent;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Protocol;

/// <summary>
/// Domain/application admission seam. An implementation may return only an observation that has
/// crossed the corresponding SIM-05 durable boundary; provisional/candidate states are forbidden.
/// </summary>
public interface ICoreGatewayDurableOperationIngressV1
{
    Task<OperationDurableObservationV1> SubmitDurablyAsync(
        StandardOperationV1 operation,
        CancellationToken cancellationToken = default);
}

public sealed class CoreGatewayOperationProtocolV1
{
    private const int MaxOperationsPerBatch = 4096;
    private readonly SqlitePersistenceStore _store;
    private readonly CoreMasterAuthorityCoordinatorV1 _master;
    private readonly ICoreGatewayDurableOperationIngressV1 _ingress;
    private readonly ConcurrentDictionary<OpaqueId128, byte[]> _batchDigests = new();

    public CoreGatewayOperationProtocolV1(
        SqlitePersistenceStore store,
        CoreMasterAuthorityCoordinatorV1 master,
        ICoreGatewayDurableOperationIngressV1 ingress)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public async Task<OperationBatchResultV1> SubmitBatchAsync(
        OpaqueId128 authenticatedGatewayLogicalId,
        ulong masterGeneration,
        OperationBatchV1 batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _master.RequireCurrentMaster(masterGeneration, authenticatedGatewayLogicalId);
        var batchId = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(batch.BatchId, "batch_id", allowZero: false));
        var batchDigest = CoreGatewayWireValidatorV1.ValidateHash256(batch.BatchDigest, "batch_digest");
        CoreGatewayWireValidatorV1.ValidateStableToken(batch.BatchKind, "batch_kind");
        if (batch.Operations.Count is 0 or > MaxOperationsPerBatch)
            throw new CoreGatewayProtocolException("protocol.limit-exceeded", "Operation batch size is outside 1..4096.");
        var orderedOperations = batch.Operations
            .Select(static operation => operation?.Clone() ?? throw new ArgumentNullException(nameof(batch)))
            .OrderBy(static operation => operation.OperationId, ByteStringLexicographicComparerV1.Instance)
            .ToArray();
        if (orderedOperations.Select(static operation => Convert.ToHexString(operation.OperationId.Span)).Distinct(StringComparer.Ordinal).Count() != orderedOperations.Length)
            throw new CoreGatewayProtocolException("request.invalid", "OperationBatch contains duplicate OperationId values.");
        foreach (var operation in orderedOperations) ValidateOperation(operation);

        if (_batchDigests.TryGetValue(batchId, out var knownDigest))
        {
            if (!knownDigest.AsSpan().SequenceEqual(batchDigest))
                throw new CoreGatewayProtocolException("protocol.batch-payload-mismatch", "Same BatchId was received with a different BatchDigest.");
        }
        else if (!_batchDigests.TryAdd(batchId, batchDigest.ToArray()))
        {
            var racedDigest = _batchDigests[batchId];
            if (!racedDigest.AsSpan().SequenceEqual(batchDigest))
                throw new CoreGatewayProtocolException("protocol.batch-payload-mismatch", "Same BatchId raced with a different BatchDigest.");
        }

        var results = new List<OperationBatchEntryResultV1>(orderedOperations.Length);
        foreach (var operation in orderedOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var observation = await _ingress.SubmitDurablyAsync(operation, cancellationToken);
                results.Add(ToBatchEntry(operation, observation));
            }
            catch (CoreGatewayProtocolException ex)
            {
                results.Add(ToRejectedBatchEntry(operation, ex.Code.Value));
            }
            catch (InvalidDataException ex)
            {
                var code = TryStableCode(ex.Message) ?? "request.invalid";
                results.Add(ToRejectedBatchEntry(operation, code));
            }
        }

        var terminalOrScheduled = results.Count(static entry => entry.Lifecycle is
            OperationLifecycleWireStateV1.Accepted or
            OperationLifecycleWireStateV1.Scheduled or
            OperationLifecycleWireStateV1.Terminal);
        var rejected = results.Count(static entry => entry.Result.Status == ResultStatusV1.Rejected);
        var status = rejected == results.Count
            ? BatchWireStatusV1.Rejected
            : rejected > 0
                ? BatchWireStatusV1.Partial
                : BatchWireStatusV1.Complete;
        if (terminalOrScheduled == 0 && rejected == 0)
            status = BatchWireStatusV1.Received;

        var response = new OperationBatchResultV1
        {
            BatchId = ByteString.CopyFrom(batchId.ToBytes()),
            BatchDigest = ByteString.CopyFrom(batchDigest),
            Status = status,
            Result = new ResultV1
            {
                Status = status == BatchWireStatusV1.Rejected ? ResultStatusV1.Rejected : ResultStatusV1.Success,
                Code = status switch
                {
                    BatchWireStatusV1.Partial => "batch.partial",
                    BatchWireStatusV1.Rejected => "request.invalid",
                    _ => "batch.complete",
                },
                RetryAdvice = RetryAdviceV1.DoNotRetry,
            },
        };
        response.Entries.AddRange(results);
        return response;
    }

    public async Task<OperationStatusResultV1> QueryStatusAsync(
        OperationStatusQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var operationId = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(query.OperationId, "operation_id", allowZero: false));
        var state = await _store.ReadOperationStateAsync(operationId, cancellationToken);
        if (state is null)
        {
            return new OperationStatusResultV1
            {
                OperationId = ByteString.CopyFrom(operationId.ToBytes()),
                State = OperationLifecycleWireStateV1.Unknown,
                RichResultDetailsAvailable = false,
            };
        }
        return ToStatusResult(state);
    }

    public static OperationSchedulingPolicyWireV1 ToWireSchedulingPolicy(OperationSchedulingPolicyV1 policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var wire = new OperationSchedulingPolicyWireV1
        {
            OwnerConfigGeneration = policy.OwnerConfigGeneration,
            MinLeadSteps = policy.MinLeadSteps,
            GraceSteps = policy.GraceSteps,
            LatePolicy = policy.LatePolicy switch
            {
                OperationLatePolicyV1.Reject => LatePolicyWireV1.Reject,
                OperationLatePolicyV1.DeferWithinGrace => LatePolicyWireV1.DeferWithinGrace,
                _ => throw new InvalidDataException("operation.late-policy-invalid"),
            },
        };
        if (policy.DefaultDeadlineWindowSteps is { } deadline) wire.DefaultDeadlineWindowSteps = deadline;
        return wire;
    }

    private static void ValidateOperation(StandardOperationV1 operation)
    {
        CoreGatewayWireValidatorV1.ValidateId128(operation.OperationId, "operation_id", allowZero: false);
        CoreGatewayWireValidatorV1.ValidateHash256(operation.ImmutablePayloadDigest, "immutable_payload_digest");
        CoreGatewayWireValidatorV1.ValidateStableToken(operation.OperationKind, "operation_kind");
        CoreGatewayWireValidatorV1.ValidateStableToken(operation.OperationPayloadSchemaId, "operation_payload_schema_id");
        if (operation.OperationPayloadSchemaVersion is null || operation.OperationPayloadSchemaVersion.Major == 0 || operation.OperationPayloadSchemaVersion.Major > ushort.MaxValue || operation.OperationPayloadSchemaVersion.Minor > ushort.MaxValue)
            throw new CoreGatewayProtocolException("protocol.schema-unsupported", "Operation payload schema version is invalid.");
        if (operation.Admission is null || operation.Admission.SchedulingPolicyGeneration == 0)
            throw new CoreGatewayProtocolException("protocol.missing-required", "Operation scheduling admission is required.");
        if (operation.Admission.HasRequestedNotBeforeStep && operation.Admission.HasRequestedDeadlineStep &&
            operation.Admission.RequestedDeadlineStep < operation.Admission.RequestedNotBeforeStep)
            throw new CoreGatewayProtocolException("request.invalid", "requested_deadline_step precedes requested_not_before_step.");
    }

    private static OperationBatchEntryResultV1 ToBatchEntry(
        StandardOperationV1 operation,
        OperationDurableObservationV1 observation)
    {
        var entry = new OperationBatchEntryResultV1
        {
            OperationId = operation.OperationId,
            OperationPayloadDigest = operation.ImmutablePayloadDigest,
            Lifecycle = ToWireLifecycle(observation.Lifecycle),
            Result = ToResult(observation),
        };
        if (observation.EffectiveStep is { } effectiveStep) entry.EffectiveStep = effectiveStep;
        return entry;
    }

    private static OperationBatchEntryResultV1 ToRejectedBatchEntry(StandardOperationV1 operation, string code)
        => new()
        {
            OperationId = operation.OperationId,
            OperationPayloadDigest = operation.ImmutablePayloadDigest,
            Lifecycle = OperationLifecycleWireStateV1.Unknown,
            Result = new ResultV1
            {
                Status = ResultStatusV1.Rejected,
                Code = code,
                RetryAdvice = code is "component.unavailable" or "component.resyncing"
                    ? RetryAdviceV1.RetrySameIdentity
                    : RetryAdviceV1.DoNotRetry,
            },
        };

    private static OperationStatusResultV1 ToStatusResult(DurableOperationStateV1 state)
    {
        var lifecycle = state.Lifecycle switch
        {
            DurableOperationLifecycleV1.AcceptedDurable => OperationLifecycleWireStateV1.Accepted,
            DurableOperationLifecycleV1.ScheduledDurable => OperationLifecycleWireStateV1.Scheduled,
            DurableOperationLifecycleV1.TerminalDurable => OperationLifecycleWireStateV1.Terminal,
            _ => throw new InvalidDataException("persistence.operation-lifecycle-invalid"),
        };
        var result = new OperationStatusResultV1
        {
            OperationId = ByteString.CopyFrom(state.OperationId.ToBytes()),
            State = lifecycle,
            OperationPayloadDigest = ByteString.CopyFrom(state.OperationPayloadDigest),
            RichResultDetailsAvailable = state.RichResultPayload is not null,
        };
        if (state.EffectiveStep is { } effectiveStep) result.EffectiveStep = effectiveStep;
        if (state.Lifecycle == DurableOperationLifecycleV1.TerminalDurable)
        {
            if (state.TerminalStatus is not { } raw || state.ResultCode is null ||
                !Enum.IsDefined(typeof(CoreOperationResultStatusV1), raw))
                throw new InvalidDataException("persistence.operation-terminal-state-incomplete");
            var status = (CoreOperationResultStatusV1)raw;
            result.TerminalResult = new ResultV1
            {
                Status = ToWireStatus(status),
                Code = state.ResultCode,
                RetryAdvice = RetryAdviceV1.DoNotRetry,
            };
        }
        return result;
    }

    private static ResultV1 ToResult(OperationDurableObservationV1 observation)
    {
        if (observation.Lifecycle == OperationLifecycleStateV1.TerminalDurable)
        {
            if (observation.TerminalStatus is not { } terminal || observation.ResultCode is null)
                throw new InvalidDataException("operation.durable-terminal-observation-incomplete");
            return new ResultV1
            {
                Status = ToWireStatus(terminal),
                Code = observation.ResultCode.Value.Value,
                RetryAdvice = RetryAdviceV1.DoNotRetry,
            };
        }
        return new ResultV1
        {
            Status = observation.Duplicate ? ResultStatusV1.Duplicate : ResultStatusV1.Accepted,
            Code = observation.Lifecycle == OperationLifecycleStateV1.ScheduledDurable ? "operation.scheduled" : "operation.accepted",
            RetryAdvice = RetryAdviceV1.RetrySameIdentity,
        };
    }

    private static OperationLifecycleWireStateV1 ToWireLifecycle(OperationLifecycleStateV1 lifecycle)
        => lifecycle switch
        {
            OperationLifecycleStateV1.AcceptedDurable => OperationLifecycleWireStateV1.Accepted,
            OperationLifecycleStateV1.ScheduledDurable => OperationLifecycleWireStateV1.Scheduled,
            OperationLifecycleStateV1.TerminalDurable => OperationLifecycleWireStateV1.Terminal,
            _ => OperationLifecycleWireStateV1.Unknown,
        };

    private static ResultStatusV1 ToWireStatus(CoreOperationResultStatusV1 status)
        => status switch
        {
            CoreOperationResultStatusV1.Success => ResultStatusV1.Success,
            CoreOperationResultStatusV1.Accepted => ResultStatusV1.Accepted,
            CoreOperationResultStatusV1.Pending => ResultStatusV1.Pending,
            CoreOperationResultStatusV1.NoChange => ResultStatusV1.NoChange,
            CoreOperationResultStatusV1.Duplicate => ResultStatusV1.Duplicate,
            CoreOperationResultStatusV1.Rejected => ResultStatusV1.Rejected,
            CoreOperationResultStatusV1.Failed => ResultStatusV1.Failed,
            _ => throw new InvalidDataException("operation.result-status-invalid"),
        };

    private static string? TryStableCode(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var first = message.Split(':', 2)[0];
        try
        {
            _ = new StableToken(first);
            return first;
        }
        catch
        {
            return null;
        }
    }
}
