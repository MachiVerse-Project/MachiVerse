using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureTailFullProductionSmoke
{
    internal static ulong VerifiedRecordCount { get; private set; }

    [ModuleInitializer]
    internal static void Run()
    {
        Console.WriteLine("Validating final canonical Infrastructure/Information authority (19,900 records)...");
        Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04InfrastructureTailCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.AddressPlaceIndexes.ItemCount == Qa04InfrastructureTailCanonicalAuthorityV1.AddressCount,
            "AddressPlaceIndex proof must materialize exactly 5,000 records.");
        Require(materialization.FailureRecoveries.ItemCount == Qa04InfrastructureTailCanonicalAuthorityV1.FailureRecoveryCount,
            "FailureRecovery proof must materialize exactly 10,000 records.");
        Require(materialization.Lineages.ItemCount == Qa04InfrastructureTailCanonicalAuthorityV1.LineageCount,
            "Infrastructure Lineage proof must materialize exactly 4,900 records.");
        Require(materialization.MaterializedTailRecordCount == 19_900 && 480_100UL + materialization.MaterializedTailRecordCount == 500_000,
            "Infrastructure tail proof must close the 500,000-record decomposition exactly.");

        var snapshot = Qa04InfrastructureTailSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(snapshot.AddressPlaceIndexCount == Qa04InfrastructureTailCanonicalAuthorityV1.AddressCount &&
                snapshot.FailureRecoveryCount == Qa04InfrastructureTailCanonicalAuthorityV1.FailureRecoveryCount &&
                snapshot.LineageCount == Qa04InfrastructureTailCanonicalAuthorityV1.LineageCount,
            "Infrastructure tail Snapshot recovery must preserve exact semantic counts.");

        VerifiedRecordCount = checked(snapshot.AddressPlaceIndexCount + snapshot.FailureRecoveryCount + snapshot.LineageCount);
        Require(VerifiedRecordCount == Qa04InfrastructureTailCanonicalAuthorityV1.CanonicalCount,
            "Infrastructure tail verified record count drifted.");

        VerifyAddressFailClosed(materialization);
        VerifyFailureRecoveryFailClosed(materialization);
        VerifyLineageFailClosed(materialization);

        Console.WriteLine(
            $"infrastructure-tail-full-production-pass address={materialization.AddressPlaceIndexes.ItemCount} " +
            $"failure={materialization.FailureRecoveries.ItemCount} lineage={materialization.Lineages.ItemCount} " +
            $"recovered={VerifiedRecordCount}");
    }

    private static void VerifyAddressFailClosed(Qa04InfrastructureTailCanonicalMaterializationV1 materialization)
    {
        var first = materialization.AddressRecordsByOrdinal[0];
        var wrongPlacePayload = first.Payload with
        {
            PlaceRef = Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef(1),
        };
        var wrongPlace = Clone(first, wrongPlacePayload);
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalAddressRecord(
                0, wrongPlace, materialization.FacilityAuthority, materialization.References),
            "AddressPlaceIndex ordinal/place mismatch must fail closed.");

        var wrongToken = Clone(first, first.Payload with { AddressToken = new StableToken("perf.address.invalid") });
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalAddressRecord(
                0, wrongToken, materialization.FacilityAuthority, materialization.References),
            "AddressPlaceIndex token drift must fail closed.");

        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalAddressRecord(
                0, first, materialization.FacilityAuthority, new MissingReferenceResolver()),
            "AddressPlaceIndex missing place/scope authority must fail closed.");
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateAddressUniqueness(new[] { first, first }),
            "AddressPlaceIndex duplicate identity/relation must fail closed.");
    }

    private static void VerifyFailureRecoveryFailClosed(Qa04InfrastructureTailCanonicalMaterializationV1 materialization)
    {
        var first = materialization.FailureRecordsByOrdinal[0];
        var wrongSubject = Clone(first, first.Payload with
        {
            SubjectRef = materialization.FacilityAuthority.CanonicalServicePool[1],
        });
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalFailureRecoveryRecord(
                0, wrongSubject, materialization.FacilityAuthority, materialization.References),
            "FailureRecovery ordinal/subject mismatch must fail closed.");

        var wrongStatus = Clone(first, first.Payload with { Status = new StableToken("active") });
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalFailureRecoveryRecord(
                0, wrongStatus, materialization.FacilityAuthority, materialization.References),
            "FailureRecovery restored-genesis scalar/token drift must fail closed.");

        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalFailureRecoveryRecord(
                0, first, materialization.FacilityAuthority, new MissingReferenceResolver()),
            "FailureRecovery missing service authority must fail closed.");
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateFailureRecoveryUniqueness(new[] { first, first }),
            "FailureRecovery duplicate identity/relation must fail closed.");
    }

    private static void VerifyLineageFailClosed(Qa04InfrastructureTailCanonicalMaterializationV1 materialization)
    {
        var first = materialization.LineageRecordsByOrdinal[0];
        var wrongSubject = Clone(first, first.Payload with
        {
            SubjectRef = Qa04FacilityServiceCanonicalAuthorityV1.FacilityServiceRef(1),
        });
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalLineageRecord(
                0, wrongSubject, materialization.FacilityAuthority, materialization.References),
            "Infrastructure Lineage ordinal/subject mismatch must fail closed.");

        var corruptedDigest = first.Payload.SourceDigest.ToArray();
        corruptedDigest[0] ^= 0xff;
        var wrongDigest = Clone(first, first.Payload with { SourceDigest = corruptedDigest });
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalLineageRecord(
                0, wrongDigest, materialization.FacilityAuthority, materialization.References),
            "Infrastructure Lineage source-digest drift must fail closed.");

        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateCanonicalLineageRecord(
                0, first, materialization.FacilityAuthority, new MissingReferenceResolver()),
            "Infrastructure Lineage missing FacilityService authority must fail closed.");
        RequireInvalid(
            () => Qa04InfrastructureTailCanonicalAuthorityV1.ValidateLineageUniqueness(new[] { first, first }),
            "Infrastructure Lineage duplicate identity/relation must fail closed.");
    }

    private static DomainRecordEnvelopeV1<TPayload> Clone<TPayload>(
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
