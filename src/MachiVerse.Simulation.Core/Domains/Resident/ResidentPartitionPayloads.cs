using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Resident;

public sealed record ResidentIdentityLifecyclePayloadV1(
    OpaqueId128 ResidentId,
    StableToken Lifecycle,
    ulong? BirthStep,
    ulong? DeathStep,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    uint LineageGeneration,
    StableToken ProfileToken)
{
    public const string PartitionId = "resident.identity_lifecycle";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = ResidentPayloadFields.Map(
            ("resident_id", ResidentId),
            ("lifecycle", Lifecycle.Value),
            ("parent_refs", ParentRefs),
            ("lineage_generation", LineageGeneration),
            ("profile_token", ProfileToken.Value));
        ResidentPayloadFields.AddOptional(values, "birth_step", BirthStep);
        ResidentPayloadFields.AddOptional(values, "death_step", DeathStep);
        return values;
    }
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentIdentityLifecyclePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<OpaqueId128>(values, PartitionId, "resident_id"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "lifecycle")),
        ResidentPayloadFields.Optional<ulong>(values, "birth_step"),
        ResidentPayloadFields.Optional<ulong>(values, "death_step"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "lineage_generation"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "profile_token")));
}

public sealed record ResidentBodyHealthPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    uint DevelopmentPpm,
    uint HealthCapacityPpm,
    IReadOnlyList<ICanonicalDomainNestedValueV1> BodyRegionStates,
    IReadOnlyList<PartitionRecordRefV1> InjuryRefs,
    IReadOnlyList<PartitionRecordRefV1> DiseaseRefs,
    uint RecoveryPpm)
{
    public const string PartitionId = "resident.body_health";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("development_ppm", DevelopmentPpm),
        ("health_capacity_ppm", HealthCapacityPpm),
        ("body_region_states", BodyRegionStates),
        ("injury_refs", InjuryRefs),
        ("disease_refs", DiseaseRefs),
        ("recovery_ppm", RecoveryPpm));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentBodyHealthPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "development_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "health_capacity_ppm"),
        ResidentPayloadFields.Required<IReadOnlyList<ICanonicalDomainNestedValueV1>>(values, PartitionId, "body_region_states"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "injury_refs"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "disease_refs"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "recovery_ppm"));
}

public sealed record ResidentPhysiologyPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    uint HungerPpm,
    uint ThirstPpm,
    uint FatiguePpm,
    uint SleepPressurePpm,
    uint ThermalStressPpm,
    uint HygienePpm)
{
    public const string PartitionId = "resident.physiology";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("hunger_ppm", HungerPpm),
        ("thirst_ppm", ThirstPpm),
        ("fatigue_ppm", FatiguePpm),
        ("sleep_pressure_ppm", SleepPressurePpm),
        ("thermal_stress_ppm", ThermalStressPpm),
        ("hygiene_ppm", HygienePpm));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentPhysiologyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "hunger_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "thirst_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "fatigue_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "sleep_pressure_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "thermal_stress_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "hygiene_ppm"));
}

public sealed record ResidentPerceptionPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    IReadOnlyList<PartitionRecordRefV1> AttentionTargetRefs,
    IReadOnlyList<ICanonicalDomainNestedValueV1> PerceivedFacts,
    uint SensoryCapacityPpm,
    ulong BasisStep)
{
    public const string PartitionId = "resident.perception";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("attention_target_refs", AttentionTargetRefs),
        ("perceived_facts", PerceivedFacts),
        ("sensory_capacity_ppm", SensoryCapacityPpm),
        ("basis_step", BasisStep));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentPerceptionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "attention_target_refs"),
        ResidentPayloadFields.Required<IReadOnlyList<ICanonicalDomainNestedValueV1>>(values, PartitionId, "perceived_facts"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "sensory_capacity_ppm"),
        ResidentPayloadFields.Required<ulong>(values, PartitionId, "basis_step"));
}

public sealed record ResidentKnowledgeBeliefPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    PartitionRecordRefV1 SubjectRef,
    StableToken PropositionToken,
    uint ConfidencePpm,
    IReadOnlyList<PartitionRecordRefV1> EvidenceRefs,
    ulong LastUpdatedStep)
{
    public const string PartitionId = "resident.knowledge_belief";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("subject_ref", SubjectRef),
        ("proposition_token", PropositionToken.Value),
        ("confidence_ppm", ConfidencePpm),
        ("evidence_refs", EvidenceRefs),
        ("last_updated_step", LastUpdatedStep));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentKnowledgeBeliefPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "proposition_token")),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "confidence_ppm"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "evidence_refs"),
        ResidentPayloadFields.Required<ulong>(values, PartitionId, "last_updated_step"));
}

public sealed record ResidentMemoryPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    StableToken MemoryKind,
    IReadOnlyList<PartitionRecordRefV1> SubjectRefs,
    ulong EncodedStep,
    uint SaliencePpm,
    uint ConfidencePpm,
    uint DecayStatePpm)
{
    public const string PartitionId = "resident.memory";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("memory_kind", MemoryKind.Value),
        ("subject_refs", SubjectRefs),
        ("encoded_step", EncodedStep),
        ("salience_ppm", SaliencePpm),
        ("confidence_ppm", ConfidencePpm),
        ("decay_state_ppm", DecayStatePpm));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentMemoryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "memory_kind")),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "subject_refs"),
        ResidentPayloadFields.Required<ulong>(values, PartitionId, "encoded_step"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "salience_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "confidence_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "decay_state_ppm"));
}

public sealed record ResidentPsychologyPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    IReadOnlyList<KeyValuePair<string, uint>> EmotionVector,
    uint StressPpm,
    IReadOnlyList<KeyValuePair<string, uint>> Traits,
    IReadOnlyList<KeyValuePair<string, int>> Preferences,
    IReadOnlyList<KeyValuePair<string, int>> Values)
{
    public const string PartitionId = "resident.psychology";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("emotion_vector", EmotionVector),
        ("stress_ppm", StressPpm),
        ("traits", Traits),
        ("preferences", Preferences),
        ("values", Values));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentPsychologyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<IReadOnlyList<KeyValuePair<string, uint>>>(values, PartitionId, "emotion_vector"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "stress_ppm"),
        ResidentPayloadFields.Required<IReadOnlyList<KeyValuePair<string, uint>>>(values, PartitionId, "traits"),
        ResidentPayloadFields.Required<IReadOnlyList<KeyValuePair<string, int>>>(values, PartitionId, "preferences"),
        ResidentPayloadFields.Required<IReadOnlyList<KeyValuePair<string, int>>>(values, PartitionId, "values"))
    {
        ResidentRef = ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref")
    };
}

public sealed record ResidentGoalPlanPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    StableToken GoalToken,
    long Utility,
    StableToken Status,
    IReadOnlyList<StableToken> PlanActions,
    ushort CurrentActionIndex,
    uint PlanningGeneration,
    IReadOnlyList<PartitionRecordRefV1> TargetRefs)
{
    public const string PartitionId = "resident.goal_plan";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("goal_token", GoalToken.Value),
        ("utility", Utility),
        ("status", Status.Value),
        ("plan_actions", ResidentPayloadFields.TokenValues(PlanActions)),
        ("current_action_index", CurrentActionIndex),
        ("planning_generation", PlanningGeneration),
        ("target_refs", TargetRefs));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentGoalPlanPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "goal_token")),
        ResidentPayloadFields.Required<long>(values, PartitionId, "utility"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "status")),
        ResidentPayloadFields.Tokens(values, PartitionId, "plan_actions"),
        ResidentPayloadFields.Required<ushort>(values, PartitionId, "current_action_index"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "planning_generation"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "target_refs"));
}

