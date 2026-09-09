using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class ParticipationSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        VerifyTypedEmptyMaterial();
        VerifyAbsencePolicyActualMaterial();
    }

    private static void VerifyTypedEmptyMaterial()
    {
        var frozen = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1).WorldState;
        var state = ParticipationDomainStateV1.CreateEmpty();
        var material = state.BindSnapshotMaterial(frozen);
        var typedEmpty = ParticipationDomainSnapshotMaterialV1.BindTypedEmpty(
            frozen,
            state.Binding,
            state.AbsencePolicy,
            state.ControlMode,
            state.History,
            state.DetailRequirement);

        Require(material.Authorities.Count == 5 && material.Authorities.All(static authority => authority.ActualItemCount == 0),
            "Participation runtime state must bind all five typed empty actual partition roots.");
        Require(typedEmpty.Authorities.Select(static authority => authority.PartitionId.Value)
                .SequenceEqual(material.Authorities.Select(static authority => authority.PartitionId.Value)),
            "Typed-empty guard must bind the same runtime-owned Participation roots.");

        var providers = ParticipationDomainSnapshotProviderV1.CreateAll()
            .ToDictionary(static provider => provider.SectionId, StringComparer.Ordinal);
        foreach (var authority in material.Authorities)
        {
            var provider = providers[authority.PartitionId.Value];
            var section = provider.Create(authority);
            Require(section.LogicalItemCount == 0 &&
                    section.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest) &&
                    section.Fragments.Count == 1,
                $"Actual empty Participation partition must emit exactly one canonical fragment: {authority.PartitionId.Value}.");
            var fragment = section.Fragments[0];
            Require(fragment.ItemCount == 0 && fragment.FirstRecordId is null && fragment.LastRecordId is null,
                $"Actual empty Participation fragment must not fabricate record ranges: {authority.PartitionId.Value}.");

            var semantic = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(semantic.LogicalItemCount == 0 &&
                    semantic.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Actual empty Participation recovery must recompute the frozen canonical digest: {authority.PartitionId.Value}.");
        }

        var identity = StandardDomainPartitionRegistry.Get(ParticipationAbsencePolicyPayloadV1.PartitionId);
        var payload = new ParticipationAbsencePolicyPayloadV1(
            OpaqueId128.Parse("0000000000000000000000000001e001"),
            PolicyGeneration: 1,
            PriorityRules: Array.AsReadOnly(new[] { new ParticipationPolicyRuleV1(0, new StableToken("default")) }),
            EffectiveFrom: 0,
            EffectiveUntil: null);
        var record = new DomainRecordEnvelopeV1<ParticipationAbsencePolicyPayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000001e002"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var nonempty = new DomainPartitionStateV1<ParticipationAbsencePolicyPayloadV1>(identity, new[] { record });
        ExpectInvalid(
            "nonempty partition cannot be bound as typed empty material",
            () => _ = ParticipationDomainSnapshotMaterialV1.BindTypedEmpty(
                frozen,
                state.Binding,
                nonempty,
                state.ControlMode,
                state.History,
                state.DetailRequirement));
    }

    private static void VerifyAbsencePolicyActualMaterial()
    {
        var identity = StandardDomainPartitionRegistry.Get(ParticipationAbsencePolicyPayloadV1.PartitionId);
        var payload = new ParticipationAbsencePolicyPayloadV1(
            OpaqueId128.Parse("0000000000000000000000000002a001"),
            PolicyGeneration: 1,
            PriorityRules: Array.AsReadOnly(new[]
            {
                new ParticipationPolicyRuleV1(-10, new StableToken("safety")),
                new ParticipationPolicyRuleV1(20, new StableToken("routine")),
            }),
            EffectiveFrom: 10,
            EffectiveUntil: null);
        var record = new DomainRecordEnvelopeV1<ParticipationAbsencePolicyPayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000002a101"),
            identity.RecordSchema,
            revision: 1,
            createdStep: 10,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
        var partition = new DomainPartitionStateV1<ParticipationAbsencePolicyPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 10,
            detailLevel: DetailLevelV1.D0Entity,
            static value => value.CanonicalDigest());
        var authority = new DomainPartitionSnapshotAuthorityV1<ParticipationAbsencePolicyPayloadV1>(
            partition,
            header,
            static value => value.CanonicalDigest());

        var provider = ParticipationDomainSnapshotProviderV1.CreateAll()
            .Single(static value => value.SectionId == ParticipationAbsencePolicyPayloadV1.PartitionId);
        var section = provider.Create(authority);
        var restored = provider.CreateSemanticVerifier(header).Verify(section.Fragments);

        Require(section.LogicalItemCount == 1 && restored.LogicalItemCount == 1,
            "Participation Snapshot provider must preserve actual item count.");
        Require(section.LogicalContentDigest.SequenceEqual(header.CanonicalDigest) &&
                restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest),
            "Participation Snapshot recovery must recompute the frozen partition digest.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            ParticipationAbsencePolicyPayloadV1.PartitionId,
            section.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = ParticipationAbsencePolicyPayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(restoredPayload.PriorityRules.Count == 2 &&
                restoredPayload.PriorityRules[0].RuleId.Value == "safety" &&
                restoredPayload.PriorityRules[1].RuleId.Value == "routine" &&
                restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Participation nested payload semantic digest did not round-trip canonically.");
    }

    private static void ExpectInvalid(string name, Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
