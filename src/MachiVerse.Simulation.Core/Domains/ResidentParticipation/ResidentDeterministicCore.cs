using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

public readonly record struct ResidentPpmV1
{
    public const uint Scale = 1_000_000;

    public ResidentPpmV1(uint value)
    {
        if (value > Scale) throw new InvalidDataException("resident.ppm-out-of-range");
        Value = value;
    }

    public uint Value { get; }

    public ResidentPpmV1 AddChecked(long delta)
    {
        var next = checked((long)Value + delta);
        if (next is < 0 or > Scale) throw new InvalidDataException("resident.ppm-out-of-range");
        return new ResidentPpmV1((uint)next);
    }
}

public sealed record ResidentHealthStateV1(
    ResidentPpmV1 HealthCapacity,
    ResidentPpmV1 Pain,
    ResidentPpmV1 Stress,
    ResidentPpmV1 Fatigue)
{
    public ResidentHealthStateV1 ApplyDelta(
        long healthCapacityDelta,
        long painDelta,
        long stressDelta,
        long fatigueDelta)
        => new(
            HealthCapacity.AddChecked(healthCapacityDelta),
            Pain.AddChecked(painDelta),
            Stress.AddChecked(stressDelta),
            Fatigue.AddChecked(fatigueDelta));
}

public sealed record ResidentLifecycleStateV1(
    OpaqueId128 ResidentId,
    StableToken Lifecycle,
    ulong? BirthStep,
    ulong? DeathStep)
{
    private static readonly StableToken Developing = new("developing");
    private static readonly StableToken Alive = new("alive");
    private static readonly StableToken Deceased = new("deceased");

    public ResidentLifecycleStateV1 Validate()
    {
        if (ResidentId.IsZero) throw new InvalidDataException("resident.lifecycle-id-zero");
        if (Lifecycle != Developing && Lifecycle != Alive && Lifecycle != Deceased)
            throw new InvalidDataException("resident.lifecycle-state-invalid");
        if (Lifecycle == Developing && (BirthStep is not null || DeathStep is not null))
            throw new InvalidDataException("resident.lifecycle-developing-step-invalid");
        if (Lifecycle == Alive && (BirthStep is null || DeathStep is not null))
            throw new InvalidDataException("resident.lifecycle-alive-step-invalid");
        if (Lifecycle == Deceased && DeathStep is null)
            throw new InvalidDataException("resident.lifecycle-death-step-missing");
        if (BirthStep is not null && DeathStep is not null && DeathStep < BirthStep)
            throw new InvalidDataException("resident.lifecycle-death-before-birth");
        return this;
    }

    public ResidentLifecycleStateV1 MarkAlive(ulong step)
    {
        Validate();
        if (Lifecycle != Developing)
            throw new InvalidDataException("resident.lifecycle-transition-invalid");
        return this with { Lifecycle = Alive, BirthStep = step };
    }

    public ResidentLifecycleStateV1 MarkDeceased(ulong step)
    {
        Validate();
        if (Lifecycle != Alive || BirthStep is null || step < BirthStep.Value)
            throw new InvalidDataException("resident.lifecycle-transition-invalid");
        return this with { Lifecycle = Deceased, DeathStep = step };
    }

    public static ResidentLifecycleStateV1 CreateDeveloping(OpaqueId128 residentId)
        => new ResidentLifecycleStateV1(residentId, Developing, null, null).Validate();
}

public static class ResidentDiseaseRandomV1
{
    private static readonly StableToken ResidentDomain = new("resident");

    public static bool Occurs(
        WorldSeed256 worldSeed,
        OpaqueId128 worldId,
        OpaqueId128 residentId,
        OpaqueId128 conditionId,
        ulong step,
        StableToken eventKind,
        uint probabilityPpm)
    {
        if (worldId.IsZero || residentId.IsZero || conditionId.IsZero)
            throw new InvalidDataException("resident.disease-random-id-zero");
        if (probabilityPpm > ResidentPpmV1.Scale)
            throw new InvalidDataException("resident.ppm-out-of-range");
        if (probabilityPpm == 0) return false;
        if (probabilityPpm == ResidentPpmV1.Scale) return true;

        var context = new RandomContextV1(
            worldId,
            step,
            ResidentDomain,
            eventKind,
            residentId,
            conditionId,
            OpaqueId128.Zero,
            0);
        return DeterministicRandom.BoundedUInt64(worldSeed, context, drawIndex: 0, ResidentPpmV1.Scale) < probabilityPpm;
    }
}

public static class ResidentSkillProgressionV1
{
    public static uint ApplyPractice(uint skillPpm, uint baseGainPpm)
    {
        if (skillPpm > ResidentPpmV1.Scale || baseGainPpm > ResidentPpmV1.Scale)
            throw new InvalidDataException("resident.ppm-out-of-range");

        var remaining = ResidentPpmV1.Scale - skillPpm;
        var increment = DivideRoundToEven(
            checked((Int128)baseGainPpm * remaining),
            ResidentPpmV1.Scale);
        var next = checked((ulong)skillPpm + increment);
        if (next > ResidentPpmV1.Scale)
            throw new InvalidDataException("resident.skill-overflow");
        return (uint)next;
    }

    private static ulong DivideRoundToEven(Int128 numerator, ulong denominator)
    {
        if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        var divisor = (Int128)denominator;
        var quotient = numerator / divisor;
        var remainder = numerator % divisor;
        var twiceRemainder = checked(remainder * 2);
        if (twiceRemainder > divisor || (twiceRemainder == divisor && (quotient & 1) != 0))
            quotient++;
        if (quotient > ulong.MaxValue) throw new OverflowException("simulation.numeric-overflow");
        return (ulong)quotient;
    }
}
