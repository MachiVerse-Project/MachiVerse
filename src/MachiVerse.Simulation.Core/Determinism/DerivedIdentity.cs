namespace MachiVerse.Simulation.Core.Determinism;

public static class DerivedIdentity
{
    public static OpaqueId128 DeriveEntityId(
        OpaqueId128 worldId,
        ulong creationStep,
        StableToken creatorDomain,
        OpaqueId128 creatorEntityId,
        StableToken creationKind,
        ulong localOrdinal)
    {
        for (ulong nonce = 0; ; nonce++)
        {
            var candidate = HashSuite.Trunc128(HashSuite.DomainHash("mv.entity.v1", writer =>
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(creationStep);
                writer.WriteUnsigned(2); writer.WriteAsciiText(creatorDomain.Value);
                writer.WriteUnsigned(3); writer.WriteBytes(creatorEntityId.ToBytes());
                writer.WriteUnsigned(4); writer.WriteAsciiText(creationKind.Value);
                writer.WriteUnsigned(5); writer.WriteUnsigned(localOrdinal);
                writer.WriteUnsigned(6); writer.WriteUnsigned(nonce);
            }));

            if (!candidate.IsZero) return candidate;
            if (nonce == ulong.MaxValue) throw new InvalidOperationException("Could not derive non-zero EntityId.");
        }
    }

    public static OpaqueId128 DeriveIntentId(
        OpaqueId128 worldId,
        ulong effectiveStep,
        byte sourceKind,
        OpaqueId128 sourceId,
        StableToken domain,
        StableToken mutationKind,
        ulong localOrdinal)
    {
        return HashSuite.Trunc128(HashSuite.DomainHash("mv.intent.v1", writer =>
        {
            writer.WriteMapStart(7);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(effectiveStep);
            writer.WriteUnsigned(2); writer.WriteUnsigned(sourceKind);
            writer.WriteUnsigned(3); writer.WriteBytes(sourceId.ToBytes());
            writer.WriteUnsigned(4); writer.WriteAsciiText(domain.Value);
            writer.WriteUnsigned(5); writer.WriteAsciiText(mutationKind.Value);
            writer.WriteUnsigned(6); writer.WriteUnsigned(localOrdinal);
        }));
    }
}