public sealed record ResidentSkillAptitudePayloadV1(
    PartitionRecordRefV1 ResidentRef,
    StableToken SkillToken,
    uint SkillPpm,
    uint AptitudePpm,
    ulong PracticeAccumulator,
    ulong? LastPracticeStep)
{
    public const string PartitionId = "resident.skill_aptitude";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = ResidentPayloadFields.Map(
            ("resident_ref", ResidentRef),
            ("skill_token", SkillToken.Value),
            ("skill_ppm", SkillPpm),
            ("aptitude_ppm", AptitudePpm),
            ("practice_accumulator", PracticeAccumulator));
        ResidentPayloadFields.AddOptional(values, "last_practice_step", LastPracticeStep);
        return values;
    }
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentSkillAptitudePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "skill_token")),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "skill_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "aptitude_ppm"),
        ResidentPayloadFields.Required<ulong>(values, PartitionId, "practice_accumulator"),
        ResidentPayloadFields.Optional<ulong>(values, "last_practice_step"));
}

public sealed record ResidentRelationshipPayloadV1(
    PartitionRecordRefV1 SubjectResident,
    PartitionRecordRefV1 ObjectResident,
    StableToken RelationshipKind,
    int Affinity,
    uint TrustPpm,
    uint FamiliarityPpm,
    StableToken Status)
{
    public const string PartitionId = "resident.relationship";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("subject_resident", SubjectResident),
        ("object_resident", ObjectResident),
        ("relationship_kind", RelationshipKind.Value),
        ("affinity", Affinity),
        ("trust_ppm", TrustPpm),
        ("familiarity_ppm", FamiliarityPpm),
        ("status", Status.Value));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentRelationshipPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_resident"),
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "object_resident"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "relationship_kind")),
        ResidentPayloadFields.Required<int>(values, PartitionId, "affinity"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "trust_ppm"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "familiarity_ppm"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "status")));
}

public sealed record ResidentFamilyLineagePayloadV1(
    PartitionRecordRefV1 ResidentRef,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    IReadOnlyList<PartitionRecordRefV1> ChildRefs,
    IReadOnlyList<PartitionRecordRefV1> FamilyRelationRefs,
    int GenerationIndex)
{
    public const string PartitionId = "resident.family_lineage";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => ResidentPayloadFields.Map(
        ("resident_ref", ResidentRef),
        ("parent_refs", ParentRefs),
        ("child_refs", ChildRefs),
        ("family_relation_refs", FamilyRelationRefs),
        ("generation_index", GenerationIndex));
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentFamilyLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "child_refs"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "family_relation_refs"),
        ResidentPayloadFields.Required<int>(values, PartitionId, "generation_index"));
}

public sealed record ResidentBehaviorStatePayloadV1(
    PartitionRecordRefV1 ResidentRef,
    StableToken Mode,
    PartitionRecordRefV1? ActiveGoalRef,
    StableToken? ActiveActionToken,
    IReadOnlyList<PartitionRecordRefV1> ActionTargetRefs,
    ulong? ActionStartedStep,
    StableToken ControlSource)
{
    public const string PartitionId = "resident.behavior_state";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = ResidentPayloadFields.Map(
            ("resident_ref", ResidentRef),
            ("mode", Mode.Value),
            ("action_target_refs", ActionTargetRefs),
            ("control_source", ControlSource.Value));
        ResidentPayloadFields.AddOptional(values, "active_goal_ref", ActiveGoalRef);
        ResidentPayloadFields.AddOptionalToken(values, "active_action_token", ActiveActionToken);
        ResidentPayloadFields.AddOptional(values, "action_started_step", ActionStartedStep);
        return values;
    }
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentBehaviorStatePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "mode")),
        ResidentPayloadFields.Optional<PartitionRecordRefV1>(values, "active_goal_ref"),
        ResidentPayloadFields.OptionalToken(values, "active_action_token"),
        ResidentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "action_target_refs"),
        ResidentPayloadFields.Optional<ulong>(values, "action_started_step"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "control_source")));
}

