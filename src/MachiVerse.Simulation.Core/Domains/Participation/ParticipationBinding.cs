using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.Participation;

public enum ParticipationBindingStatusV1 : byte
{
    Active = 1,
    ResidentDeceased = 2,
    Released = 3,
    Superseded = 4,
}

public sealed record ParticipationBindingStateV1(
    OpaqueId128 BindingId,
    OpaqueId128 DiverRef,
    OpaqueId128 ResidentId,
    ParticipationBindingStatusV1 Status,
    ulong EffectiveFromStep,
    ulong? EndedStep,
    uint BindingGeneration)
{
    public bool IsActive => Status == ParticipationBindingStatusV1.Active;

    public void Validate()
    {
        if (BindingId.IsZero || DiverRef.IsZero || ResidentId.IsZero)
            throw new InvalidDataException("participation.binding-id-zero");
        if (!Enum.IsDefined(Status)) throw new InvalidDataException("participation.binding-status-invalid");
        if (BindingGeneration == 0) throw new InvalidDataException("participation.binding-generation-zero");
        if (Status is ParticipationBindingStatusV1.Active or ParticipationBindingStatusV1.ResidentDeceased)
        {
            if (EndedStep is not null) throw new InvalidDataException("participation.binding-active-has-ended-step");
        }
        else if (EndedStep is null)
        {
            throw new InvalidDataException("participation.binding-terminal-ended-step-required");
        }
        if (EndedStep is not null && EndedStep.Value < EffectiveFromStep)
            throw new InvalidDataException("participation.binding-ended-before-effective");
    }

    public ParticipationBindingStateV1 MarkResidentDeceased()
    {
        Validate();
        if (Status == ParticipationBindingStatusV1.ResidentDeceased) return this;
        if (Status != ParticipationBindingStatusV1.Active)
            throw new InvalidDataException("participation.binding-death-transition-invalid");
        return this with { Status = ParticipationBindingStatusV1.ResidentDeceased };
    }

    public ParticipationBindingStateV1 Release(ulong endedStep)
    {
        Validate();
        if (Status is ParticipationBindingStatusV1.Released or ParticipationBindingStatusV1.Superseded)
            throw new InvalidDataException("participation.binding-already-terminal");
        if (endedStep < EffectiveFromStep)
            throw new InvalidDataException("participation.binding-ended-before-effective");
        return this with { Status = ParticipationBindingStatusV1.Released, EndedStep = endedStep };
    }

    public ParticipationBindingStateV1 Supersede(ulong endedStep)
    {
        Validate();
        if (Status is ParticipationBindingStatusV1.Released or ParticipationBindingStatusV1.Superseded)
            throw new InvalidDataException("participation.binding-already-terminal");
        if (endedStep < EffectiveFromStep)
            throw new InvalidDataException("participation.binding-ended-before-effective");
        return this with { Status = ParticipationBindingStatusV1.Superseded, EndedStep = endedStep };
    }
}

public sealed record ParticipationBindRequestV1(
    OpaqueId128 BindingId,
    OpaqueId128 DiverRef,
    OpaqueId128 ResidentId,
    uint BindingGeneration,
    ulong EffectiveStep,
    SameStepOrderKey OrderKey)
{
    public void Validate()
    {
        if (BindingId.IsZero || DiverRef.IsZero || ResidentId.IsZero)
            throw new InvalidDataException("participation.bind-request-id-zero");
        if (BindingGeneration == 0) throw new InvalidDataException("participation.bind-generation-zero");
        ArgumentNullException.ThrowIfNull(OrderKey);
    }
}

public sealed record ParticipationBindResolutionV1(
    IReadOnlyList<ParticipationBindingStateV1> Accepted,
    IReadOnlyList<OpaqueId128> RejectedBindingIds);

