using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Participation;

public sealed record ParticipationBindingPayloadV1(
    OpaqueId128 BindingId,
    OpaqueId128 DiverRef,
    PartitionRecordRefV1 ResidentRef,
    StableToken Status,
    ulong EffectiveFrom,
    ulong? EndedStep,
    uint BindingGeneration,
    PartitionRecordRefV1? AbsencePolicyRef,
    IReadOnlyList<PartitionRecordRefV1> CausalityRefs)
{
    public const string PartitionId = "participation.binding";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["binding_id"] = BindingId,
            ["diver_ref"] = DiverRef,
            ["resident_ref"] = ResidentRef,
            ["status"] = Status.Value,
            ["effective_from"] = EffectiveFrom,
            ["binding_generation"] = BindingGeneration,
            ["causality_refs"] = CausalityRefs,
        };
        if (EndedStep is { } ended) values["ended_step"] = ended;
        if (AbsencePolicyRef is { } policy) values["absence_policy_ref"] = policy;
        return values;
    }

    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(PartitionId, ToStandardPayload());

    public static ParticipationBindingPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            (OpaqueId128)Require(values, "binding_id"),
            (OpaqueId128)Require(values, "diver_ref"),
            (PartitionRecordRefV1)Require(values, "resident_ref"),
            new StableToken((string)Require(values, "status")),
            (ulong)Require(values, "effective_from"),
            Optional<ulong>(values, "ended_step"),
            (uint)Require(values, "binding_generation"),
            Optional<PartitionRecordRefV1>(values, "absence_policy_ref"),
            (IReadOnlyList<PartitionRecordRefV1>)Require(values, "causality_refs"));

    private static object Require(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? value
            : throw new InvalidDataException($"participation.snapshot-payload.required:{PartitionId}:{field}");

    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
}

public sealed record ParticipationAbsencePolicyPayloadV1(
    OpaqueId128 DiverRef,
    uint PolicyGeneration,
    IReadOnlyList<ParticipationPolicyRuleV1> PriorityRules,
    ulong EffectiveFrom,
    ulong? EffectiveUntil)
{
    public const string PartitionId = "participation.absence_policy";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["diver_ref"] = DiverRef,
            ["policy_generation"] = PolicyGeneration,
            ["priority_rules"] = PriorityRules,
            ["effective_from"] = EffectiveFrom,
        };
        if (EffectiveUntil is { } until) values["effective_until"] = until;
        return values;
    }

    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(
            PartitionId,
            ToStandardPayload(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

    public static ParticipationAbsencePolicyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
    {
        var nested = (IReadOnlyList<ICanonicalDomainNestedValueV1>)Require(values, "priority_rules");
        var rules = nested.Select(static value => value as ParticipationPolicyRuleV1
            ?? throw new InvalidDataException("participation.snapshot-payload.priority-rule-type")).ToArray();
        return new ParticipationAbsencePolicyPayloadV1(
            (OpaqueId128)Require(values, "diver_ref"),
            (uint)Require(values, "policy_generation"),
            Array.AsReadOnly(rules),
            (ulong)Require(values, "effective_from"),
            Optional<ulong>(values, "effective_until"));
    }

    private static object Require(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? value
            : throw new InvalidDataException($"participation.snapshot-payload.required:{PartitionId}:{field}");

    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
}

public sealed record ParticipationControlModePayloadV1(
    PartitionRecordRefV1 ResidentRef,
    PartitionRecordRefV1? BindingRef,
    StableToken Mode,
    ulong EffectiveFrom,
    uint InputAuthorityGeneration)
{
    public const string PartitionId = "participation.control_mode";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resident_ref"] = ResidentRef,
            ["mode"] = Mode.Value,
            ["effective_from"] = EffectiveFrom,
            ["input_authority_generation"] = InputAuthorityGeneration,
        };
        if (BindingRef is { } binding) values["binding_ref"] = binding;
        return values;
    }

    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(PartitionId, ToStandardPayload());

    public static ParticipationControlModePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            (PartitionRecordRefV1)Require(values, "resident_ref"),
            Optional<PartitionRecordRefV1>(values, "binding_ref"),
            new StableToken((string)Require(values, "mode")),
            (ulong)Require(values, "effective_from"),
            (uint)Require(values, "input_authority_generation"));

    private static object Require(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? value
            : throw new InvalidDataException($"participation.snapshot-payload.required:{PartitionId}:{field}");

    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
}

