using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace MachiVerse.Simulation.Core.Determinism;

public static class HashSuite
{
    public static byte[] Hash256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public static byte[] DomainHash(string label, Action<MvDcborWriter> writeValue)
    {
        ArgumentNullException.ThrowIfNull(writeValue);
        var labelBytes = EncodeDomainLabel(label);
        var writer = new MvDcborWriter();
        writeValue(writer);
        var valueBytes = writer.ToArray();
        var preimage = new byte[labelBytes.Length + 1 + valueBytes.Length];
        labelBytes.CopyTo(preimage, 0);
        preimage[labelBytes.Length] = 0;
        valueBytes.CopyTo(preimage, labelBytes.Length + 1);
        return SHA256.HashData(preimage);
    }

    /// <summary>
    /// Computes the exact same domain hash as <see cref="DomainHash"/> while feeding canonical
    /// MV-DCBOR bytes directly into SHA-256. The full canonical value/preimage is never retained.
    /// </summary>
    public static byte[] DomainHashStreaming(string label, Action<MvDcborWriter> writeValue)
    {
        ArgumentNullException.ThrowIfNull(writeValue);
        using var session = BeginDomainHashStreaming(label);
        writeValue(session.Writer);
        return session.Complete();
    }

    /// <summary>
    /// Opens an incremental domain-hash session. Callers may keep the canonical writer across async
    /// suspension points and finalize after the complete value has been emitted. The resulting hash
    /// uses the identical ASCII-label + NUL + MV-DCBOR preimage as DomainHash/DomainHashStreaming.
    /// </summary>
    public static StreamingDomainHashSession BeginDomainHashStreaming(string label)
        => new(EncodeDomainLabel(label));

    public static OpaqueId128 Trunc128(ReadOnlySpan<byte> hash)
    {
        if (hash.Length < 16) throw new ArgumentException("Hash must contain at least 16 bytes.", nameof(hash));
        return OpaqueId128.FromBytes(hash[..16]);
    }

    private static byte[] EncodeDomainLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (label.Any(static c => c > 0x7f))
            throw new ArgumentException("Domain label must be ASCII.", nameof(label));
        return Encoding.ASCII.GetBytes(label);
    }

    public sealed class StreamingDomainHashSession : IDisposable
    {
        private readonly IncrementalHash _hash;
        private bool _completed;
        private bool _disposed;

        internal StreamingDomainHashSession(byte[] labelBytes)
        {
            ArgumentNullException.ThrowIfNull(labelBytes);
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _hash.AppendData(labelBytes);
            Span<byte> separator = stackalloc byte[1];
            separator[0] = 0;
            _hash.AppendData(separator);
            Writer = new MvDcborWriter(new IncrementalHashBufferWriter(_hash));
        }

        public MvDcborWriter Writer { get; }

        public byte[] Complete()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed) throw new InvalidOperationException("Domain hash streaming session is already complete.");
            _completed = true;
            return _hash.GetHashAndReset();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hash.Dispose();
        }
    }

    private sealed class IncrementalHashBufferWriter : IBufferWriter<byte>
    {
        private readonly IncrementalHash _hash;
        private byte[] _buffer = new byte[256];

        public IncrementalHashBufferWriter(IncrementalHash hash)
            => _hash = hash ?? throw new ArgumentNullException(nameof(hash));

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count != 0) _hash.AppendData(_buffer.AsSpan(0, count));
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
            if (sizeHint == 0) sizeHint = 1;
            if (sizeHint <= _buffer.Length) return;
            Array.Resize(ref _buffer, sizeHint);
        }
    }
}
