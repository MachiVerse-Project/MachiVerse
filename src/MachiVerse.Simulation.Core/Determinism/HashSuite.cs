using System.Security.Cryptography;
using System.Text;

namespace MachiVerse.Simulation.Core.Determinism;

public static class HashSuite
{
    public static byte[] Hash256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public static byte[] DomainHash(string label, Action<MvDcborWriter> writeValue)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(writeValue);
        if (label.Any(static c => c > 0x7f))
            throw new ArgumentException("Domain label must be ASCII.", nameof(label));

        var labelBytes = Encoding.ASCII.GetBytes(label);
        var writer = new MvDcborWriter();
        writeValue(writer);
        var valueBytes = writer.ToArray();
        var preimage = new byte[labelBytes.Length + 1 + valueBytes.Length];
        labelBytes.CopyTo(preimage, 0);
        preimage[labelBytes.Length] = 0;
        valueBytes.CopyTo(preimage, labelBytes.Length + 1);
        return SHA256.HashData(preimage);
    }

    public static OpaqueId128 Trunc128(ReadOnlySpan<byte> hash)
    {
        if (hash.Length < 16) throw new ArgumentException("Hash must contain at least 16 bytes.", nameof(hash));
        return OpaqueId128.FromBytes(hash[..16]);
    }
}