public static class ParticipationBindResolverV1
{
    public static ParticipationBindResolutionV1 Resolve(
        IEnumerable<ParticipationBindingStateV1> existingBindings,
        IEnumerable<ParticipationBindRequestV1> requests)
    {
        ArgumentNullException.ThrowIfNull(existingBindings);
        ArgumentNullException.ThrowIfNull(requests);
        var existing = existingBindings.ToArray();
        foreach (var binding in existing) binding.Validate();
        EnsureOneToOne(existing.Where(static binding => binding.IsActive));

        var activeDivers = existing.Where(static binding => binding.IsActive)
            .Select(static binding => binding.DiverRef).ToHashSet();
        var activeResidents = existing.Where(static binding => binding.IsActive)
            .Select(static binding => binding.ResidentId).ToHashSet();
        var seenBindingIds = existing.Select(static binding => binding.BindingId).ToHashSet();
        var ordered = requests.OrderBy(static request => request.OrderKey).ToArray();
        foreach (var request in ordered) request.Validate();
        if (ordered.Select(static request => request.BindingId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("participation.bind-request-duplicate-id");

        var accepted = new List<ParticipationBindingStateV1>();
        var rejected = new List<OpaqueId128>();
        foreach (var request in ordered)
        {
            if (seenBindingIds.Contains(request.BindingId) ||
                activeDivers.Contains(request.DiverRef) ||
                activeResidents.Contains(request.ResidentId))
            {
                rejected.Add(request.BindingId);
                continue;
            }

            var priorGeneration = existing.Concat(accepted)
                .Where(item => item.DiverRef == request.DiverRef)
                .Select(static item => item.BindingGeneration)
                .DefaultIfEmpty(0u)
                .Max();
            if (request.BindingGeneration <= priorGeneration)
            {
                rejected.Add(request.BindingId);
                continue;
            }

            var binding = new ParticipationBindingStateV1(
                request.BindingId,
                request.DiverRef,
                request.ResidentId,
                ParticipationBindingStatusV1.Active,
                request.EffectiveStep,
                null,
                request.BindingGeneration);
            binding.Validate();
            accepted.Add(binding);
            seenBindingIds.Add(binding.BindingId);
            activeDivers.Add(binding.DiverRef);
            activeResidents.Add(binding.ResidentId);
        }

        return new ParticipationBindResolutionV1(
            Array.AsReadOnly(accepted.OrderBy(static binding => binding.BindingId).ToArray()),
            Array.AsReadOnly(rejected.OrderBy(static id => id).ToArray()));
    }

    public static void EnsureOneToOne(IEnumerable<ParticipationBindingStateV1> activeBindings)
    {
        ArgumentNullException.ThrowIfNull(activeBindings);
        var active = activeBindings.ToArray();
        if (active.GroupBy(static binding => binding.DiverRef).Any(static group => group.Count() > 1))
            throw new InvalidDataException("participation.one-resident-per-diver");
        if (active.GroupBy(static binding => binding.ResidentId).Any(static group => group.Count() > 1))
            throw new InvalidDataException("participation.one-diver-per-resident");
    }
}

public enum ParticipationControlAvailabilityV1 : byte
{
    Available = 1,
    Unavailable = 2,
}

public enum ResidentControlModeV1 : byte
{
    Autonomous = 1,
    DiverControlAvailable = 2,
    DiverAbsentPolicy = 3,
    BoundResidentDeceased = 4,
}

public sealed record ParticipationControlContextV1(
    OpaqueId128 ResidentId,
    OpaqueId128? BindingId,
    ResidentControlModeV1 Mode,
    uint? AbsencePolicyGeneration,
    ulong BasisStep);

public static class ParticipationControlContextFactoryV1
{
    public static ParticipationControlContextV1 ForBinding(
        ParticipationBindingStateV1 binding,
        ParticipationControlAvailabilityV1 availability,
        uint? absencePolicyGeneration,
        ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        if (!Enum.IsDefined(availability))
            throw new InvalidDataException("participation.control-availability-invalid");

        var mode = binding.Status switch
        {
            ParticipationBindingStatusV1.ResidentDeceased => ResidentControlModeV1.BoundResidentDeceased,
            ParticipationBindingStatusV1.Active when availability == ParticipationControlAvailabilityV1.Available
                => ResidentControlModeV1.DiverControlAvailable,
            ParticipationBindingStatusV1.Active => ResidentControlModeV1.DiverAbsentPolicy,
            _ => ResidentControlModeV1.Autonomous,
        };

        return new ParticipationControlContextV1(
            binding.ResidentId,
            mode == ResidentControlModeV1.Autonomous ? null : binding.BindingId,
            mode,
            mode == ResidentControlModeV1.DiverAbsentPolicy ? absencePolicyGeneration : null,
            basisStep);
    }
}

public sealed record ParticipationPolicyRuleV1(int Priority, StableToken RuleId)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RuleId.Value))
            throw new InvalidDataException("participation.policy-rule-id-empty");
    }
}

