using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim13CrossDomainTransactionSmoke
{
    internal static void Run()
    {
        VerifyRegistry();
        Sim13GoldenScenarioSmoke.Run();
        VerifyCrimeJusticeRequiredAny();
        VerifyTransactionIdentityPermutation();
        VerifyFutureRootCausalityRejected();
        VerifyInvariantEvidenceBinding();
        VerifyAllTransactionKinds();
        VerifyWorkerCountDeterminismAsync().GetAwaiter().GetResult();
        Sim13StepCandidateTransactionSmoke.Run();
    }

    private static void VerifyRegistry()
    {
        Require(CrossDomainTransactionKindRegistryV1.Entries.Count == CrossDomainTransactionKindRegistryV1.StandardKindCount &&
                CrossDomainTransactionKindRegistryV1.StandardKindCount == 17 &&
                CrossDomainTransactionKindRegistryV1.Registrations.Count == 17 &&
                CrossDomainTransactionKindRegistryV1.Entries.Distinct().Count() == 17,
            "SIM-13 transaction registry must contain exactly 17 stable kinds.");

        foreach (var registration in CrossDomainTransactionKindRegistryV1.Registrations)
        {
            Require(registration.RequiredDomains.Count > 0,
                $"{registration.TransactionKind.Value}: at least one required participant domain is required.");
            Require(!registration.RequiredDomains.Intersect(registration.OptionalDomains).Any(),
                $"{registration.TransactionKind.Value}: required/optional participant sets must not overlap.");
            Require(registration.RequiredAnyDomainGroups.All(static group => group.Count > 0),
                $"{registration.TransactionKind.Value}: required-any participant groups must not be empty.");
            Require(CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(registration.TransactionKind).Count > 0,
                $"{registration.TransactionKind.Value}: required invariant registry coverage is mandatory.");
        }

        var demolition = CrossDomainTransactionKindRegistryV1.GetRegistration(
            CrossDomainTransactionKindRegistryV1.Get("transaction.demolition"));
        Require(
            demolition.RequiredDomains.SequenceEqual([
                new StableToken("physical_built"),
                new StableToken("society_economy")
            ]) &&
            demolition.OptionalDomains.SequenceEqual([
                new StableToken("spatial"),
                new StableToken("governance_security")
            ]),
            "transaction.demolition participant matrix must match the Phase 4 catalog.");

        var crimeJustice = CrossDomainTransactionKindRegistryV1.GetRegistration(
            CrossDomainTransactionKindRegistryV1.Get("transaction.crime-justice"));
        Require(
            crimeJustice.RequiredDomains.SequenceEqual([new StableToken("governance_security")]) &&
            crimeJustice.OptionalDomains.SequenceEqual([new StableToken("society_economy")]) &&
            crimeJustice.RequiredAnyDomainGroups.Count == 1 &&
            crimeJustice.RequiredAnyDomainGroups[0].SequenceEqual([
                new StableToken("physical_built"),
                new StableToken("resident")
            ]),
            "transaction.crime-justice participant matrix must require governance plus resident/physical fact source.");
    }

    private static void VerifyCrimeJusticeRequiredAny()
    {
        const ulong basisStep = 300;
        var worldId = Id("00000000000000000000000000013701");
        var rootId = Id("00000000000000000000000000013702");
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep);
        var subject = Id("00000000000000000000000000013703");
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.crime-justice");
        var passInvariant = Invariant(kind, InvariantOutcomeV1.Pass);
        var governance = ReadyParticipant(kind, new StableToken("governance_security"));
        var resident = ReadyParticipant(kind, new StableToken("resident"));
        var physical = ReadyParticipant(kind, new StableToken("physical_built"));

        var residentFact = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 0,
            [governance, resident],
            [passInvariant]);
        Require(residentFact.CanFinalize && residentFact.Status == TransactionCandidateStatusV1.Valid,
            "transaction.crime-justice: resident fact source must satisfy required-any participant group.");

        var physicalFact = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 0,
            [governance, physical],
            [passInvariant]);
        Require(physicalFact.CanFinalize && physicalFact.Status == TransactionCandidateStatusV1.Valid,
            "transaction.crime-justice: physical fact source must satisfy required-any participant group.");

        var noFactSource = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 0,
            [governance],
            [passInvariant]);
        Require(!noFactSource.CanFinalize &&
                noFactSource.Status == TransactionCandidateStatusV1.Invalid &&
                noFactSource.FailureCode?.Value == "transaction.participant-missing",
            "transaction.crime-justice: missing resident/physical fact source must fail atomically.");

        var failedFactSource = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 0,
            [governance, FailedParticipant(kind, new StableToken("resident"))],
            [passInvariant]);
        Require(!failedFactSource.CanFinalize &&
                failedFactSource.Status == TransactionCandidateStatusV1.Invalid &&
                failedFactSource.FailureCode?.Value == "transaction.required-participant-failed",
            "transaction.crime-justice: failed required-any participant must fail atomically.");
    }

    private static void VerifyTransactionIdentityPermutation()
    {
        var worldId = Id("00000000000000000000000000013001");
        var rootId = Id("00000000000000000000000000013002");
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), 42);
        var subjectLow = Id("00000000000000000000000000013010");
        var subjectHigh = Id("00000000000000000000000000013011");
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.market-sale-delivery");
        var forward = TransactionIdentityV1.Derive(worldId, kind, 42, root, [subjectHigh, subjectLow], 7);
        var reverse = TransactionIdentityV1.Derive(worldId, kind, 42, root, [subjectLow, subjectHigh], 7);
        var expected = Id("784bffff95ab49f037ff0ee72cbf5129");

        Require(forward == expected && reverse == expected,
            "determinism.transaction-id.vector: mv.transaction.v1 golden TransactionId mismatch.");
    }

    private static void VerifyFutureRootCausalityRejected()
    {
        const ulong basisStep = 42;
        var worldId = Id("00000000000000000000000000013021");
        var rootId = Id("00000000000000000000000000013022");
        var futureRoot = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep + 1);
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var participant = ReadyParticipant(kind, new StableToken("resident"));
        var rejected = false;
        try
        {
            _ = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                kind,
                basisStep,
                futureRoot,
                [Id("00000000000000000000000000013023")],
                0,
                [participant],
                [Invariant(kind, InvariantOutcomeV1.Pass)]);
        }
        catch (InvalidDataException ex) when (ex.Message == "transaction.root-causality-future")
        {
            rejected = true;
        }
        Require(rejected,
            "SIM-13 transaction root causality must not reference a future Step.");
    }

    private static void VerifyInvariantEvidenceBinding()
    {
        const ulong basisStep = 77;
        var worldId = Id("00000000000000000000000000013801");
        var rootId = Id("00000000000000000000000000013802");
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep);
        var subject = Id("00000000000000000000000000013803");
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var participant = ReadyParticipant(kind, new StableToken("resident"));
        var refA = new CausalityRefV1(CausalityRefKindV1.Entity, Id("00000000000000000000000000013810").ToBytes(), basisStep);
        var refB = new CausalityRefV1(CausalityRefKindV1.Entity, Id("00000000000000000000000000013811").ToBytes(), basisStep);

        var first = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            0,
            [participant],
            [Invariant(kind, InvariantOutcomeV1.Pass, participantRefs: [refA])]);
        var second = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            0,
            [participant],
            [Invariant(kind, InvariantOutcomeV1.Pass, participantRefs: [refB])]);

        Require(first.TransactionId == second.TransactionId &&
                !first.DiagnosticDigest.SequenceEqual(second.DiagnosticDigest),
            "SIM-13 transaction diagnostic must bind invariant participant refs.");
    }

    private static void VerifyAllTransactionKinds()
    {
        var worldId = Id("00000000000000000000000000013101");
        var rootId = Id("00000000000000000000000000013102");
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), 100);
        var subjects = new[]
        {
            Id("00000000000000000000000000013110"),
            Id("00000000000000000000000000013111"),
        };

        ulong ordinal = 0;
        foreach (var registration in CrossDomainTransactionKindRegistryV1.Registrations)
        {
            var participants = RequiredParticipantDomains(registration)
                .Select(domain => ReadyParticipant(registration.TransactionKind, domain))
                .ToArray();
            var passInvariant = Invariant(registration.TransactionKind, InvariantOutcomeV1.Pass);

            var success = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep: 100,
                root,
                subjects.Reverse(),
                ordinal,
                participants.Reverse(),
                [passInvariant]);
            var replay = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep: 100,
                root,
                subjects,
                ordinal,
                participants,
                [passInvariant]);

            Require(success.CanFinalize && !success.IsAuthoritative &&
                    success.Status == TransactionCandidateStatusV1.Valid &&
                    success.TransactionId == replay.TransactionId &&
                    success.DiagnosticDigest.SequenceEqual(replay.DiagnosticDigest),
                $"{registration.TransactionKind.Value}.success/replay: deterministic candidate mismatch.");

            var missingInvariant = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep: 100,
                root,
                subjects,
                ordinal,
                participants,
                []);
            Require(!missingInvariant.CanFinalize &&
                    missingInvariant.Status == TransactionCandidateStatusV1.Invalid &&
                    missingInvariant.FailureCode?.Value == "transaction.invariant-missing",
                $"{registration.TransactionKind.Value}: missing registered invariant must fail closed.");

            var missing = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep: 100,
                root,
                subjects,
                ordinal,
                participants.Skip(1),
                [passInvariant]);
            Require(!missing.CanFinalize && !missing.IsAuthoritative &&
                    missing.Status == TransactionCandidateStatusV1.Invalid &&
                    missing.FailureCode?.Value == "transaction.participant-missing",
                $"{registration.TransactionKind.Value}.required-participant-failure: transaction must fail atomically.");

            var failedRequiredParticipants = participants.ToArray();
            failedRequiredParticipants[0] = FailedParticipant(
                registration.TransactionKind,
                failedRequiredParticipants[0].DomainToken);
            var failedRequired = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep: 100,
                root,
                subjects,
                ordinal,
                failedRequiredParticipants,
                [passInvariant]);
            Require(!failedRequired.CanFinalize && failedRequired.Status == TransactionCandidateStatusV1.Invalid,
                $"{registration.TransactionKind.Value}.required-participant-failure: failed participant must block finalization.");

            var failedInvariant = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep: 100,
                root,
                subjects,
                ordinal,
                participants,
                [Invariant(registration.TransactionKind, InvariantOutcomeV1.Fail)]);
            Require(!failedInvariant.CanFinalize && !failedInvariant.IsAuthoritative &&
                    failedInvariant.Status == TransactionCandidateStatusV1.Invalid &&
                    failedInvariant.FailureCode?.Value == "transaction.invariant-failed",
                $"{registration.TransactionKind.Value}.invariant-failure: transaction must fail atomically.");

            Require(!success.IsAuthoritative,
                $"{registration.TransactionKind.Value}.crash-before-commit: candidate state must never become authority before durable finalize.");

            ordinal++;
        }
    }

    private static async Task VerifyWorkerCountDeterminismAsync()
    {
        var worldId = Id("00000000000000000000000000013201");
        var rootId = Id("00000000000000000000000000013202");
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), 200);
        var subjects = new[]
        {
            Id("00000000000000000000000000013210"),
            Id("00000000000000000000000000013211"),
        };
        var work = CrossDomainTransactionKindRegistryV1.Registrations
            .Select((registration, index) => new WorkerTransactionCase(registration, (ulong)index))
            .ToArray();

        string[]? baseline = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            foreach (var input in new[] { work, work.Reverse().ToArray() })
            {
                var assembled = await DeterministicBatchExecutor.RunAsync(
                    input,
                    workerCount,
                    (item, _) =>
                    {
                        var participants = RequiredParticipantDomains(item.Registration)
                            .Select(domain => ReadyParticipant(item.Registration.TransactionKind, domain))
                            .Reverse()
                            .ToArray();
                        var candidate = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                            worldId,
                            item.Registration.TransactionKind,
                            200,
                            root,
                            subjects.Reverse(),
                            item.Ordinal,
                            participants,
                            [Invariant(item.Registration.TransactionKind, InvariantOutcomeV1.Pass)]);
                        return ValueTask.FromResult(candidate);
                    });
                var canonical = assembled
                    .OrderBy(static candidate => candidate.TransactionKind.Value, StringComparer.Ordinal)
                    .Select(static candidate => $"{candidate.TransactionKind.Value}:{candidate.TransactionId}:{Convert.ToHexString(candidate.DiagnosticDigest)}")
                    .ToArray();

                baseline ??= canonical;
                Require(canonical.SequenceEqual(baseline),
                    $"SIM-13 worker/input permutation changed transaction assembly at worker-count={workerCount}.");
            }
        }
    }

    private static IEnumerable<StableToken> RequiredParticipantDomains(
        CrossDomainTransactionKindRegistrationV1 registration)
        => registration.RequiredDomains.Concat(
            registration.RequiredAnyDomainGroups.Select(static group => group[0]));

    private static TransactionParticipantCandidateV1 ReadyParticipant(StableToken kind, StableToken domain)
        => Participant(kind, domain, TransactionParticipantOutcomeV1.Ready, "ready", null);

    private static TransactionParticipantCandidateV1 FailedParticipant(StableToken kind, StableToken domain)
        => Participant(
            kind,
            domain,
            TransactionParticipantOutcomeV1.Failed,
            "failed",
            new StableToken("transaction.required-participant-failed"));

    private static TransactionParticipantCandidateV1 Participant(
        StableToken kind,
        StableToken domain,
        TransactionParticipantOutcomeV1 outcome,
        string suffix,
        StableToken? diagnosticCode)
    {
        var entry = StandardDomainExecutionPlanV1.Create().Entries.Single(item => item.DomainToken == domain);
        var partitionId = entry.OwnedPartitions[0];
        var intentId = HashSuite.Trunc128(SHA256.HashData(Encoding.ASCII.GetBytes(
            kind.Value + ":" + domain.Value + ":" + suffix)));
        return new TransactionParticipantCandidateV1(
            domain,
            partitionId,
            [intentId],
            required: true,
            outcome,
            SHA256.HashData(Encoding.ASCII.GetBytes(kind.Value + ":" + domain.Value + ":" + suffix)),
            diagnosticCode);
    }

    private static InvariantResultV1 Invariant(
        StableToken kind,
        InvariantOutcomeV1 outcome,
        InvariantSeverityV1 severity = InvariantSeverityV1.CommitBlocking,
        IEnumerable<CausalityRefV1>? participantRefs = null,
        StableToken? diagnosticCode = null)
        => new(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            severity,
            outcome,
            participantRefs,
            diagnosticCode);

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record WorkerTransactionCase(
        CrossDomainTransactionKindRegistrationV1 Registration,
        ulong Ordinal);
}
