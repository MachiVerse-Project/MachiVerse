using System.Buffers.Binary;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ReferenceClassV1(
    StableToken ClassToken,
    ulong Count);

public sealed record Qa04ReferenceRecordV1(
    StableToken ClassToken,
    ulong Ordinal,
    OpaqueId128 RecordId,
    DetailLevelV1 DetailLevel,
    ushort RegionalTileIndex,
    byte? DenseRegionIndex);

public sealed record Qa04OperationFamilyV1(
    StableToken FamilyToken,
    ushort SharePermille);

public sealed record Qa04OperationDescriptorV1(
    ulong InjectionStep,
    StableToken FamilyToken,
    ulong FamilyOrdinal,
    OpaqueId128 OperationId,
    byte[] PayloadDigest);

public sealed record Qa04ActivityClassV1(
    StableToken ActivityToken,
    byte Percent);

/// <summary>
/// Canonical deterministic input generator for P4-06 / QA-04 perf.reference.v1.
///
/// This type only owns benchmark input identity/distribution. It does not claim that a generated
/// descriptor has been materialized into authoritative WorldState. The release target must consume
/// these descriptors through the real domain/runtime/persistence paths before emitting evidence.
/// </summary>
public static class Qa04ReferenceLoadV1
{
    public const string BenchmarkProfileId = "perf.reference.v1";
    public const string WorldSeedHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    public const int RegionalTileRows = 64;
    public const int RegionalTileColumns = 64;
    public const int RegionalTileCount = RegionalTileRows * RegionalTileColumns;
    public const ulong WarmupSteps = 9_000;
    public const ulong MeasurementSteps = 18_000;
    public const ulong SteadyOperationsPerStep = 5_000;
    public const ulong BurstEverySteps = 900;
    public const ulong BurstOperations = 50_000;
    public const ulong DetailTransitionEverySteps = 300;
    public const ulong CanonicalInitialRecordCount = 6_760_000;

    private static readonly StableToken PerformanceDomain = new("performance");
    private static readonly StableToken ParticipationDomain = new("participation");
    private static readonly StableToken ParticipationControlModeClass = new("participation.control_mode");
    private static readonly StableToken ParticipationControlModeCreationKind = new("perf.control-mode");
    private static readonly StableToken PositionPurpose = new("perf.reference.position.v1");
    private static readonly StableToken ActivityPurpose = new("perf.reference.activity.v1");

    public static readonly WorldSeed256 WorldSeed = new(Convert.FromHexString(WorldSeedHex));
    public static readonly OpaqueId128 WorldId = DeriveBenchmarkWorldId();

    public static readonly IReadOnlyList<Qa04ReferenceClassV1> RecordClasses = Array.AsReadOnly(new[]
    {
        new Qa04ReferenceClassV1(new StableToken("resident.persistent-identity"), 1_000_000),
        new Qa04ReferenceClassV1(ParticipationControlModeClass, 1_000_000),
        new Qa04ReferenceClassV1(new StableToken("physical.d0-presence"), 500_000),
        new Qa04ReferenceClassV1(new StableToken("environment.d0-cell-cohort"), 1_000_000),
        new Qa04ReferenceClassV1(new StableToken("environment.d1-aggregate"), 250_000),
        new Qa04ReferenceClassV1(new StableToken("society-governance.active-record"), 2_000_000),
        new Qa04ReferenceClassV1(new StableToken("infrastructure.active-record"), 500_000),
        new Qa04ReferenceClassV1(new StableToken("spatial.hot-terrain-brick"), 500_000),
        new Qa04ReferenceClassV1(new StableToken("transaction.active-cross-domain"), 10_000),
    });

