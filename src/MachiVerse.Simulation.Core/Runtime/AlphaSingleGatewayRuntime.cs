using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using MachiVerse.Protocol.Canonical;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Protocol;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

/// <summary>
/// Local-only INT-01 bootstrap. This adapter deliberately does not replace the release transport
/// authentication design; it exists only so the single-Gateway Alpha can exercise the real SIM-14
/// gRPC boundary and durable state before INT-02 multi-Gateway/mTLS acceptance.
/// </summary>
public sealed record AlphaSingleGatewayOptions(
    string CoreConfigPath,
    string PersistenceRoot,
    OpaqueId128 WorldId,
    WorldSeed256 WorldSeed,
    OpaqueId128 CoreInstanceId,
    OpaqueId128 GatewayLogicalId,
    OpaqueId128 GatewayComponentInstanceId)
{
    private const string DefaultWorldId = "00000000000000000000000000000001";
    private const string DefaultCoreInstanceId = "00000000000000000000000000000002";
    private const string DefaultGatewayLogicalId = "00000000000000000000000000000003";
    private const string DefaultGatewayComponentInstanceId = "00000000000000000000000000000004";
    private const string DefaultWorldSeedHex = "0000000000000000000000000000000000000000000000000000000000000000";

    public static AlphaSingleGatewayOptions FromEnvironment()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MACHIVERSE_ALPHA_LOCAL"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "INT-01 local runtime is opt-in. Set MACHIVERSE_ALPHA_LOCAL=1; release transport authentication is not configured by this bootstrap.");

        return new AlphaSingleGatewayOptions(
            Environment.GetEnvironmentVariable("MACHIVERSE_CORE_CONFIG")
                ?? Path.Combine(AppContext.BaseDirectory, "config", "simulation-core.toml"),
            Environment.GetEnvironmentVariable("MACHIVERSE_WORLD_ROOT") ?? ".machiverse/alpha",
            ParseId("MACHIVERSE_WORLD_ID", DefaultWorldId),
            ParseSeed(Environment.GetEnvironmentVariable("MACHIVERSE_WORLD_SEED_HEX") ?? DefaultWorldSeedHex),
            ParseId("MACHIVERSE_CORE_INSTANCE_ID", DefaultCoreInstanceId),
            ParseId("MACHIVERSE_GATEWAY_LOGICAL_ID", DefaultGatewayLogicalId),
            ParseId("MACHIVERSE_GATEWAY_COMPONENT_INSTANCE_ID", DefaultGatewayComponentInstanceId));
    }

    private static OpaqueId128 ParseId(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable) ?? fallback;
        var id = OpaqueId128.Parse(value);
        if (id.IsZero) throw new InvalidDataException($"{variable} cannot be ZERO.");
        return id;
    }

    private static WorldSeed256 ParseSeed(string value)
    {
        if (value.Length != 64 || value.Any(static c => c is >= 'A' and <= 'F'))
            throw new InvalidDataException("MACHIVERSE_WORLD_SEED_HEX must be 64 lowercase hexadecimal digits.");
        try
        {
            return new WorldSeed256(Convert.FromHexString(value));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("MACHIVERSE_WORLD_SEED_HEX is not canonical hexadecimal.", ex);
        }
    }
}

public sealed class AlphaSingleGatewayRuntime
{
    private AlphaSingleGatewayRuntime(
        SqlitePersistenceStore store,
        CoreGatewayNegotiationProfileV1 negotiationProfile,
        CoreGatewayEnvelopeFactoryV1 envelopeFactory,
        IAuthenticatedGatewayIdentityResolverV1 identityResolver,
        CoreGatewaySessionRegistryV1 sessions,
        CoreMasterAuthorityCoordinatorV1 master,
        CoreGatewayOperationProtocolV1 operations,
        CoreConfirmedPublicationCoordinatorV1 publications,
        ICoreGatewayWorldAuthorityV1 worldAuthority)
    {
        Store = store;
        NegotiationProfile = negotiationProfile;
        EnvelopeFactory = envelopeFactory;
        IdentityResolver = identityResolver;
        Sessions = sessions;
        Master = master;
        Operations = operations;
        Publications = publications;
        WorldAuthority = worldAuthority;
    }

