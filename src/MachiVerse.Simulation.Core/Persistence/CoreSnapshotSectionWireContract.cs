using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public enum CoreSnapshotLogicalItemKindV1 : byte
{
    WorldStateHeader = 1,
    ScheduledOperation = 2,
    DurableOperation = 3,
    DetailDirectoryItem = 4,
    DomainRuntimeDescriptor = 5,
    ConfigField = 6,
}

public sealed record CoreSnapshotSectionWireContractV1(
    string SectionId,
    SchemaRefV1 SectionSchema,
    CoreSnapshotLogicalItemKindV1 LogicalItemKind,
    string FragmentMessageName);

/// <summary>
/// Machine-readable binding for the P4-04 six-Core-section amendment.
/// This registry identifies the normative logical schema and protobuf fragment message only;
/// protobuf bytes are not semantic digest authority.
/// </summary>
public static class CoreSnapshotSectionWireRegistryV1
{
    public const string WorldStateHeaderDigestDomain = "mv.core-world-state-header.v1";
    public const string DomainRegistryDigestDomain = "mv.core-domain-registry-state.v1";
    public const string NormativeProtoPath = "docs/design/proto/phase4-core-snapshot-sections-v1.proto";

    private static readonly CoreSnapshotSectionWireContractV1[] CanonicalEntries =
    [
        new(
            CoreSnapshotOwnerSectionRegistryV1.ConfigState,
            new SchemaRefV1("config.simulation-core"),
            CoreSnapshotLogicalItemKindV1.ConfigField,
            "CoreConfigStateSnapshotFragmentWireV1"),
        new(
            CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
            new SchemaRefV1("core.detail-state"),
            CoreSnapshotLogicalItemKindV1.DetailDirectoryItem,
            "CoreDetailDirectorySnapshotFragmentWireV1"),
        new(
            CoreSnapshotOwnerSectionRegistryV1.DomainRegistry,
            new SchemaRefV1("core.domain-registry-state"),
            CoreSnapshotLogicalItemKindV1.DomainRuntimeDescriptor,
            "CoreDomainRegistrySnapshotFragmentWireV1"),
        new(
            CoreSnapshotOwnerSectionRegistryV1.OperationState,
            new SchemaRefV1("core.operation-state"),
            CoreSnapshotLogicalItemKindV1.DurableOperation,
            "CoreOperationStateSnapshotFragmentWireV1"),
        new(
            CoreSnapshotOwnerSectionRegistryV1.SchedulerState,
            new SchemaRefV1("core.scheduler-state"),
            CoreSnapshotLogicalItemKindV1.ScheduledOperation,
            "CoreSchedulerStateSnapshotFragmentWireV1"),
        new(
            CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader,
            new SchemaRefV1("core.world-state"),
            CoreSnapshotLogicalItemKindV1.WorldStateHeader,
            "CoreWorldStateHeaderSnapshotWireV1"),
    ];

    public static IReadOnlyList<CoreSnapshotSectionWireContractV1> Entries { get; }
        = Array.AsReadOnly(CanonicalEntries);

    static CoreSnapshotSectionWireRegistryV1()
    {
        ValidateStandardRegistry();
    }

    public static CoreSnapshotSectionWireContractV1 Get(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        return CanonicalEntries.SingleOrDefault(entry =>
                string.Equals(entry.SectionId, sectionId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Core snapshot section wire contract is not registered: {sectionId}");
    }

    public static void ValidateStandardRegistry()
    {
        if (CanonicalEntries.Length != 6)
            throw new InvalidOperationException("Core snapshot wire registry must contain exactly six sections.");
        if (CanonicalEntries.Select(static entry => entry.SectionId).Distinct(StringComparer.Ordinal).Count() != CanonicalEntries.Length)
            throw new InvalidOperationException("Core snapshot wire registry contains a duplicate section id.");
        if (CanonicalEntries.Select(static entry => entry.LogicalItemKind).Distinct().Count() != CanonicalEntries.Length)
            throw new InvalidOperationException("Core snapshot wire registry contains a duplicate logical item kind.");

        var expected = StandardSnapshotSectionSetV1.SectionIds
            .Where(StandardSnapshotSectionSetV1.IsCoreSection)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var actual = CanonicalEntries
            .Select(static entry => entry.SectionId)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("Core snapshot wire registry does not match the standard six-section set.");

        foreach (var entry in CanonicalEntries)
        {
            _ = new Determinism.StableToken(entry.SectionId);
            _ = entry.SectionSchema.SchemaId.Value;
            if (entry.SectionSchema.Version != new SchemaVersionV1(1, 0))
                throw new InvalidOperationException($"Core snapshot section schema must be v1.0: {entry.SectionId}");
            if (string.IsNullOrWhiteSpace(entry.FragmentMessageName))
                throw new InvalidOperationException($"Core snapshot fragment message name is missing: {entry.SectionId}");
        }
    }
}
