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
        if (!Enum.IsDefined(Status))
            throw new InvalidDataException("participation.binding-status-invalid");
        if (BindingGeneration == 0)
            throw new InvalidDataException("participation.binding-generation-zero");
        if (Status is ParticipationBindingStatusV1.Active or ParticipationBindingStatusV1.ResidentDeceased)
        {
            if (EndedStep is not null)
                throw new InvalidDataException("participation.binding-active-has-ended-step");
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
        if (BindingGeneration == 0)
            throw new InvalidDataException("participation.bind-generation-zero");
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

public sealed record ParticipationAbsencePolicyV1(
    OpaqueId128 DiverRef,
    OpaqueId128? BindingId,
    uint PolicyGeneration,
    IReadOnlyList<StableToken> PriorityRules,
    ulong EffectiveFromStep)
{
    public void Validate()
    {
        if (DiverRef.IsZero) throw new InvalidDataException("participation.policy-diver-zero");
        if (BindingId is { } bindingId && bindingId.IsZero)
            throw new InvalidDataException("participation.policy-binding-zero");
        if (PolicyGeneration == 0) throw new InvalidDataException("participation.policy-generation-zero");
        ArgumentNullException.ThrowIfNull(PriorityRules);
        if (PriorityRules.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != PriorityRules.Count)
            throw new InvalidDataException("participation.policy-rule-duplicate");
    }

    public ParticipationAbsencePolicyV1 Revise(
        uint expectedGeneration,
        IEnumerable<StableToken> priorityRules,
        ulong effectiveFromStep)
    {
        Validate();
        if (expectedGeneration != PolicyGeneration)
            throw new InvalidDataException("participation.absence-policy-stale-generation");
        ArgumentNullException.ThrowIfNull(priorityRules);
        var rules = priorityRules.ToArray();
        var next = this with
        {
            PolicyGeneration = checked(PolicyGeneration + 1),
            PriorityRules = Array.AsReadOnly(rules),
            EffectiveFromStep = effectiveFromStep,
        };
        next.Validate();
        return next;
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
        var floor = (byte)0;
        foreach (var requirement in requirements)
        {
            requirement.Validate();
            if (requirement.ResidentId == residentId && requirement.AppliesAt(step))
                floor = Math.Max(floor, requirement.MinimumDetail);
        }
        return floor;
    }
}