    public SqlitePersistenceStore Store { get; }
    public CoreGatewayNegotiationProfileV1 NegotiationProfile { get; }
    public CoreGatewayEnvelopeFactoryV1 EnvelopeFactory { get; }
    public IAuthenticatedGatewayIdentityResolverV1 IdentityResolver { get; }
    public CoreGatewaySessionRegistryV1 Sessions { get; }
    public CoreMasterAuthorityCoordinatorV1 Master { get; }
    public CoreGatewayOperationProtocolV1 Operations { get; }
    public CoreConfirmedPublicationCoordinatorV1 Publications { get; }
    public ICoreGatewayWorldAuthorityV1 WorldAuthority { get; }

    public static async Task<AlphaSingleGatewayRuntime> CreateAsync(
        AlphaSingleGatewayOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configText = await File.ReadAllTextAsync(options.CoreConfigPath, cancellationToken);
        var config = new CoreConfigCoordinator().LoadStartup(configText);
        var paths = ResolvePersistencePaths(options);
        var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken);

        CoreProtocolPersistenceHeadV1 head;
        try
        {
            head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.meta-not-initialized")
        {
            var genesis = CreateGenesis(options.WorldId, options.WorldSeed, config.Digest);
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(options.WorldId, genesis.RecordDigest);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    options.WorldId,
                    PersistenceLayout.ReadCurrent(paths),
                    options.WorldSeed,
                    continuity,
                    config.Generation,
                    config.Digest,
                    1),
                genesis,
                cancellationToken);
            head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
        }

        if (!CryptographicOperations.FixedTimeEquals(head.ConfigDigest, config.Digest))
            throw new InvalidDataException(
                "alpha.config-digest-mismatch: persisted world Config differs from config/simulation-core.toml; use the Config change path or a fresh Alpha world root.");

        var sessions = new CoreGatewaySessionRegistryV1();
        var master = new CoreMasterAuthorityCoordinatorV1(store, sessions);
        await master.RecoverAsync(cancellationToken);

        sessions.Register(
            options.GatewayLogicalId,
            new GatewayRegisterV1
            {
                GatewayLogicalId = ByteString.CopyFrom(options.GatewayLogicalId.ToBytes()),
                ComponentInstanceId = ByteString.CopyFrom(options.GatewayComponentInstanceId.ToBytes()),
                LastKnownMasterGeneration = master.Current.MasterGeneration,
                Readiness = (GatewayReadinessV1)3,
            });
        await master.AssignMasterAsync(
            options.GatewayLogicalId,
            new StableToken("master.alpha-single-gateway"),
            cancellationToken);

        head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
        var binding = await RecoverBindingAsync(store, cancellationToken);
        var state = BuildConfirmedState(options, head, binding);
        var worldAuthority = new AlphaWorldAuthorityV1(state, binding, OperationSchedulingPolicyV1.FromConfig(config));
        var ingress = new AlphaDurableOperationIngressV1(store, options, config, worldAuthority);
        var operations = new CoreGatewayOperationProtocolV1(store, master, ingress);
        var publications = new CoreConfirmedPublicationCoordinatorV1(store, new AlphaParticipationProjectionSourceV1(worldAuthority));

        return new AlphaSingleGatewayRuntime(
            store,
            new CoreGatewayNegotiationProfileV1(),
            new CoreGatewayEnvelopeFactoryV1(options.CoreInstanceId),
            new AlphaLoopbackGatewayIdentityResolverV1(options.GatewayLogicalId),
            sessions,
            master,
            operations,
            publications,
            worldAuthority);
    }

    private static async Task<ParticipationBindingViewV1> RecoverBindingAsync(
        SqlitePersistenceStore store,
        CancellationToken cancellationToken)
    {
        var latest = await store.ReadLatestTerminalByResultCodeAsync("participation.binding.created", cancellationToken);
        if (latest is null) return AlphaParticipationBindingStateV1.None();
        if (latest.RichResultPayload is null)
            throw new InvalidDataException("alpha.binding-terminal-missing-rich-result");
        var binding = ParticipationBindingViewV1.Parser.ParseFrom(latest.RichResultPayload);
        AlphaParticipationBindingStateV1.Validate(binding, requireActive: true);
        return binding;
    }

    private static WorldPersistencePaths ResolvePersistencePaths(AlphaSingleGatewayOptions options)
    {
        var generationOne = PersistenceLayout.Resolve(options.PersistenceRoot, options.WorldId, 1);
        if (!File.Exists(generationOne.CurrentPath))
        {
            PersistenceLayout.EnsureGenerationDirectories(generationOne);
            PersistenceLayout.WriteCurrentAsync(generationOne, 1).GetAwaiter().GetResult();
            return generationOne;
        }

        var currentGeneration = PersistenceLayout.ReadCurrent(generationOne);
        var current = PersistenceLayout.Resolve(options.PersistenceRoot, options.WorldId, currentGeneration);
        PersistenceLayout.EnsureGenerationDirectories(current);
        return current;
    }

    private static HistoryRecordMaterial CreateGenesis(
        OpaqueId128 worldId,
        WorldSeed256 seed,
        byte[] configDigest)
        => HistoryRecordMaterial.Create(
            worldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: worldId.ToBytes().Concat(seed.ToBytes()).Concat(configDigest).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
                writer.WriteUnsigned(3); writer.WriteBytes(configDigest);
            });

    internal static WorldStateV1 BuildConfirmedState(
        AlphaSingleGatewayOptions options,
        CoreProtocolPersistenceHeadV1 head,
        ParticipationBindingViewV1 binding)
    {
        AlphaParticipationBindingStateV1.Validate(binding, requireActive: false);
        var revision = head.FinalizedStep == ulong.MaxValue ? ulong.MaxValue : head.FinalizedStep + 1;
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry =>
        {
            var isParticipation = string.Equals(entry.PartitionId.Value, "participation", StringComparison.Ordinal);
            var itemCount = isParticipation && (int)binding.Status == AlphaParticipationBindingStateV1.ActiveStatus ? 1UL : 0UL;
            var digest = isParticipation
                ? AlphaParticipationBindingStateV1.ComputeStateDigest(binding, head.FinalizedStep)
                : SHA256.HashData(Encoding.ASCII.GetBytes($"alpha-empty:{entry.PartitionId.Value}:{head.FinalizedStep}"));
            return new PartitionStateRefV1(new PartitionStateHeaderV1(
                entry,
                revision,
                head.FinalizedStep,
                DetailLevelV1.D0Entity,
                itemCount,
                digest));
        });
        var header = new WorldStateHeaderV1(
            options.WorldId,
            head.FinalizedStep,
            SHA256.HashData(options.WorldSeed.ToBytes()),
            head.ConfigGeneration,
            head.MasterGeneration,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            head.ConfigDigest);
    }
}

