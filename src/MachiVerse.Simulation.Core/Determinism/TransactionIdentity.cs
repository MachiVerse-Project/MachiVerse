namespace MachiVerse.Simulation.Core.Determinism;

public enum CausalityRefKindV1 : byte
{
    Operation = 0,
    Event = 1,
    Intent = 2,
    Entity = 3,
    Transaction = 4,
    ConfigGeneration = 5,
    HistoryRecord = 6,
    PartitionRevision = 7,
}

public sealed record CausalityRefV1
{
    public CausalityRefV1(
        CausalityRefKindV1 kind,
        ReadOnlySpan<byte> identityBytes,
        ulong? basisStep = null)
    {
        if (!Enum.IsDefined(kind))
            throw new InvalidDataException("transaction.causality-kind-invalid");
        if (identityBytes.Length == 0 || identityBytes.Length > 64)
            throw new InvalidDataException("transaction.causality-identity-size-invalid");

        Kind = kind;
        IdentityBytes = identityBytes.ToArray();
        BasisStep = basisStep;
    }

    public CausalityRefKindV1 Kind { get; }
    public byte[] IdentityBytes { get; }
    public ulong? BasisStep { get; }
}

public static class TransactionIdentityV1
{
    public static OpaqueId128 Derive(
        OpaqueId128 worldId,
        StableToken transactionKind,
        ulong basisStep,
        CausalityRefV1 rootCausalityRef,
        IEnumerable<OpaqueId128> subjectRefs,
        ulong stableLocalOrdinal)
    {
        if (worldId.IsZero)
            throw new InvalidDataException("transaction.world-id-zero");
        ArgumentNullException.ThrowIfNull(rootCausalityRef);
        ArgumentNullException.ThrowIfNull(subjectRefs);

        var subjects = subjectRefs.Order().ToArray();
        if (subjects.Any(static subject => subject.IsZero))
            throw new InvalidDataException("transaction.subject-id-zero");
        if (subjects.Distinct().Count() != subjects.Length)
            throw new InvalidDataException("transaction.subject-id-duplicate");

        var digest = HashSuite.DomainHash("mv.transaction.v1", writer =>
        {
            writer.WriteMapStart(6);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteAsciiText(transactionKind.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(3); WriteCausalityRef(writer, rootCausalityRef);
            writer.WriteUnsigned(4);
            writer.WriteArrayStart((ulong)subjects.Length);
            foreach (var subject in subjects)
                writer.WriteBytes(subject.ToBytes());
            writer.WriteUnsigned(5); writer.WriteUnsigned(stableLocalOrdinal);
        });

        var transactionId = HashSuite.Trunc128(digest);
        if (transactionId.IsZero)
            throw new InvalidOperationException("transaction.derived-id-zero");
        return transactionId;
    }

    private static void WriteCausalityRef(MvDcborWriter writer, CausalityRefV1 reference)
    {
        writer.WriteMapStart(3);
        writer.WriteUnsigned(0); writer.WriteUnsigned((uint)reference.Kind);
        writer.WriteUnsigned(1); writer.WriteBytes(reference.IdentityBytes);
        writer.WriteUnsigned(2);
        if (reference.BasisStep is { } basisStep)
        {
            writer.WriteArrayStart(1);
            writer.WriteUnsigned(basisStep);
        }
        else
        {
            writer.WriteArrayStart(0);
        }
    }
}