    public static readonly IReadOnlyList<Qa04ActivityClassV1> ResidentActivityMix = Array.AsReadOnly(new[]
    {
        new Qa04ActivityClassV1(new StableToken("idle-routine"), 35),
        new Qa04ActivityClassV1(new StableToken("local-movement"), 25),
        new Qa04ActivityClassV1(new StableToken("social-communication"), 10),
        new Qa04ActivityClassV1(new StableToken("market-consumption"), 10),
        new Qa04ActivityClassV1(new StableToken("employment-work"), 10),
        new Qa04ActivityClassV1(new StableToken("infrastructure-service-use"), 5),
        new Qa04ActivityClassV1(new StableToken("health-medical"), 3),
        new Qa04ActivityClassV1(new StableToken("governance-security-interaction"), 2),
    });

    public static readonly IReadOnlyList<Qa04OperationFamilyV1> OperationFamilies = Array.AsReadOnly(new[]
    {
        new Qa04OperationFamilyV1(new StableToken("participation-control-resident-action"), 350),
        new Qa04OperationFamilyV1(new StableToken("physical-item-movement-work"), 200),
        new Qa04OperationFamilyV1(new StableToken("society-market-payment-contract"), 200),
        new Qa04OperationFamilyV1(new StableToken("infrastructure-service-delivery"), 150),
        new Qa04OperationFamilyV1(new StableToken("governance-security"), 50),
        new Qa04OperationFamilyV1(new StableToken("environment-spatial-admin-synthetic"), 50),
    });

    public static void ValidateCanonicalContract()
    {
        if (RecordClasses.Select(static x => x.ClassToken).Distinct().Count() != RecordClasses.Count)
            throw new InvalidDataException("qa04.reference.duplicate-record-class");
        if (RecordClasses.Single(x => x.ClassToken.Value == "resident.persistent-identity").Count != 1_000_000)
            throw new InvalidDataException("qa04.reference.resident-count-drift");
        if (RecordClasses.Single(x => x.ClassToken == ParticipationControlModeClass).Count != 1_000_000)
            throw new InvalidDataException("qa04.reference.participation-control-mode-count-drift");
        if (RecordClasses.Aggregate(0UL, static (sum, item) => checked(sum + item.Count)) != CanonicalInitialRecordCount)
            throw new InvalidDataException("qa04.reference.canonical-record-total-drift");
        if (ResidentActivityMix.Sum(static x => (int)x.Percent) != 100)
            throw new InvalidDataException("qa04.reference.activity-mix-total");
        if (OperationFamilies.Sum(static x => (int)x.SharePermille) != 1_000)
            throw new InvalidDataException("qa04.reference.operation-family-total");
        if (ResidentDetailCount(DetailLevelV1.D0Entity) != 100_000 ||
            ResidentDetailCount(DetailLevelV1.D1LocalAggregate) != 300_000 ||
            ResidentDetailCount(DetailLevelV1.D2RegionalAggregate) != 400_000 ||
            ResidentDetailCount(DetailLevelV1.D3BoundarySummary) != 200_000)
            throw new InvalidDataException("qa04.reference.resident-detail-count-drift");

        var steadyCounts = OperationFamilies.Sum(family =>
            checked((long)(SteadyOperationsPerStep * family.SharePermille / 1_000)));
        if ((ulong)steadyCounts != SteadyOperationsPerStep)
            throw new InvalidDataException("qa04.reference.operation-family-rounding");
    }

