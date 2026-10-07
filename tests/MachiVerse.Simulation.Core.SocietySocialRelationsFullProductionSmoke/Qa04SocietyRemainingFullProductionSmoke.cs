using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyRemainingFullProductionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Console.WriteLine("Validating final canonical Society authority (89,800 records)...");
        Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04SocietyRemainingCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.BusinessProductions.ItemCount == Qa04SocietyRemainingCanonicalAuthorityV1.BusinessCount,
            "BusinessProduction proof must materialize exactly 30,000 records.");
        Require(materialization.LogisticsObligations.ItemCount == Qa04SocietyRemainingCanonicalAuthorityV1.LogisticsCount,
            "LogisticsObligation proof must materialize exactly 40,000 records.");
        Require(materialization.HistoryLineages.ItemCount == Qa04SocietyRemainingCanonicalAuthorityV1.HistoryCount,
            "HistoryLineage proof must materialize exactly 19,800 records.");
        Require(materialization.MaterializedRecordCount == 89_800,
            "Remaining Society proof must materialize exactly 89,800 records.");
        Require(materialization.Organizations.ItemCount == 10_000,
            "Remaining Society proof must bind to the exact 10,000 canonical Organizations.");

        var snapshot = Qa04SocietyRemainingSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(snapshot.BusinessProductionCount == Qa04SocietyRemainingCanonicalAuthorityV1.BusinessCount &&
                snapshot.LogisticsObligationCount == Qa04SocietyRemainingCanonicalAuthorityV1.LogisticsCount &&
                snapshot.HistoryLineageCount == Qa04SocietyRemainingCanonicalAuthorityV1.HistoryCount,
            "Remaining Society Snapshot recovery must preserve exact semantic counts.");

        VerifyBusinessFailClosed(materialization);
        VerifyLogisticsFailClosed(materialization);
        VerifyHistoryFailClosed(materialization);

        Console.WriteLine(
            $"society-remaining-full-production-pass business={materialization.BusinessProductions.ItemCount} " +
            $"logistics={materialization.LogisticsObligations.ItemCount} history={materialization.HistoryLineages.ItemCount} " +
            $"recovered={snapshot.BusinessProductionCount + snapshot.LogisticsObligationCount + snapshot.HistoryLineageCount}");
    }

    private static void VerifyBusinessFailClosed(Qa04SocietyRemainingCanonicalMaterializationV1 materialization)
    {
        var first = materialization.BusinessRecordsByOrdinal[0];
        var wrongOrganization = Copy(first, first.Payload with
        {
            OrganizationRef = Qa04SocietyRemainingCanonicalAuthorityV1.OrganizationRef(1),
        });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalBusinessRecord(
                0, wrongOrganization, materialization.References),
            "BusinessProduction wrong Organization relation must fail closed.");

        var wrongToken = Copy(first, first.Payload with { RecipeToken = new StableToken("perf.invalid-production-plan") });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalBusinessRecord(
                0, wrongToken, materialization.References),
            "BusinessProduction recipe token drift must fail closed.");

        var wrongStatus = Copy(first, first.Payload with { Status = new StableToken("active") });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalBusinessRecord(
                0, wrongStatus, materialization.References),
            "BusinessProduction status drift must fail closed.");

        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalBusinessRecord(
                0, first, new MissingReferenceResolver()),
            "BusinessProduction missing Organization authority must fail closed.");
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateBusinessUniqueness(new[] { first, first }),
            "BusinessProduction duplicate RecordId must fail closed.");
    }

    private static void VerifyLogisticsFailClosed(Qa04SocietyRemainingCanonicalMaterializationV1 materialization)
    {
        var first = materialization.LogisticsRecordsByOrdinal[0];
        var second = materialization.LogisticsRecordsByOrdinal[1];

        var wrongCargo = Copy(first, first.Payload with { CargoRefs = second.Payload.CargoRefs });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalLogisticsRecord(
                0, wrongCargo, materialization.FacilityAuthority, materialization.References),
            "Logistics wrong cargo relation must fail closed.");

        var reversedFacility = Copy(first, first.Payload with
        {
            OriginRef = first.Payload.DestinationRef,
            DestinationRef = first.Payload.OriginRef,
        });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalLogisticsRecord(
                0, reversedFacility, materialization.FacilityAuthority, materialization.References),
            "Logistics reversed facility relation must fail closed.");

        var wrongStatus = Copy(first, first.Payload with { Status = new StableToken("delivered") });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalLogisticsRecord(
                0, wrongStatus, materialization.FacilityAuthority, materialization.References),
            "Logistics status drift must fail closed.");

        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalLogisticsRecord(
                0, first, materialization.FacilityAuthority, new MissingReferenceResolver()),
            "Logistics missing Organization/Physical/Built authority must fail closed.");
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateLogisticsUniqueness(new[] { first, first }),
            "Logistics duplicate RecordId/cargo relation must fail closed.");
    }

    private static void VerifyHistoryFailClosed(Qa04SocietyRemainingCanonicalMaterializationV1 materialization)
    {
        var first = materialization.HistoryRecordsByOrdinal[0];
        var wrongSubject = Copy(first, first.Payload with
        {
            SubjectRef = Qa04SocietyRemainingCanonicalAuthorityV1.BusinessRef(1),
        });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalHistoryRecord(
                0, wrongSubject, materialization.BusinessRecordsByOrdinal, materialization.References),
            "HistoryLineage wrong BusinessProduction subject must fail closed.");

        var corruptDigest = first.Payload.CausalityDigest.ToArray();
        corruptDigest[0] ^= 0xff;
        var wrongDigest = Copy(first, first.Payload with { CausalityDigest = corruptDigest });
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalHistoryRecord(
                0, wrongDigest, materialization.BusinessRecordsByOrdinal, materialization.References),
            "HistoryLineage causality digest drift must fail closed.");

        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateCanonicalHistoryRecord(
                0, first, materialization.BusinessRecordsByOrdinal, new MissingReferenceResolver()),
            "HistoryLineage missing BusinessProduction authority must fail closed.");
        RequireInvalid(
            () => Qa04SocietyRemainingCanonicalAuthorityV1.ValidateHistoryUniqueness(new[] { first, first }),
            "HistoryLineage duplicate RecordId/subject relation must fail closed.");
    }

    private static DomainRecordEnvelopeV1<TPayload> Copy<TPayload>(
        DomainRecordEnvelopeV1<TPayload> source,
        TPayload payload)
        => new(
            source.RecordId,
            source.RecordSchema,
            source.Revision,
            source.CreatedStep,
            source.RetiredStep,
            source.DetailLevel,
            source.LineageRef,
            payload);

    private static void RequireInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class MissingReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }
}