public sealed record ResidentLineagePayloadV1(
    PartitionRecordRefV1 ResidentRef,
    PartitionRecordRefV1? SourceAggregateRef,
    uint Generation,
    StableToken MaterializationRole,
    PartitionRecordRefV1 CreationRef,
    byte[] SourceDigest)
{
    public const string PartitionId = "resident.lineage";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = ResidentPayloadFields.Map(
            ("resident_ref", ResidentRef),
            ("generation", Generation),
            ("materialization_role", MaterializationRole.Value),
            ("creation_ref", CreationRef),
            ("source_digest", SourceDigest));
        ResidentPayloadFields.AddOptional(values, "source_aggregate_ref", SourceAggregateRef);
        return values;
    }
    public byte[] CanonicalDigest() => ResidentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static ResidentLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "resident_ref"),
        ResidentPayloadFields.Optional<PartitionRecordRefV1>(values, "source_aggregate_ref"),
        ResidentPayloadFields.Required<uint>(values, PartitionId, "generation"),
        new StableToken(ResidentPayloadFields.Required<string>(values, PartitionId, "materialization_role")),
        ResidentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "creation_ref"),
        ResidentPayloadFields.Required<byte[]>(values, PartitionId, "source_digest").ToArray());
}

public static class ResidentDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            Provider<ResidentIdentityLifecyclePayloadV1>(ResidentIdentityLifecyclePayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentIdentityLifecyclePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentBodyHealthPayloadV1>(ResidentBodyHealthPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentBodyHealthPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentPhysiologyPayloadV1>(ResidentPhysiologyPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentPhysiologyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentPerceptionPayloadV1>(ResidentPerceptionPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentPerceptionPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentKnowledgeBeliefPayloadV1>(ResidentKnowledgeBeliefPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentKnowledgeBeliefPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentMemoryPayloadV1>(ResidentMemoryPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentMemoryPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentPsychologyPayloadV1>(ResidentPsychologyPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentPsychologyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentGoalPlanPayloadV1>(ResidentGoalPlanPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentGoalPlanPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentSkillAptitudePayloadV1>(ResidentSkillAptitudePayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentSkillAptitudePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentRelationshipPayloadV1>(ResidentRelationshipPayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentRelationshipPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentFamilyLineagePayloadV1>(ResidentFamilyLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentFamilyLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentBehaviorStatePayloadV1>(ResidentBehaviorStatePayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentBehaviorStatePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<ResidentLineagePayloadV1>(ResidentLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), ResidentLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(static provider => provider.SectionId, StringComparer.Ordinal).ToArray());
    }

    private static IDomainPartitionSnapshotSectionProviderV1 Provider<TPayload>(
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest)
        => new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId,
            toStandard,
            fromStandard,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
}

internal static class ResidentPayloadFields
{
    public static Dictionary<string, object?> Map(params (string Name, object? Value)[] values)
        => values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);
    public static void AddOptional<T>(Dictionary<string, object?> values, string field, T? value) where T : struct
    {
        if (value is { } present) values[field] = present;
    }
    public static void AddOptionalToken(Dictionary<string, object?> values, string field, StableToken? value)
    {
        if (value is { } present) values[field] = present.Value;
    }
    public static T Required<T>(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
        => values.TryGetValue(field, out var value) && value is T typed
            ? typed
            : throw new InvalidDataException($"resident.snapshot-payload.required:{partitionId}:{field}");
    public static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
    public static StableToken? OptionalToken(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is string token ? new StableToken(token) : null;
    public static IReadOnlyList<string> TokenValues(IReadOnlyList<StableToken> tokens)
        => Array.AsReadOnly(tokens.Select(static token => token.Value).ToArray());
    public static IReadOnlyList<StableToken> Tokens(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
        => Array.AsReadOnly(Required<IReadOnlyList<string>>(values, partitionId, field).Select(static token => new StableToken(token)).ToArray());
    public static byte[] Digest(string partitionId, IReadOnlyDictionary<string, object?> payload)
        => StandardDomainPayloadCanonicalDigestV1.Compute(partitionId, payload);
}