    public static ulong ResidentDetailCount(DetailLevelV1 level) => level switch
    {
        DetailLevelV1.D0Entity => 100_000,
        DetailLevelV1.D1LocalAggregate => 300_000,
        DetailLevelV1.D2RegionalAggregate => 400_000,
        DetailLevelV1.D3BoundarySummary => 200_000,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static DetailLevelV1 ResidentDetailLevel(ulong residentOrdinal)
    {
        RequireOrdinal(residentOrdinal, 1_000_000, "resident");
        return residentOrdinal switch
        {
            < 100_000 => DetailLevelV1.D0Entity,
            < 400_000 => DetailLevelV1.D1LocalAggregate,
            < 800_000 => DetailLevelV1.D2RegionalAggregate,
            _ => DetailLevelV1.D3BoundarySummary,
        };
    }

    public static Qa04ReferenceRecordV1 Record(StableToken classToken, ulong ordinal)
    {
        var definition = RecordClasses.SingleOrDefault(x => x.ClassToken == classToken)
            ?? throw new KeyNotFoundException($"Unknown QA-04 reference record class: {classToken.Value}");
        RequireOrdinal(ordinal, definition.Count, classToken.Value);
        var id = classToken == ParticipationControlModeClass
            ? DerivedIdentity.DeriveEntityId(
                WorldId,
                creationStep: 0,
                ParticipationDomain,
                OpaqueId128.Zero,
                ParticipationControlModeCreationKind,
                ordinal)
            : DerivedIdentity.DeriveEntityId(
                WorldId,
                creationStep: 0,
                PerformanceDomain,
                OpaqueId128.Zero,
                new StableToken($"perf/{classToken.Value}"),
                ordinal);

        var detail = classToken.Value switch
        {
            "resident.persistent-identity" or "participation.control_mode" => ResidentDetailLevel(ordinal),
            "physical.d0-presence" or "environment.d0-cell-cohort" or "spatial.hot-terrain-brick" => DetailLevelV1.D0Entity,
            "environment.d1-aggregate" => DetailLevelV1.D1LocalAggregate,
            _ => DetailLevelV1.D2RegionalAggregate,
        };
        var baseTile = RegionalTileIndex(id);
        byte? dense = IsDenseD0(detail, ordinal) ? DenseRegionIndex(id) : null;
        var tile = dense is { } denseIndex ? DenseRegionTile(denseIndex, id) : baseTile;
        return new Qa04ReferenceRecordV1(classToken, ordinal, id, detail, tile, dense);
    }

    public static ushort RegionalTileIndex(OpaqueId128 subjectId)
    {
        if (subjectId.IsZero) throw new ArgumentException("subjectId ZERO is invalid.", nameof(subjectId));
        var digest = HashSuite.DomainHash("mv.perf-reference-tile.v1", writer => writer.WriteBytes(subjectId.ToBytes()));
        var low12 = BinaryPrimitives.ReadUInt16BigEndian(digest.AsSpan(digest.Length - 2)) & 0x0fff;
        return checked((ushort)(low12 % RegionalTileCount));
    }

    public static (double X, double Y) PositionWithinTile(OpaqueId128 subjectId, ulong step = 0)
    {
        if (subjectId.IsZero) throw new ArgumentException("subjectId ZERO is invalid.", nameof(subjectId));
        var context = new RandomContextV1(
            WorldId,
            step,
            PerformanceDomain,
            PositionPurpose,
            subjectId,
            OpaqueId128.Zero,
            OpaqueId128.Zero,
            0);
        return (
            DeterministicRandom.UniformDouble(WorldSeed, context, 0),
            DeterministicRandom.UniformDouble(WorldSeed, context, 1));
    }

    public static StableToken ResidentActivity(OpaqueId128 residentId, ulong activationStep)
    {
        if (residentId.IsZero) throw new ArgumentException("residentId ZERO is invalid.", nameof(residentId));
        var context = new RandomContextV1(
            WorldId,
            activationStep,
            PerformanceDomain,
            ActivityPurpose,
            residentId,
            OpaqueId128.Zero,
            OpaqueId128.Zero,
            0);
        var bucket = DeterministicRandom.BoundedUInt64(WorldSeed, context, 0, 100);
        ulong cumulative = 0;
        foreach (var entry in ResidentActivityMix)
        {
            cumulative += entry.Percent;
            if (bucket < cumulative) return entry.ActivityToken;
        }
        throw new InvalidDataException("qa04.reference.activity-selection-fell-through");
    }

    public static ulong OperationCountForStep(ulong injectionStep)
        => checked(SteadyOperationsPerStep + (injectionStep != 0 && injectionStep % BurstEverySteps == 0 ? BurstOperations : 0));

    public static IEnumerable<Qa04OperationDescriptorV1> OperationsForStep(ulong injectionStep)
    {
        var steadyOrdinalBase = 0UL;
        foreach (var family in OperationFamilies)
        {
            var familyCount = checked(SteadyOperationsPerStep * family.SharePermille / 1_000);
            for (ulong ordinal = 0; ordinal < familyCount; ordinal++)
                yield return Operation(injectionStep, family.FamilyToken, steadyOrdinalBase + ordinal);
            steadyOrdinalBase += familyCount;
        }

        if (injectionStep == 0 || injectionStep % BurstEverySteps != 0) yield break;
        for (ulong ordinal = 0; ordinal < BurstOperations; ordinal++)
        {
            var family = OperationFamilies[(int)(ordinal % (ulong)OperationFamilies.Count)];
            yield return Operation(injectionStep, family.FamilyToken, SteadyOperationsPerStep + ordinal);
        }
    }

    private static Qa04OperationDescriptorV1 Operation(ulong step, StableToken family, ulong ordinal)
    {
        var payload = HashSuite.DomainHash("mv.perf-reference-operation-payload.v1", writer =>
        {
            writer.WriteMapStart(4);
            writer.WriteUnsigned(0); writer.WriteAsciiText(BenchmarkProfileId);
            writer.WriteUnsigned(1); writer.WriteUnsigned(step);
            writer.WriteUnsigned(2); writer.WriteAsciiText(family.Value);
            writer.WriteUnsigned(3); writer.WriteUnsigned(ordinal);
        });
        var id = NonZeroTrunc128("mv.perf-reference-operation-id.v1", writer =>
        {
            writer.WriteMapStart(4);
            writer.WriteUnsigned(0); writer.WriteAsciiText(BenchmarkProfileId);
            writer.WriteUnsigned(1); writer.WriteUnsigned(step);
            writer.WriteUnsigned(2); writer.WriteAsciiText(family.Value);
            writer.WriteUnsigned(3); writer.WriteUnsigned(ordinal);
        });
        return new Qa04OperationDescriptorV1(step, family, ordinal, id, payload);
    }

    private static OpaqueId128 DeriveBenchmarkWorldId()
        => NonZeroTrunc128("mv.perf-reference-world.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteAsciiText(BenchmarkProfileId);
            writer.WriteUnsigned(1); writer.WriteBytes(Convert.FromHexString(WorldSeedHex));
        });

