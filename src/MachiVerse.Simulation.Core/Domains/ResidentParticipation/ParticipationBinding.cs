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
}

public sealed record ResidentControlContextV1(
    OpaqueId128 ResidentId,
    OpaqueId128? BindingId,
    uint? BindingGeneration,
    ResidentControlModeV1 ControlMode,
    OpaqueId128? EffectiveAbsencePolicyRef,
    ulong BasisStep);

public sealed class ParticipationBindingAuthorityV1
{
    private readonly ParticipationBindingV1[] _bindings;

    public ParticipationBindingAuthorityV1(IEnumerable<ParticipationBindingV1> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings
            .OrderBy(static item => item.BindingId)
            .ToArray();
        foreach (var binding in _bindings) binding.Validate();
        if (_bindings.Select(static item => item.BindingId).Distinct().Count() != _bindings.Length)
            throw new InvalidDataException("participation.binding-id-duplicate");
        if (_bindings.Where(static item => item.IsActive)
            .GroupBy(static item => item.DiverRef)
            .Any(static group => group.Count() > 1))
            throw new InvalidDataException("participation.diver-active-binding-duplicate");
        if (_bindings.Where(static item => item.IsActive)
            .GroupBy(static item => item.ResidentId)
            .Any(static group => group.Count() > 1))
            throw new InvalidDataException("participation.resident-active-binding-duplicate");
    }

    public IReadOnlyList<ParticipationBindingV1> BindingsCanonical => Array.AsReadOnly(_bindings);

    public void ValidateCreate(ParticipationBindingV1 candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.Validate();
        if (!candidate.IsActive)
            throw new InvalidDataException("participation.binding-create-not-active");
        if (_bindings.Any(binding => binding.BindingId == candidate.BindingId))
            throw new InvalidDataException("participation.binding-id-duplicate");
        if (_bindings.Any(binding => binding.IsActive && binding.DiverRef == candidate.DiverRef))
            throw new InvalidDataException("participation.diver-active-binding-duplicate");
        if (_bindings.Any(binding => binding.IsActive && binding.ResidentId == candidate.ResidentId))
            throw new InvalidDataException("participation.resident-active-binding-duplicate");

        var priorGeneration = _bindings
            .Where(binding => binding.DiverRef == candidate.DiverRef)
            .Select(static binding => binding.BindingGeneration)
            .DefaultIfEmpty(0)
            .Max();
        if (candidate.BindingGeneration <= priorGeneration)
            throw new InvalidDataException("participation.binding-generation-stale");
    }

    public ParticipationBindingV1 Release(
        OpaqueId128 bindingId,
        uint expectedGeneration,
        ulong effectiveStep)
    {
        var binding = RequireActive(bindingId, expectedGeneration);
        if (effectiveStep < binding.EffectiveFromStep)
            throw new InvalidDataException("participation.binding-ended-before-effective");
        return binding with
        {
            Status = ParticipationBindingStatusV1.Released,
            EndedStep = effectiveStep,
        };
    }

    public ParticipationBindingV1 MarkResidentDeceased(
        OpaqueId128 bindingId,
        uint expectedGeneration,
        ulong effectiveStep)
    {
        var binding = RequireActive(bindingId, expectedGeneration);
        if (effectiveStep < binding.EffectiveFromStep)
            throw new InvalidDataException("participation.binding-ended-before-effective");
        return binding with
        {
            Status = ParticipationBindingStatusV1.ResidentDeceased,
            EndedStep = effectiveStep,
        };
    }

    public ResidentControlContextV1 ProjectControlContext(
        OpaqueId128 residentId,
        ParticipationControlAvailabilityV1 availability,
        OpaqueId128? effectiveAbsencePolicyRef,
        ulong basisStep)
    {
        if (residentId.IsZero) throw new InvalidDataException("participation.resident-id-zero");
        if (!Enum.IsDefined(availability))
            throw new InvalidDataException("participation.control-availability-invalid");
        if (effectiveAbsencePolicyRef is { IsZero: true })
            throw new InvalidDataException("participation.absence-policy-ref-zero");

        var binding = _bindings.SingleOrDefault(item => item.ResidentId == residentId && item.IsActive);
        if (binding is null)
            return new ResidentControlContextV1(
                residentId, null, null, ResidentControlModeV1.Autonomous, null, basisStep);

        var mode = availability switch
        {
            ParticipationControlAvailabilityV1.Available => ResidentControlModeV1.DiverControlAvailable,
            ParticipationControlAvailabilityV1.Unavailable => ResidentControlModeV1.DiverAbsentPolicy,
            _ => throw new InvalidDataException("participation.control-availability-invalid"),
        };
        return new ResidentControlContextV1(
            residentId,
            binding.BindingId,
            binding.BindingGeneration,
            mode,
            mode == ResidentControlModeV1.DiverAbsentPolicy ? effectiveAbsencePolicyRef : null,
            basisStep);
    }

    public ResidentControlContextV1 ProjectTerminalControlContext(
        ParticipationBindingV1 binding,
        ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        if (binding.Status != ParticipationBindingStatusV1.ResidentDeceased)
            throw new InvalidDataException("participation.binding-not-resident-deceased");
        return new ResidentControlContextV1(
            binding.ResidentId,
            binding.BindingId,
            binding.BindingGeneration,
            ResidentControlModeV1.BoundResidentDeceased,
            null,
            basisStep);
    }

    private ParticipationBindingV1 RequireActive(OpaqueId128 bindingId, uint expectedGeneration)
    {
        if (bindingId.IsZero) throw new InvalidDataException("participation.binding-id-zero");
        var binding = _bindings.SingleOrDefault(item => item.BindingId == bindingId)
            ?? throw new KeyNotFoundException("participation.binding-not-found");
        if (!binding.IsActive) throw new InvalidDataException("participation.binding-not-active");
        if (binding.BindingGeneration != expectedGeneration)
            throw new InvalidDataException("participation.binding-generation-stale");
        return binding;
    }
}
