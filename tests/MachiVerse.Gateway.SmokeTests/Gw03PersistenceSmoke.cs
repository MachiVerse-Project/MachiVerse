using System.Runtime.CompilerServices;
using Google.Protobuf;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

internal static class Gw03PersistenceSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-gw03-" + Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "custody.sqlite3");
        try
        {
            var operation = new StandardOperationV1
            {
                OperationId = Id(90),
                ImmutablePayloadDigest = Hash(91),
                OperationKind = "resident.fixture",
                Admission = new OperationSchedulingAdmissionWireV1
                {
                    AdmissionBasisStep = 100,
                    SchedulingPolicyGeneration = 7,
                },
                OperationPayloadSchemaId = "resident.fixture.v1",
                OperationPayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
                OperationPayload = ByteString.CopyFromUtf8("fixture"),
            };

            await using (var store = await PersistentCustodyStore.OpenAsync(databasePath))
            {
                var held = await store.HoldSourceAsync(operation);
                if (held.State != GatewayCustodyState.SourceHeld)
                    throw new InvalidOperationException("Durable custody must begin at SOURCE_HELD.");

                var duplicate = await store.HoldSourceAsync(operation.Clone());
                if (duplicate.OperationIdHex != held.OperationIdHex)
                    throw new InvalidOperationException("Duplicate logical Operation must attach to existing custody.");

                var mismatch = operation.Clone();
                mismatch.ImmutablePayloadDigest = Hash(92);
                var mismatchRejected = false;
                try
                {
                    await store.HoldSourceAsync(mismatch);
                }
                catch (InvalidDataException ex) when (ex.Message == "protocol.operation-payload-mismatch")
                {
                    mismatchRejected = true;
                }
                if (!mismatchRejected)
                    throw new InvalidOperationException("Persistent custody must reject same OperationId with different digest.");

                await store.AdvanceAsync(
                    operation.OperationId,
                    operation.ImmutablePayloadDigest,
                    GatewayCustodyState.MasterReceived,
                    observedMasterGeneration: 4);
            }

            PersistedCustodyOperation recovered;
            await using (var reopened = await PersistentCustodyStore.OpenAsync(databasePath))
            {
                var records = await reopened.LoadAllAsync();
                if (records.Count != 1 || records[0].State != GatewayCustodyState.MasterReceived)
                    throw new InvalidOperationException("Gateway restart must recover MASTER_RECEIVED custody without losing identity.");
                recovered = records[0];

                if (RetryCoordinator.PlanAfterFailover(recovered.State) != RetryConvergenceAction.QueryAuthoritativeStatus)
                    throw new InvalidOperationException("Failover must converge through authoritative status before blind identity changes.");
                var query = RetryCoordinator.CreateStatusQuery(recovered);
                if (!query.OperationId.Span.SequenceEqual(operation.OperationId.Span))
                    throw new InvalidOperationException("Status query must preserve OperationId.");
                var retry = RetryCoordinator.CreateRetryOperation(recovered);
                if (!retry.OperationId.Span.SequenceEqual(operation.OperationId.Span) ||
                    !retry.ImmutablePayloadDigest.Span.SequenceEqual(operation.ImmutablePayloadDigest.Span))
                    throw new InvalidOperationException("Retry must preserve OperationId and immutable digest.");

                await reopened.AdvanceAsync(
                    operation.OperationId,
                    operation.ImmutablePayloadDigest,
                    GatewayCustodyState.CoreAccepted,
                    observedMasterGeneration: 5);
                await reopened.AdvanceAsync(
                    operation.OperationId,
                    operation.ImmutablePayloadDigest,
                    GatewayCustodyState.Terminal,
                    observedMasterGeneration: 5,
                    terminalResult: new ResultV1
                    {
                        Status = (ResultStatusV1)1,
                        Code = "ok",
                        RetryAdvice = (RetryAdviceV1)1,
                    });
            }

            await using (var reopenedTerminal = await PersistentCustodyStore.OpenAsync(databasePath))
            {
                var terminal = (await reopenedTerminal.LoadAllAsync()).Single();
                if (terminal.State != GatewayCustodyState.Terminal || terminal.TerminalResult?.Code != "ok")
                    throw new InvalidOperationException("Terminal custody/result must survive Gateway restart.");
                if (RetryCoordinator.PlanAfterFailover(terminal.State) != RetryConvergenceAction.None)
                    throw new InvalidOperationException("Terminal Operation must not re-enter mutation retry after restart.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ByteString Id(byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, 16).ToArray());
    private static ByteString Hash(byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, 32).ToArray());
}