internal sealed class AlphaLoopbackGatewayIdentityResolverV1(OpaqueId128 gatewayLogicalId)
    : IAuthenticatedGatewayIdentityResolverV1
{
    public OpaqueId128 ResolveAuthenticatedGatewayLogicalId(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var peer = context.Peer ?? string.Empty;
        if (!peer.StartsWith("ipv4:127.0.0.1:", StringComparison.Ordinal) &&
            !peer.StartsWith("ipv6:[::1]:", StringComparison.Ordinal) &&
            !peer.StartsWith("ipv6:::1:", StringComparison.Ordinal))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "auth.unauthenticated: INT-01 local identity resolver accepts loopback peers only."));
        }
        return gatewayLogicalId;
    }
}

internal sealed class AlphaWorldAuthorityV1 : ICoreGatewayWorldAuthorityV1
{
    private readonly object _gate = new();
    private WorldStateV1 _state;
    private ParticipationBindingViewV1 _binding;
    private readonly OperationSchedulingPolicyV1 _schedulingPolicy;

    public AlphaWorldAuthorityV1(
        WorldStateV1 state,
        ParticipationBindingViewV1 binding,
        OperationSchedulingPolicyV1 schedulingPolicy)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _binding = binding?.Clone() ?? throw new ArgumentNullException(nameof(binding));
        _schedulingPolicy = schedulingPolicy ?? throw new ArgumentNullException(nameof(schedulingPolicy));
    }

    public Task<WorldStateV1> GetCurrentFinalizedStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_state);
    }

    public ParticipationBindingViewV1 GetBinding()
    {
        lock (_gate) return _binding.Clone();
    }

    public void ApplyDurableState(WorldStateV1 state, ParticipationBindingViewV1 binding)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(binding);
        AlphaParticipationBindingStateV1.Validate(binding, requireActive: false);
        lock (_gate)
        {
            if (state.Header.Step < _state.Header.Step)
                throw new InvalidDataException("alpha.world-state-regression");
            _state = state;
            _binding = binding.Clone();
        }
    }

    public OperationSchedulingPolicyV1 GetCurrentSchedulingPolicy() => _schedulingPolicy;
}

