using System.Security.Cryptography;
using System.Text;
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
        var resident = new StableToken("resident");
        var residentPartitionId = new StableToken("resident.identity_lifecycle");
        var residentPartition = new PartitionCandidateV1(
            residentPartitionId,
            resident,
            basisRevision: 1,
            basisStep,
            SHA256.HashData("sim13-resident-birth-effects"u8));
        var intentA = BirthIntent(basisStep, 1);
        var intentB = BirthIntent(basisStep, 2);
        var intents = new[] { intentA, intentB };
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => entry.DomainToken == resident
                ? new DomainCandidateOutputV1(entry.DomainToken, basisStep, intents, [residentPartition])
                : new DomainCandidateOutputV1(entry.DomainToken, basisStep))
            .ToArray();
        var resolutions = DeterministicIntentMergerV1.GroupByConflictScope(intents, basisStep)
            .Select(DeterministicIntentMergerV1.ResolveSequential)
            .ToArray();

        var validA = BirthTransaction(
            worldId,
            basisStep,
            1,
            0,
            participantPresent: true,
            residentPartition.CandidateDigest,
            intentA.IntentId);
        var validB = BirthTransaction(
            worldId,
            basisStep,
            2,
            1,
            participantPresent: true,
            residentPartition.CandidateDigest,
            intentB.IntentId);
        var candidateA = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013390"),
            state,
            frozen,
            outputs,
            resolutions,
            transactionCandidates: [validB, validA]);
        var candidateB = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013391"),
            state,
            frozen,
            outputs.Reverse(),
            resolutions.Reverse(),
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

        var invalid = BirthTransaction(
            worldId,
            basisStep,
            3,
            2,
            participantPresent: false,
            residentPartition.CandidateDigest,
            intentA.IntentId);
        var blocked = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013392"),
            state,
            frozen,
            outputs,
            resolutions,
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
            var future = BirthTransaction(
                worldId,
                basisStep + 1,
                4,
                3,
                participantPresent: true,
                residentPartition.CandidateDigest,
                intentA.IntentId);
            _ = StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000013393"),
                state,
                frozen,
                outputs,
                resolutions,
                transactionCandidates: [future]);
        }
        catch (InvalidDataException ex) when (ex.Message == "step-candidate.transaction-basis-mismatch")
        {
            basisMismatchRejected = true;
        }
        Require(basisMismatchRejected,
            "SIM-13 StepCandidate must reject transaction candidates from a different basis Step.");

        var worldMismatchRejected = false;
        try
        {
            var foreign = BirthTransaction(
                OpaqueId128.Parse("000000000000000000000000000133ff"),
                basisStep,
                5,
                4,
                participantPresent: true,
                residentPartition.CandidateDigest,
                intentA.IntentId);
            _ = StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000013394"),
                state,
                frozen,
                outputs,
                resolutions,
                transactionCandidates: [foreign]);
        }
        catch (InvalidDataException ex) when (ex.Message == "step-candidate.transaction-world-mismatch")
        {
            worldMismatchRejected = true;
        }
        Require(worldMismatchRejected,
            "SIM-13 StepCandidate must reject a transaction assembled for another world.");

        var wrongEffectRejected = false;
        try
        {
            var wrongEffect = BirthTransaction(
                worldId,
                basisStep,
                6,
                5,
                participantPresent: true,
                SHA256.HashData("unrelated-partition-effect"u8),
                intentA.IntentId);
            _ = StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000013395"),
                state,
                frozen,
                outputs,
                resolutions,
                transactionCandidates: [wrongEffect]);
        }
        catch (InvalidDataException ex) when (ex.Message == "step-candidate.transaction-participant-effect-digest-mismatch")
        {
            wrongEffectRejected = true;
        }
        Require(wrongEffectRejected,
            "SIM-13 StepCandidate must bind transaction candidate effect digest to the actual partition candidate.");

        var missingIntentRejected = false;
        try
        {
            var missingIntent = BirthTransaction(
                worldId,
                basisStep,
                7,
                6,
                participantPresent: true,
                residentPartition.CandidateDigest,
                OpaqueId128.Parse("00000000000000000000000000013999"));
            _ = StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000013396"),
                state,
                frozen,
                outputs,
                resolutions,
                transactionCandidates: [missingIntent]);
        }
        catch (InvalidDataException ex) when (ex.Message == "step-candidate.transaction-participant-intent-missing")
        {
            missingIntentRejected = true;
        }
        Require(missingIntentRejected,
            "SIM-13 StepCandidate must reject transaction participant intent refs absent from the assembled Step.");

        var fatalTransaction = BirthTransaction(
            worldId,
            basisStep,
            8,
            7,
            participantPresent: true,
            residentPartition.CandidateDigest,
            intentA.IntentId,
            invariantOutcome: InvariantOutcomeV1.Fail,
            invariantSeverity: InvariantSeverityV1.FatalAuthority);
        var fatalBlocked = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013397"),
            state,
            frozen,
            outputs,
            resolutions,
            transactionCandidates: [fatalTransaction]);
        var atomicity = fatalBlocked.InvariantResults.Single(result => result.InvariantId.Value == "transaction.atomicity");
        Require(!fatalBlocked.CommitDecision.CanCommit &&
                fatalBlocked.CommitDecision.FatalAuthorityFailure &&
                atomicity.Severity == InvariantSeverityV1.FatalAuthority &&
                atomicity.Outcome == InvariantOutcomeV1.Fail,
            "SIM-13 fatal transaction invariant must remain FatalAuthority at the Step barrier.");

        Sim13DurableAtomicitySmoke.Run();
    }

    private static MutationIntentCandidateV1 BirthIntent(ulong basisStep, int subjectSuffix)
    {
        var resident = new StableToken("resident");
        var partition = new StableToken("resident.identity_lifecycle");
        var subject = OpaqueId128.Parse((0x13500 + subjectSuffix).ToString("x32"));
        var intentId = OpaqueId128.Parse((0x13600 + subjectSuffix).ToString("x32"));
        return new MutationIntentCandidateV1(
            intentId,
            phase: 3,
            sourceDomain: resident,
            targetDomain: resident,
            targetPartitionId: partition,
            basisStep,
            new StableToken("resident.intent.lifecycle-transition"),
            new ConflictScopeV1(
                resident,
                new StableToken("resident"),
                subject.ToBytes(),
                new StableToken("lifecycle")),
            semanticPriority: 0,
            ConflictResolutionModeV1.Sequential,
            SHA256.HashData(subject.ToBytes()));
    }

    private static CrossDomainTransactionCandidateV1 BirthTransaction(
        OpaqueId128 worldId,
        ulong basisStep,
        int subjectSuffix,
        ulong ordinal,
        bool participantPresent,
        byte[] candidateEffectDigest,
        OpaqueId128 intentId,
        InvariantOutcomeV1 invariantOutcome = InvariantOutcomeV1.Pass,
        InvariantSeverityV1 invariantSeverity = InvariantSeverityV1.CommitBlocking)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var rootId = OpaqueId128.Parse((0x13400 + subjectSuffix).ToString("x32"));
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep);
        var subject = OpaqueId128.Parse((0x13500 + subjectSuffix).ToString("x32"));
        var participants = participantPresent
            ? new[]
            {
                new TransactionParticipantCandidateV1(
                    new StableToken("resident"),
                    new StableToken("resident.identity_lifecycle"),
                    [intentId],
                    required: true,
                    TransactionParticipantOutcomeV1.Ready,
                    candidateEffectDigest)
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
                CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
                invariantSeverity,
                invariantOutcome)]);
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
                canonicalDigest: SHA256.HashData(Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
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
