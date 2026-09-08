using System.Security.Cryptography;
using System.Text;

namespace MachiVerse.Gateway.Auth;

public sealed record OidcEphemeralSecrets(
    string State,
    string Nonce,
    string PkceVerifier,
    string PkceChallenge);

public static class AuthSecurityPrimitives
{
    public static OidcEphemeralSecrets CreateOidcSecrets()
    {
        var state = RandomBase64Url(32);
        var nonce = RandomBase64Url(32);
        var verifier = RandomBase64Url(32);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new OidcEphemeralSecrets(state, nonce, verifier, challenge);
    }

    public static byte[] DigestAsciiSecret(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return SHA256.HashData(Encoding.ASCII.GetBytes(value));
    }

    public static bool VerifyAsciiSecret(string candidate, ReadOnlySpan<byte> expectedDigest)
    {
        if (string.IsNullOrEmpty(candidate) || expectedDigest.Length != 32) return false;
        var actual = SHA256.HashData(Encoding.ASCII.GetBytes(candidate));
        return CryptographicOperations.FixedTimeEquals(actual, expectedDigest);
    }

    public static byte[] RandomId128()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        if (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0) bytes[0] = 1;
        return bytes;
    }

    public static byte[] RandomSecret256()
        => RandomNumberGenerator.GetBytes(32);

    public static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string RandomBase64Url(int byteCount)
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));
}

public interface ILoginSecretStore
{
    string Put(string pkceVerifier, DateTimeOffset expiresAt);
    string Take(string secretReference, DateTimeOffset now);
    void Delete(string secretReference);
}

public sealed class InMemoryLoginSecretStore : ILoginSecretStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Put(string pkceVerifier, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pkceVerifier);
        var secretRef = "login-secret:" + Convert.ToHexStringLower(AuthSecurityPrimitives.RandomId128());
        lock (_gate)
        {
            _entries.Add(secretRef, new Entry(pkceVerifier, expiresAt));
        }
        return secretRef;
    }

    public string Take(string secretReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretReference);
        lock (_gate)
        {
            if (!_entries.Remove(secretReference, out var entry))
                throw new InvalidDataException("auth.login-secret-missing");
            if (now >= entry.ExpiresAt)
                throw new InvalidDataException("auth.login-expired");
            return entry.Secret;
        }
    }

    public void Delete(string secretReference)
    {
        if (string.IsNullOrWhiteSpace(secretReference)) return;
        lock (_gate) _entries.Remove(secretReference);
    }

    private sealed record Entry(string Secret, DateTimeOffset ExpiresAt);
}