internal sealed class AlphaDurableOperationIngressV1 : ICoreGatewayDurableOperationIngressV1
{
    private readonly SqlitePersistenceStore _store;
    private readonly AlphaSingleGatewayOptions _options;
    private readonly EffectiveCoreConfig _config;
    private readonly AlphaWorldAuthorityV1 _worldAuthority;
    private readonly DurableOperationCoordinatorV1 _coordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AlphaDurableOperationIngressV1(
        SqlitePersistenceStore store,
        AlphaSingleGatewayOptions options,
        EffectiveCoreConfig config,
        AlphaWorldAuthorityV1 worldAuthority)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _worldAuthority = worldAuthority ?? throw new ArgumentNullException(nameof(worldAuthority));
        var policy = OperationSchedulingPolicyV1.FromConfig(config);
        _coordinator = new DurableOperationCoordinatorV1(
            store,
            new OperationSchedulingPolicyHistoryV1([
                new OperationSchedulingPolicyHistoryEntryV1(policy, 0, null)
            ]));
    }

    public async Task<OperationDurableObservationV1> SubmitDurablyAsync(
        StandardOperationV1 operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ParticipationBindingIdentityV1.ValidateStandardOperation(operation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await SubmitBindingLockedAsync(operation, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OperationDurableObservationV1> SubmitBindingLockedAsync(
        StandardOperationV1 operation,
        CancellationToken cancellationToken)
    {
        var operationId = OpaqueId128.FromBytes(operation.OperationId.Span);
        var digest = operation.ImmutablePayloadDigest.ToByteArray();
        var existing = await _coordinator.ObserveAsync(operationId, digest, cancellationToken);
        if (existing?.Lifecycle == OperationLifecycleStateV1.TerminalDurable) return existing;

        var head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        if (operation.Admission.AdmissionBasisStep != head.FinalizedStep)
            throw new InvalidDataException("world.basis-stale");
        if (operation.Admission.SchedulingPolicyGeneration != _config.Generation)
            throw new InvalidDataException("operation.scheduling-policy-generation-mismatch");
        if ((int)_worldAuthority.GetBinding().Status == AlphaParticipationBindingStateV1.ActiveStatus)
            throw new InvalidDataException("participation.binding-already-active");

        if (existing is null)
        {
            var acceptedHistory = await NextHistoryAsync(
                "operation.accepted.v1",
                "persistence.operation-accepted",
                operation.ToByteArray(),
                writer =>
                {
                    writer.WriteMapStart(3);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(digest);
                    writer.WriteUnsigned(2); writer.WriteAsciiText(operation.OperationKind);
                },
                cancellationToken);
            existing = await _coordinator.AcceptOrConvergeAsync(operationId, digest, acceptedHistory, cancellationToken);
        }

        var admission = new OperationSchedulingAdmissionV1(
            operation.Admission.AdmissionBasisStep,
            operation.Admission.SchedulingPolicyGeneration,
            operation.Admission.HasRequestedNotBeforeStep ? operation.Admission.RequestedNotBeforeStep : null,
            operation.Admission.HasRequestedDeadlineStep ? operation.Admission.RequestedDeadlineStep : null);
        head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        var decision = _coordinator.Plan(
            admission,
            new OperationSchedulingBarrierV1(head.FinalizedStep, PauseActive: false, PauseBasisStep: null),
            operation.Candidate?.CandidateStep);

        if (decision.Kind == OperationSchedulingDecisionKindV1.TerminalRejected)
        {
            if (existing.Lifecycle != OperationLifecycleStateV1.AcceptedDurable)
                throw new InvalidDataException("persistence.operation-invalid-lifecycle-for-reject");
            throw new InvalidDataException(decision.ResultCode);
        }

        if (existing.Lifecycle == OperationLifecycleStateV1.AcceptedDurable)
        {
            var effectiveStep = decision.EffectiveStep ?? throw new InvalidDataException("operation.effective-step-missing");
            var orderKey = BuildOrderKey(operationId, effectiveStep);
            var scheduledHistory = await NextHistoryAsync(
                "operation.scheduled.v1",
                "persistence.operation-scheduled",
                operation.ToByteArray(),
                writer =>
                {
                    writer.WriteMapStart(3);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteUnsigned(effectiveStep);
                    writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
                },
                cancellationToken);
            existing = await _coordinator.ScheduleOrConvergeAsync(
                operationId,
                digest,
                decision,
                orderKey,
                scheduledHistory,
                cancellationToken);
        }

        if (existing.Lifecycle != OperationLifecycleStateV1.ScheduledDurable || existing.EffectiveStep is null)
            return existing;

        var scheduledStep = existing.EffectiveStep.Value;
        while (true)
        {
            head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
            if (head.FinalizedStep >= scheduledStep) break;
            await CommitTransitionAsync(
                head,
                terminalOperations: Array.Empty<TerminalOperationCommit>(),
                binding: _worldAuthority.GetBinding(),
                cancellationToken);
        }

        head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        if (head.FinalizedStep != scheduledStep)
            throw new InvalidDataException("persistence.operation-effective-step-passed");

        var binding = CreateActiveBinding(operationId, scheduledStep);
        await CommitTransitionAsync(
            head,
            [new TerminalOperationCommit(
                operationId,
                (int)CoreOperationResultStatusV1.Success,
                "participation.binding.created",
                binding.ToByteArray())],
            binding,
            cancellationToken);

        var terminal = await _coordinator.ObserveAsync(operationId, digest, cancellationToken)
            ?? throw new InvalidDataException("persistence.operation-state-missing-after-commit");
        if (terminal.Lifecycle != OperationLifecycleStateV1.TerminalDurable)
            throw new InvalidDataException("persistence.operation-terminal-state-incomplete");
        return terminal;
    }

    private async Task CommitTransitionAsync(
        CoreProtocolPersistenceHeadV1 head,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        ParticipationBindingViewV1 binding,
        CancellationToken cancellationToken)
    {
        var transitionHistory = await NextHistoryAsync(
            "transition.committed.v1",
            "persistence.transition-committed",
            terminalOperations.Count == 0 ? Array.Empty<byte>() : terminalOperations.First().OperationId.ToBytes(),
            writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteUnsigned(head.FinalizedStep);
                writer.WriteUnsigned(1);
                writer.WriteArrayStart((ulong)terminalOperations.Count);
                foreach (var terminal in terminalOperations.OrderBy(static item => item.OperationId))
                    writer.WriteBytes(terminal.OperationId.ToBytes());
            },
            cancellationToken);
        var resultingStep = checked(head.FinalizedStep + 1);
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            _options.WorldId,
            resultingStep,
            head.StateContinuityToken,
            transitionHistory.RecordDigest);
        await _store.PersistTransitionCommitAsync(
            head.FinalizedStep,
            resultingStep,
            continuity,
            head.ConfigGeneration,
            head.ConfigDigest,
            transitionHistory,
            terminalOperations,
            cancellationToken);
        var nextHead = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        var state = AlphaSingleGatewayRuntime.BuildConfirmedState(_options, nextHead, binding);
        _worldAuthority.ApplyDurableState(state, binding);
    }

    private async Task<HistoryRecordMaterial> NextHistoryAsync(
        string recordType,
        string schemaId,
        byte[] payloadBytes,
        Action<MvDcborWriter> normalized,
        CancellationToken cancellationToken)
    {
        var anchor = await _store.ReadHistoryAnchorAsync(cancellationToken);
        if (anchor.Sequence == ulong.MaxValue) throw new OverflowException("HistorySequence cannot wrap.");
        return HistoryRecordMaterial.Create(
            _options.WorldId,
            anchor.Sequence + 1,
            anchor.Digest,
            recordType,
            schemaId,
            1,
            0,
            payloadBytes,
            normalized);
    }

    private static SameStepOrderKey BuildOrderKey(OpaqueId128 operationId, ulong effectiveStep)
        => new(
            phase: 1,
            domainRank: 50,
            conflictScopeDigest: HashSuite.DomainHash("mv.alpha.participation-binding-scope.v1", writer =>
            {
                writer.WriteArrayStart(1);
                writer.WriteUnsigned(effectiveStep);
            }),
            semanticPriority: 0,
            intentId: operationId);

    private ParticipationBindingViewV1 CreateActiveBinding(OpaqueId128 operationId, ulong effectiveStep)
    {
        var residentId = DerivedIdentity.DeriveEntityId(
            _options.WorldId,
            0,
            new StableToken("resident"),
            _options.WorldId,
            new StableToken("alpha-resident"),
            0);
        var bindingId = DerivedIdentity.DeriveEntityId(
            _options.WorldId,
            effectiveStep,
            new StableToken("participation"),
            operationId,
            new StableToken("diver-binding"),
            0);
        return new ParticipationBindingViewV1
        {
            Status = (ParticipationBindingWireStatusV1)AlphaParticipationBindingStateV1.ActiveStatus,
            BindingId = ByteString.CopyFrom(bindingId.ToBytes()),
            ResidentId = ByteString.CopyFrom(residentId.ToBytes()),
            EffectiveFromStep = effectiveStep,
            AbsencePolicyProfile = "alpha.default",
        };
    }
}