public sealed record ParticipationHistoryPayloadV1(
    PartitionRecordRefV1 BindingRef,
    StableToken HistoryKind,
    ulong BasisStep,
    PartitionRecordRefV1? PreviousHistoryRef,
    byte[] CausalityDigest)
{
    public const string PartitionId = "participation.history";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["binding_ref"] = BindingRef,
            ["history_kind"] = HistoryKind.Value,
            ["basis_step"] = BasisStep,
            ["causality_digest"] = CausalityDigest,
        };
        if (PreviousHistoryRef is { } previous) values["previous_history_ref"] = previous;
        return values;
    }

    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(PartitionId, ToStandardPayload());

    public static ParticipationHistoryPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            (PartitionRecordRefV1)Require(values, "binding_ref"),
            new StableToken((string)Require(values, "history_kind")),
            (ulong)Require(values, "basis_step"),
            Optional<PartitionRecordRefV1>(values, "previous_history_ref"),
            ((byte[])Require(values, "causality_digest")).ToArray());

    private static object Require(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? value
            : throw new InvalidDataException($"participation.snapshot-payload.required:{PartitionId}:{field}");

    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
}

public sealed record ParticipationDetailRequirementPayloadV1(
    PartitionRecordRefV1 ResidentRef,
    byte MinimumDetail,
    PartitionRecordRefV1 ScopeRef,
    StableToken Reason,
    ulong EffectiveFrom,
    ulong? EffectiveUntil)
{
    public const string PartitionId = "participation.detail_requirement";

    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resident_ref"] = ResidentRef,
            ["minimum_detail"] = MinimumDetail,
            ["scope_ref"] = ScopeRef,
            ["reason"] = Reason.Value,
            ["effective_from"] = EffectiveFrom,
        };
        if (EffectiveUntil is { } until) values["effective_until"] = until;
        return values;
    }

    public byte[] CanonicalDigest()
        => StandardDomainPayloadCanonicalDigestV1.Compute(PartitionId, ToStandardPayload());

    public static ParticipationDetailRequirementPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values)
        => new(
            (PartitionRecordRefV1)Require(values, "resident_ref"),
            (byte)Require(values, "minimum_detail"),
            (PartitionRecordRefV1)Require(values, "scope_ref"),
            new StableToken((string)Require(values, "reason")),
            (ulong)Require(values, "effective_from"),
            Optional<ulong>(values, "effective_until"));

    private static object Require(IReadOnlyDictionary<string, object?> values, string field)
        => values.TryGetValue(field, out var value) && value is not null
            ? value
            : throw new InvalidDataException($"participation.snapshot-payload.required:{PartitionId}:{field}");

    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field) where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;
}

public static class ParticipationDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
        => Array.AsReadOnly<IDomainPartitionSnapshotSectionProviderV1>(
        [
            new DomainPartitionSnapshotSectionProviderV1<ParticipationBindingPayloadV1>(
                ParticipationBindingPayloadV1.PartitionId,
                static payload => payload.ToStandardPayload(),
                static values => ParticipationBindingPayloadV1.FromStandardPayload(values),
                static payload => payload.CanonicalDigest(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default),
            new DomainPartitionSnapshotSectionProviderV1<ParticipationAbsencePolicyPayloadV1>(
                ParticipationAbsencePolicyPayloadV1.PartitionId,
                static payload => payload.ToStandardPayload(),
                static values => ParticipationAbsencePolicyPayloadV1.FromStandardPayload(values),
                static payload => payload.CanonicalDigest(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default),
            new DomainPartitionSnapshotSectionProviderV1<ParticipationControlModePayloadV1>(
                ParticipationControlModePayloadV1.PartitionId,
                static payload => payload.ToStandardPayload(),
                static values => ParticipationControlModePayloadV1.FromStandardPayload(values),
                static payload => payload.CanonicalDigest(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default),
            new DomainPartitionSnapshotSectionProviderV1<ParticipationHistoryPayloadV1>(
                ParticipationHistoryPayloadV1.PartitionId,
                static payload => payload.ToStandardPayload(),
                static values => ParticipationHistoryPayloadV1.FromStandardPayload(values),
                static payload => payload.CanonicalDigest(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default),
            new DomainPartitionSnapshotSectionProviderV1<ParticipationDetailRequirementPayloadV1>(
                ParticipationDetailRequirementPayloadV1.PartitionId,
                static payload => payload.ToStandardPayload(),
                static values => ParticipationDetailRequirementPayloadV1.FromStandardPayload(values),
                static payload => payload.CanonicalDigest(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default),
        ]);
}
