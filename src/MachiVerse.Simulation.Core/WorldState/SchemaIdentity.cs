using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public readonly record struct SchemaVersionV1(ushort Major, ushort Minor) : IComparable<SchemaVersionV1>
{
    public int CompareTo(SchemaVersionV1 other)
    {
        var major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}

public readonly record struct SchemaRefV1 : IComparable<SchemaRefV1>
{
    public SchemaRefV1(string schemaId, ushort major = 1, ushort minor = 0)
        : this(new StableToken(schemaId), new SchemaVersionV1(major, minor))
    {
    }

    public SchemaRefV1(StableToken schemaId, SchemaVersionV1 version)
    {
        SchemaId = schemaId;
        Version = version;
    }

    public StableToken SchemaId { get; }
    public SchemaVersionV1 Version { get; }

    public int CompareTo(SchemaRefV1 other)
    {
        var schema = string.CompareOrdinal(SchemaId.Value, other.SchemaId.Value);
        return schema != 0 ? schema : Version.CompareTo(other.Version);
    }

    public override string ToString() => $"{SchemaId.Value}/{Version}";
}