    private static OpaqueId128 NonZeroTrunc128(string domain, Action<MvDcborWriter> write)
    {
        var digest = HashSuite.DomainHash(domain, write);
        var id = HashSuite.Trunc128(digest);
        if (id.IsZero) throw new InvalidDataException("qa04.reference.derived-id-zero");
        return id;
    }

    private static bool IsDenseD0(DetailLevelV1 detail, ulong ordinal)
        => detail == DetailLevelV1.D0Entity && ordinal % 4 == 0;

    private static byte DenseRegionIndex(OpaqueId128 id)
    {
        var digest = HashSuite.DomainHash("mv.perf-reference-dense-region.v1", writer => writer.WriteBytes(id.ToBytes()));
        return checked((byte)(digest[0] % 4));
    }

    private static ushort DenseRegionTile(byte denseRegionIndex, OpaqueId128 id)
    {
        // Four fixed 8x8 dense regions, one in each quadrant. The subject hash selects a tile
        // within the region without introducing iteration-order dependence.
        var origins = new (int Row, int Column)[] { (12, 12), (12, 44), (44, 12), (44, 44) };
        var origin = origins[denseRegionIndex];
        var digest = HashSuite.DomainHash("mv.perf-reference-dense-tile.v1", writer => writer.WriteBytes(id.ToBytes()));
        var local = digest[0] % 64;
        var row = origin.Row + local / 8;
        var column = origin.Column + local % 8;
        return checked((ushort)(row * RegionalTileColumns + column));
    }

    private static void RequireOrdinal(ulong ordinal, ulong count, string name)
    {
        if (ordinal >= count) throw new ArgumentOutOfRangeException(nameof(ordinal), $"{name} ordinal must be < {count}.");
    }
}
