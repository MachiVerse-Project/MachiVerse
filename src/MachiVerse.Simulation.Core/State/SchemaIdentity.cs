using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.State;

public readonly record struct SchemaVersionV1
{
    public SchemaVersionV1(ushort major, ushort minor)
    {
        if (major == 0) throw new ArgumentOutOfRangeException(nameof(major), "Authoritative schema major must be non-zero.");
        Major = major;
        Minor = minor;
    }

    public ushort Major { get; }
    public ushort Minor { get; }
}

public readonly record struct SchemaRefV1
{
    public SchemaRefV1(StableToken schemaId, SchemaVersionV1 version)
    {
        SchemaId = schemaId;
        Version = version;
    }

    public StableToken SchemaId { get; }
    public SchemaVersionV1 Version { get; }
}

public enum PersistenceClassV1 : byte
{
    AuthoritativeAlways = 0,
    AuthoritativeReconstructableWithRecipe = 1,
    DerivedCacheRebuildable = 2,
    DiagnosticOnly = 3,
}

public enum PrimaryKeyKindV1 : byte
{
    RecordId128 = 0,
}

public enum CanonicalOrderKindV1 : byte
{
    RecordIdBytewiseAscending = 0,
}