internal static class AlphaParticipationBindingStateV1
{
    internal const int NoneStatus = 1;
    internal const int ActiveStatus = 2;

    internal static ParticipationBindingViewV1 None()
        => new() { Status = (ParticipationBindingWireStatusV1)NoneStatus };

    internal static void Validate(ParticipationBindingViewV1 binding, bool requireActive)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var status = (int)binding.Status;
        if (status is not (NoneStatus or ActiveStatus))
            throw new InvalidDataException("alpha.binding-status-invalid");
        if (requireActive && status != ActiveStatus)
            throw new InvalidDataException("alpha.binding-not-active");
        if (status == ActiveStatus)
        {
            if (!binding.HasBindingId || binding.BindingId.Length != 16 || binding.BindingId.Span.IndexOfAnyExcept((byte)0) < 0)
                throw new InvalidDataException("alpha.binding-id-invalid");
            if (!binding.HasResidentId || binding.ResidentId.Length != 16 || binding.ResidentId.Span.IndexOfAnyExcept((byte)0) < 0)
                throw new InvalidDataException("alpha.binding-resident-id-invalid");
            if (!binding.HasEffectiveFromStep)
                throw new InvalidDataException("alpha.binding-effective-step-missing");
        }
    }

    internal static byte[] ComputeStateDigest(ParticipationBindingViewV1 binding, ulong basisStep)
    {
        Validate(binding, requireActive: false);
        return HashSuite.DomainHash("mv.alpha.participation-state.v1", writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteUnsigned((ulong)(int)binding.Status);
            writer.WriteUnsigned(1); WriteOptionalBytes(writer, binding.HasBindingId, binding.BindingId);
            writer.WriteUnsigned(2); WriteOptionalBytes(writer, binding.HasResidentId, binding.ResidentId);
            writer.WriteUnsigned(3);
            writer.WriteArrayStart(binding.HasEffectiveFromStep ? 1UL : 0UL);
            if (binding.HasEffectiveFromStep) writer.WriteUnsigned(binding.EffectiveFromStep);
            writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
        });
    }

    private static void WriteOptionalBytes(MvDcborWriter writer, bool present, ByteString value)
    {
        writer.WriteArrayStart(present ? 1UL : 0UL);
        if (present) writer.WriteBytes(value.Span);
    }
}