public sealed class ParticipationAbsencePolicyV1
{
    public ParticipationAbsencePolicyV1(
        OpaqueId128 diverRef,
        OpaqueId128? bindingId,
        uint policyGeneration,
        IEnumerable<ParticipationPolicyRuleV1> priorityRules,
        ulong effectiveFromStep,
        ulong? effectiveUntilStep = null)
    {
        if (diverRef.IsZero) throw new InvalidDataException("participation.policy-diver-zero");
        if (bindingId is { IsZero: true }) throw new InvalidDataException("participation.policy-binding-zero");
        if (policyGeneration == 0) throw new InvalidDataException("participation.policy-generation-zero");
        ArgumentNullException.ThrowIfNull(priorityRules);
        if (effectiveUntilStep is { } until && until < effectiveFromStep)
            throw new InvalidDataException("participation.policy-effective-range-invalid");

        var canonical = priorityRules
            .Select(rule =>
            {
                ArgumentNullException.ThrowIfNull(rule);
                rule.Validate();
                return rule;
            })
            .OrderBy(static rule => rule.Priority)
            .ThenBy(static rule => rule.RuleId.Value, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Select(static rule => rule.RuleId.Value).Distinct(StringComparer.Ordinal).Count() != canonical.Length)
            throw new InvalidDataException("participation.policy-rule-duplicate");

        DiverRef = diverRef;
        BindingId = bindingId;
        PolicyGeneration = policyGeneration;
        PriorityRules = Array.AsReadOnly(canonical);
        EffectiveFromStep = effectiveFromStep;
        EffectiveUntilStep = effectiveUntilStep;
    }

    public OpaqueId128 DiverRef { get; }
    public OpaqueId128? BindingId { get; }
    public uint PolicyGeneration { get; }
    public IReadOnlyList<ParticipationPolicyRuleV1> PriorityRules { get; }
    public ulong EffectiveFromStep { get; }
    public ulong? EffectiveUntilStep { get; }

    public void Validate()
    {
        _ = new ParticipationAbsencePolicyV1(
            DiverRef,
            BindingId,
            PolicyGeneration,
            PriorityRules,
            EffectiveFromStep,
            EffectiveUntilStep);
    }

    public ParticipationAbsencePolicyV1 Revise(
        uint expectedGeneration,
        IEnumerable<ParticipationPolicyRuleV1> priorityRules,
        ulong effectiveFromStep,
        ulong? effectiveUntilStep = null)
    {
        if (expectedGeneration != PolicyGeneration)
            throw new InvalidDataException("participation.absence-policy-stale-generation");
        if (PolicyGeneration == uint.MaxValue)
            throw new InvalidDataException("participation.policy-generation-overflow");

        return new ParticipationAbsencePolicyV1(
            DiverRef,
            BindingId,
            PolicyGeneration + 1,
            priorityRules,
            effectiveFromStep,
            effectiveUntilStep);
    }
}

public sealed record ParticipationDetailRequirementV1(
    OpaqueId128 ResidentId,
    byte MinimumDetail,
    ulong EffectiveFromStep,
    ulong? EffectiveUntilStep)
{
    public void Validate()
    {
        if (ResidentId.IsZero) throw new InvalidDataException("participation.detail-resident-zero");
        if (MinimumDetail > 3) throw new InvalidDataException("participation.detail-floor-range");
        if (EffectiveUntilStep is not null && EffectiveUntilStep.Value < EffectiveFromStep)
            throw new InvalidDataException("participation.detail-range-invalid");
    }

    public bool AppliesAt(ulong step)
        => step >= EffectiveFromStep && (EffectiveUntilStep is null || step <= EffectiveUntilStep.Value);
}

public static class ParticipationDetailFloorV1
{
    public static byte Resolve(
        OpaqueId128 residentId,
        ulong step,
        IEnumerable<ParticipationDetailRequirementV1> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (residentId.IsZero) throw new InvalidDataException("participation.detail-resident-zero");
        var floor = (byte)3;
        foreach (var requirement in requirements)
        {
            requirement.Validate();
            if (requirement.ResidentId == residentId && requirement.AppliesAt(step))
                floor = Math.Min(floor, requirement.MinimumDetail);
        }
        return floor;
    }
}
