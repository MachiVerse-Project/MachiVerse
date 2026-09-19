using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Resident;

/// <summary>
/// Exact Phase 4 nested persistence value for resident.body_health/body_region_states.
/// The nested state owns region-local aggregate manifestation only; persistent injury/disease
/// identities remain owned by the parent payload reference lists.
/// </summary>
public sealed record ResidentBodyRegionStateNestedValueV1(
    StableToken RegionToken,
    uint IntegrityPpm,
    uint FunctionCapacityPpm,
    uint PainPpm,
    uint InjuryLoadPpm,
    uint DiseaseLoadPpm,
    uint ImpairmentPpm,
    uint RecoveryPpm) : ICanonicalDomainNestedValueV1
{
    private static readonly string[] CanonicalRegionValues =
    [
        "body.arm.left",
        "body.arm.right",
        "body.head",
        "body.leg.left",
        "body.leg.right",
        "body.systemic",
        "body.torso",
    ];

    private static readonly IReadOnlySet<string> AllowedRegionValues =
        CanonicalRegionValues.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<StableToken> CanonicalRegions { get; } = Array.AsReadOnly(
        CanonicalRegionValues.Select(static value => new StableToken(value)).ToArray());

    public void ValidateCanonical()
    {
        if (!AllowedRegionValues.Contains(RegionToken.Value))
            throw new InvalidDataException("resident.body-region-token-invalid");

        ValidatePpm(IntegrityPpm, nameof(IntegrityPpm));
        ValidatePpm(FunctionCapacityPpm, nameof(FunctionCapacityPpm));
        ValidatePpm(PainPpm, nameof(PainPpm));
        ValidatePpm(InjuryLoadPpm, nameof(InjuryLoadPpm));
        ValidatePpm(DiseaseLoadPpm, nameof(DiseaseLoadPpm));
        ValidatePpm(ImpairmentPpm, nameof(ImpairmentPpm));
        ValidatePpm(RecoveryPpm, nameof(RecoveryPpm));
    }

    private static void ValidatePpm(uint value, string fieldName)
    {
        if (value > ResidentPpmV1.Max)
            throw new InvalidDataException($"resident.body-region-ppm-out-of-range:{fieldName}");
    }
}
