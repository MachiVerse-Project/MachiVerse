using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

public enum ParticipationBindingStatusV1 : byte
{
    Active = 1,
    ResidentDeceased = 2,
    Released = 3,
    Superseded = 4,
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

public sealed record ParticipationBindingV1(
    OpaqueId128 BindingId,
    OpaqueId128 DiverRef,
    OpaqueId128 ResidentId,
    ParticipationBindingStatusV1 Status,
    uint BindingGeneration,
    ulong EffectiveFromStep,
    ulong? EndedStep)
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
        if (Status == ParticipationBindingStatusV1.Active && EndedStep is not null)
            throw new InvalidDataException("participation.active-binding-ended");
        if (Status != ParticipationBindingStatusV1.Active && EndedStep is null)
            throw new InvalidDataException("participation.terminal-binding-end-missing");
        if (EndedStep is { } ended && ended < EffectiveFromStep)
            throw new InvalidDataException("participation.binding-ended-before-effective");
    }

    public ParticipationBindingV1 MarkResidentDeceased(ulong effectiveStep)
    {
        Validate();
        if (!IsActive || effectiveStep < EffectiveFromStep)
            throw new InvalidDataException("participation.binding-death-transition-invalid");
        var next = this with
        {
            Status = ParticipationBindingStatusV1.ResidentDeceased,
            EndedStep = effectiveStep,
        };
        next.Validate();
        return next;
    }

    public ParticipationBindingV1 Release(ulong effectiveStep)
    {
        Validate();
        if (!IsActive || effectiveStep < EffectiveFromStep)
            throw new InvalidDataException("participation.binding-release-transition-invalid");
        var next = this with
        {
            Status = ParticipationBindingStatusV1.Released,
            EndedStep = effectiveStep,
        };
        next.Validate();
        return next;
    }
}

public sealed class ParticipationBindingAuthorityV1
{
    private readonly ParticipationBindingV1[] _bindings;

    public ParticipationBindingAuthorityV1(IEnumerable<ParticipationBindingV1> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings.OrderBy(static binding => binding.BindingId).ToArray();
        foreach (var binding in _bindings) binding.Validate();
        if (_bindings.Select(static binding => binding.BindingId).Distinct().Count() != _bindings.Length)
            throw new InvalidDataException("participation.binding-id-duplicate");
        if (_bindings.Where(static binding => binding.IsActive)
            .GroupBy(static binding => binding.DiverRef)
            .Any(static group => group.Count() > 1))
            throw new InvalidDataException("participation.one-resident-per-diver");
        if (_bindings.Where(static binding => binding.IsActive)
            .GroupBy(static binding => binding.ResidentId)
            .Any(static group => group.Count() > 1))
            throw new InvalidDataException("participation.one-diver-per-resident");
    }

    public IReadOnlyList<ParticipationBindingV1> BindingsCanonical => Array.AsReadOnly(_bindings);
}

public sealed record ResidentControlContextV1(
    OpaqueId128 ResidentId,
    OpaqueId128? BindingId,
    uint? BindingGeneration,
    ResidentControlModeV1 ControlMode,
    uint? EffectiveAbsencePolicyGeneration,
    ulong BasisStep);

public static class ParticipationControlContextFactoryV1
{
    public static ResidentControlContextV1 ForBinding(
        ParticipationBindingV1 binding,
        ParticipationControlAvailabilityV1 availability,
        uint? effectiveAbsencePolicyGeneration,
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

        return new ResidentControlContextV1(
            binding.ResidentId,
            mode == ResidentControlModeV1.Autonomous ? null : binding.BindingId,
            mode == ResidentControlModeV1.Autonomous ? null : binding.BindingGeneration,
            mode,
            mode == ResidentControlModeV1.DiverAbsentPolicy ? effectiveAbsencePolicyGeneration : null,
            basisStep);
    }
}
