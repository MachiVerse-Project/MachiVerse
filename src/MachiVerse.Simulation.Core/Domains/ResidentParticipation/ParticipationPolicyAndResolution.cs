using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

public sealed record ParticipationBindRequestV1(
    ParticipationBindingV1 Binding,
    SameStepOrderKey OrderKey)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Binding);
        Binding.Validate();
        if (!Binding.IsActive)
            throw new InvalidDataException("participation.binding-create-not-active");
        ArgumentNullException.ThrowIfNull(OrderKey);
    }
}

public sealed record ParticipationBindResolutionV1(
    IReadOnlyList<ParticipationBindingV1> Accepted,
    IReadOnlyList<OpaqueId128> RejectedBindingIds);

public static class ParticipationBindResolverV1
{
    public static ParticipationBindResolutionV1 Resolve(
        IEnumerable<ParticipationBindingV1> existingBindings,
        IEnumerable<ParticipationBindRequestV1> requests)
    {
        var existing = existingBindings.ToArray();
        _ = new ParticipationBindingAuthorityV1(existing);
        var activeDivers = existing.Where(static binding => binding.IsActive)
            .Select(static binding => binding.DiverRef).ToHashSet();
        var activeResidents = existing.Where(static binding => binding.IsActive)
            .Select(static binding => binding.ResidentId).ToHashSet();
        var bindingIds = existing.Select(static binding => binding.BindingId).ToHashSet();

        var ordered = requests.OrderBy(static request => request.OrderKey).ToArray();
        foreach (var request in ordered) request.Validate();
        if (ordered.Select(static request => request.Binding.BindingId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("participation.bind-request-duplicate-id");

        var accepted = new List<ParticipationBindingV1>();
        var rejected = new List<OpaqueId128>();
        foreach (var request in ordered)
        {
            var binding = request.Binding;
            if (bindingIds.Contains(binding.BindingId) ||
                activeDivers.Contains(binding.DiverRef) ||
                activeResidents.Contains(binding.ResidentId))
            {
                rejected.Add(binding.BindingId);
                continue;
            }

            var priorGeneration = existing.Concat(accepted)
                .Where(item => item.DiverRef == binding.DiverRef)
                .Select(static item => item.BindingGeneration)
                .DefaultIfEmpty(0)
                .Max();
            if (binding.BindingGeneration <= priorGeneration)
            {
                rejected.Add(binding.BindingId);
                continue;
            }

            accepted.Add(binding);
            bindingIds.Add(binding.BindingId);
            activeDivers.Add(binding.DiverRef);
            activeResidents.Add(binding.ResidentId);
        }

        return new ParticipationBindResolutionV1(
            Array.AsReadOnly(accepted.OrderBy(static binding => binding.BindingId).ToArray()),
            Array.AsReadOnly(rejected.OrderBy(static id => id).ToArray()));
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
        if (BindingId is { IsZero: true }) throw new InvalidDataException("participation.policy-binding-zero");
        if (PolicyGeneration == 0) throw new InvalidDataException("participation.policy-generation-zero");
        ArgumentNullException.ThrowIfNull(PriorityRules);
        if (PriorityRules.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != PriorityRules.Count)
            throw new InvalidDataException("participation.policy-rule-duplicate");
    }

    public ParticipationAbsencePolicyV1 Revise(
        uint expectedGeneration,
        IEnumerable<StableToken> rules,
        ulong effectiveFromStep)
    {
        Validate();
        if (expectedGeneration != PolicyGeneration)
            throw new InvalidDataException("participation.absence-policy-stale-generation");
        var canonical = rules.ToArray();
        var revised = this with
        {
            PolicyGeneration = checked(PolicyGeneration + 1),
            PriorityRules = Array.AsReadOnly(canonical),
            EffectiveFromStep = effectiveFromStep,
        };
        revised.Validate();
        return revised;
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
