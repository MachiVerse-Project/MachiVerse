using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim13StepCandidateTransactionSmoke
{
    internal static void Run()
    {
        const ulong basisStep = 100;
        var worldId = OpaqueId128.Parse("00000000000000000000000000013300");
        var state = CreateWorldState(worldId, basisStep);
        var frozen = new FrozenStepInputV1(worldId, basisStep, 1, new byte[32], []);
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => new DomainCandidateOutputV1(entry.DomainToken, basisStep))
            .ToArray();

        var validA = BirthTransaction(worldId, basisStep, 1, 0, valid: true);
        var validB = BirthTransaction(worldId, basisStep, 2, 1, valid: true);
        var candidateA = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013390"),
            state,
            frozen,
            outputs,
            [],
            transactionCandidates: [validB, validA]);
        var candidateB = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013391"),
            state,
            frozen,
            outputs.Reverse(),
            [],
            transactionCandidates: [validA, validB]);

        Require(candidateA.CommitDecision.CanCommit && candidateA.TransactionCandidates.Count == 2,
            "SIM-13 valid transactions must pass the StepCandidate atomicity barrier.");
        Require(candidateA.TransactionCandidates.Select(static transaction => transaction.TransactionId)
                .SequenceEqual(candidateB.TransactionCandidates.Select(static transaction => transaction.TransactionId)) &&
                candidateA.DiagnosticDigest.SequenceEqual(candidateB.DiagnosticDigest),
            "SIM-13 StepCandidate transaction order must be canonical and independent of input order.");
        Require(candidateA.InvariantResults.Any(static result =>
                result.InvariantId.Value == "transaction.atomicity" &&
                result.Outcome == InvariantOutcomeV1.Pass),
            "SIM-13 StepCandidate must record the aggregate transaction.atomicity invariant.");

        var invalid = BirthTransaction(worldId, basisStep, 3, 2, valid: false);
        var blocked = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013392"),
            state,
            frozen,
            outputs,
            [],
            transactionCandidates: [invalid]);
        Require(!blocked.CommitDecision.CanCommit &&
                blocked.InvariantResults.Single(result => result.InvariantId.Value == "transaction.atomicity").Outcome == InvariantOutcomeV1.Fail,
            "SIM-13 invalid transaction must block the entire StepCandidate instead of partially finalizing effects.");
        Require(blocked.InvariantResults.Single(result => result.InvariantId.Value == "transaction.atomicity")
                .DiagnosticCode?.Value == "transaction.participant-missing",
            "SIM-13 StepCandidate must preserve the canonical transaction failure diagnostic.");

        var basisMismatchRejected = false;
        try
        {
            var future = BirthTransaction(worldId, basisStep + 1, 4, 3, valid: true);
            _ = StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000013393"),
                state,
                frozen,
                outputs,
                [],
                transactionCandidates: [future]);
        }
        catch (InvalidDataException ex) when (ex.Message == "step-candidate.transaction-basis-mismatch")
        {
            basisMismatchRejected = true;
        }
        Require(basisMismatchRejected,
            "SIM-13 StepCandidate must reject transaction candidates from a different basis Step.");

        Sim13DurableAtomicitySmoke.Run();
        Sim13GoldenScenarioSmoke.Run();
    }

    private static CrossDomainTransactionCandidateV1 BirthTransaction(
        OpaqueId128 worldId,
        ulong basisStep,
        int subjectSuffix,
        ulong ordinal,
        bool valid)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var rootId = OpaqueId128.Parse((0x13400 + subjectSuffix).ToString("x32"));
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep);
        var subject = OpaqueId128.Parse((0x13500 + subjectSuffix).ToString("x32"));
        var participants = valid
            ? new[]
            {
                new TransactionParticipantCandidateV1(
                    new StableToken("resident"),
                    TransactionParticipantOutcomeV1.Ready,
                    SHA256.HashData(subject.ToBytes()))
            }
            : Array.Empty<TransactionParticipantCandidateV1>();
        return CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            ordinal,
            participants,
            [new InvariantResultV1(
                new StableToken("sim13.birth.atomic"),
                InvariantSeverityV1.CommitBlocking,
                InvariantOutcomeV1.Pass)]);
    }

    private static WorldStateV1 CreateWorldState(OpaqueId128 worldId, ulong step)
    {
        var zero = new byte[32];
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        var header = new WorldStateHeaderV1(
            worldId,
            step,
            worldSeedDigest: zero,
            configGeneration: 1,
            masterGeneration: 1,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            zero);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