internal sealed class AlphaParticipationProjectionSourceV1(AlphaWorldAuthorityV1 authority)
    : ICoreStateProjectionSourceV1
{
    internal const string BindingSchemaId = "protocol.participation-binding-view.v1";
    private static readonly byte[] ProjectionSchemaDigest = SHA256.HashData("machiverse.alpha.participation-projection.v1"u8);

    public CoreProjectionFrameV1 BuildFull(WorldStateV1 state)
        => new([BuildBindingRecord(state, authority.GetBinding())], ProjectionSchemaDigest);

    public CoreProjectionFrameV1 BuildDelta(WorldStateV1 previousState, WorldStateV1 currentState)
    {
        var previous = ParticipationDigest(previousState);
        var current = ParticipationDigest(currentState);
        return CryptographicOperations.FixedTimeEquals(previous, current)
            ? new CoreProjectionFrameV1(Array.Empty<ProjectionRecordV1>(), ProjectionSchemaDigest)
            : new CoreProjectionFrameV1([BuildBindingRecord(currentState, authority.GetBinding())], ProjectionSchemaDigest);
    }

    private static ProjectionRecordV1 BuildBindingRecord(WorldStateV1 state, ParticipationBindingViewV1 binding)
    {
        var recordId = DerivedIdentity.DeriveEntityId(
            state.Header.WorldId,
            0,
            new StableToken("participation"),
            state.Header.WorldId,
            new StableToken("binding-projection"),
            0);
        return new ProjectionRecordV1
        {
            RecordSchemaId = BindingSchemaId,
            RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            RecordId = ByteString.CopyFrom(recordId.ToBytes()),
            RecordRevision = state.Header.Step + 1,
            MutationKind = (ProjectionMutationKindV1)1,
            Payload = binding.ToByteString(),
        };
    }

    private static byte[] ParticipationDigest(WorldStateV1 state)
        => state.Partitions.Get("participation").Header.CanonicalDigest.ToArray();
}
