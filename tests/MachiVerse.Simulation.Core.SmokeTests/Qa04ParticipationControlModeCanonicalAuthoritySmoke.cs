using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04ParticipationControlModeCanonicalMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04ParticipationControlModeCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == 1_000_000 &&
                materialization.Partition.ItemCount == 1_000_000 &&
                materialization.TransactionParticipantRecordIds.Count == 1_000_000,
            "Canonical participation.control_mode authority must materialize exactly 1,000,000 records.");

        var records = materialization.Partition.RecordsCanonical.ToArray();
        Require(records.Count(static record => record.DetailLevel == DetailLevelV1.D0Entity) == 100_000 &&
                records.Count(static record => record.DetailLevel == DetailLevelV1.D1LocalAggregate) == 300_000 &&
                records.Count(static record => record.DetailLevel == DetailLevelV1.D2RegionalAggregate) == 400_000 &&
                records.Count(static record => record.DetailLevel == DetailLevelV1.D3BoundarySummary) == 200_000,
            "Canonical participation.control_mode DetailLevel distribution must mirror Resident authority.");
        Require(records.All(static record =>
                record.Payload.BindingRef is null &&
                record.Payload.Mode.Value == "autonomous" &&
                record.Payload.EffectiveFrom == 0 &&
                record.Payload.InputAuthorityGeneration == 0),
            "Canonical participation.control_mode genesis payload drifted.");
        Require(records.Select(static record => record.Payload.ResidentRef).Distinct().Count() == 1_000_000,
            "Canonical participation.control_mode must contain exactly one effective record per Resident.");

        var measurement = Qa04ParticipationControlModeSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(measurement.RecoveredRecordCount == 1_000_000 &&
                measurement.EncodedFragmentPayloadBytes > 0 &&
                measurement.FragmentCount > 0,
            "Canonical participation.control_mode Snapshot/recovery/measurement proof failed.");

        var pools = CreateTransactionPools(materialization.TransactionParticipantRecordIds);
        var transactions = Qa04CrossDomainTransactionGenesisMaterializerV1.Materialize(pools);
        var participationTargets = transactions
            .SelectMany(static binding => binding.ParticipantTargetRefs)
            .Where(static reference => reference.PartitionId.Value == ParticipationControlModePayloadV1.PartitionId)
            .ToArray();
        Require(participationTargets.Length > 0 &&
                participationTargets.All(materialization.References.Exists),
            "Transaction participant binding must use actual canonical participation.control_mode records.");

        var first = records[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                ParticipationControlModePayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Missing Resident authority must fail closed.");

        var wrongResidentPayload = first.Payload with
        {
            ResidentRef = new PartitionRecordRefV1(ParticipationControlModePayloadV1.PartitionId, first.RecordId),
        };
        ExpectInvalid(() => Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, wrongResidentPayload),
                materialization.References),
            "Wrong resident_ref partition/identity must fail closed.");

        var invalidModePayload = first.Payload with { Mode = new StableToken("invalid-control-mode") };
        ExpectInvalid(() => Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, invalidModePayload),
                materialization.References),
            "Invalid control-mode Token must fail closed.");

        var generationDriftPayload = first.Payload with { InputAuthorityGeneration = 1 };
        ExpectInvalid(() => Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateCanonicalRecord(
                0,
                CopyWithPayload(first, generationDriftPayload),
                materialization.References),
            "Genesis input_authority_generation drift must fail closed.");

        var second = records[1];
        var duplicateResident = CopyWithPayload(second, second.Payload with { ResidentRef = first.Payload.ResidentRef });
        ExpectInvalid(() => Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateResidentUniqueness(
                new[] { first, duplicateResident }),
            "Duplicate Resident control-mode authority must fail closed.");

        ExpectInvalid(() => Qa04ParticipationControlModeCanonicalAuthorityV1.ValidateResidentUniqueness(
                new[] { first, first }),
            "Duplicate control-mode RecordId must fail closed.");
    }

    private static DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> CopyWithPayload(
        DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> record,
        ParticipationControlModePayloadV1 payload)
        => new(
            record.RecordId,
            record.RecordSchema,
            record.Revision,
            record.CreatedStep,
            record.RetiredStep,
            record.DetailLevel,
            record.LineageRef,
            payload);

    private static IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> CreateTransactionPools(
        IReadOnlyList<OpaqueId128> participationPool)
    {
        var names = new[]
        {
            "spatial.scope_registry",
            "environment.hazard",
            "physical.presence",
            "participation.control_mode",
            "resident.identity_lifecycle",
            "society.contract_claim",
            "governance.permission_license",
            "infrastructure.service_queue",
        };
        return names.Select((name, index) => new
            {
                name,
                ids = name == ParticipationControlModePayloadV1.PartitionId
                    ? participationPool
                    : (IReadOnlyList<OpaqueId128>)Array.AsReadOnly(new[]
                    {
                        OpaqueId128.Parse((0x58000 + index * 4 + 1).ToString("x32")),
                        OpaqueId128.Parse((0x58000 + index * 4 + 2).ToString("x32")),
                        OpaqueId128.Parse((0x58000 + index * 4 + 3).ToString("x32")),
                    }),
            })
            .ToDictionary(static item => item.name, static item => item.ids, StringComparer.Ordinal);
    }

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
