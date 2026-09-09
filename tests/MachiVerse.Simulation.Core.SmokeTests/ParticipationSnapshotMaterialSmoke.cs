using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class ParticipationSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
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

        if (section.LogicalItemCount != 1 || restored.LogicalItemCount != 1)
            throw new InvalidOperationException("Participation Snapshot provider must preserve actual item count.");
        if (!section.LogicalContentDigest.SequenceEqual(header.CanonicalDigest) ||
            !restored.LogicalContentDigest.SequenceEqual(header.CanonicalDigest))
            throw new InvalidOperationException("Participation Snapshot recovery must recompute the frozen partition digest.");

        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            ParticipationAbsencePolicyPayloadV1.PartitionId,
            section.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var restoredPayload = ParticipationAbsencePolicyPayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        if (restoredPayload.PriorityRules.Count != 2 ||
            restoredPayload.PriorityRules[0].RuleId.Value != "safety" ||
            restoredPayload.PriorityRules[1].RuleId.Value != "routine" ||
            !restoredPayload.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()))
        {
            throw new InvalidOperationException("Participation nested payload semantic digest did not round-trip canonically.");
        }
    }
}
