using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.ContractFixtures;

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        try
        {
            var repoRoot = FindRepositoryRoot();
            if (args.Length > 0 && string.Equals(args[0], "generate-persistence-seed", StringComparison.Ordinal))
            {
                var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(repoRoot, "artifacts", "persistence-fixture-seed.json");
                GeneratePersistenceSeed(output);
                Console.WriteLine($"Generated {output}");
                return 0;
            }

            VerifyAll(repoRoot);
            Console.WriteLine("Contract fixture verification passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void VerifyAll(string repoRoot)
    {
        var root = Path.Combine(repoRoot, "tests", "contract-fixtures", "v1");
        VerifyStableTokens(Path.Combine(root, "stable-token-vectors.json"));
        VerifySha256(Path.Combine(root, "sha256-vectors.json"));
        VerifyDeterminismVectors(Path.Combine(root, "determinism-vectors.json"));
        VerifyProtocolVersion(Path.Combine(root, "protobuf", "protocol-version-v1.json"));
        VerifySchemaFiles(repoRoot, Path.Combine(root, "schema-source-manifest.json"));
    }

    private static void VerifyStableTokens(string path)
    {
        var fixture = ReadJson<StableTokenFixture>(path);
        foreach (var token in fixture.Valid)
        {
            Require(StableTokenRegex().IsMatch(token), $"Expected valid StableToken: {token}");
        }

        foreach (var token in fixture.Invalid)
        {
            Require(!StableTokenRegex().IsMatch(token), $"Expected invalid StableToken: {token}");
        }
    }

    private static void VerifySha256(string path)
    {
        var vectors = ReadJson<List<Sha256Vector>>(path);
        foreach (var vector in vectors)
        {
            var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(vector.InputUtf8)));
            Require(string.Equals(actual, vector.ExpectedHex, StringComparison.Ordinal), $"SHA-256 mismatch for '{vector.Name}'.");
        }
    }

    private static void VerifyDeterminismVectors(string path)
    {
        var fixture = ReadJson<DeterminismFixture>(path);
        foreach (var vector in fixture.Dcbor)
        {
            var actual = Convert.ToHexStringLower(EncodeReferenceDcbor(vector));
            Require(string.Equals(actual, vector.ExpectedHex, StringComparison.Ordinal), $"MV-DCBOR mismatch for '{vector.Name}'.");
        }

        foreach (var vector in fixture.DomainHash)
        {
            var actual = Convert.ToHexStringLower(DomainHash(vector.Label, Convert.FromHexString(vector.DcborHex)));
            Require(string.Equals(actual, vector.ExpectedHashHex, StringComparison.Ordinal), $"DomainHash mismatch for '{vector.Name}'.");
        }

        foreach (var vector in fixture.DerivedId)
        {
            var digest = DomainHash(vector.Label, Convert.FromHexString(vector.DcborHex));
            var actual = Convert.ToHexStringLower(digest.AsSpan(0, 16));
            Require(string.Equals(actual, vector.ExpectedTrunc128Hex, StringComparison.Ordinal), $"Derived 128-bit identity mismatch for '{vector.Name}'.");
        }
    }

    private static byte[] EncodeReferenceDcbor(DcborVector vector)
    {
        return vector.Kind switch
        {
            "unsigned" when vector.UnsignedValue.HasValue => EncodeUnsigned(vector.UnsignedValue.Value),
            "map-u-u" when vector.MapKey.HasValue && vector.MapValue.HasValue =>
                [0xa1, .. EncodeUnsigned(vector.MapKey.Value), .. EncodeUnsigned(vector.MapValue.Value)],
            _ => throw new InvalidDataException($"Unsupported reference MV-DCBOR fixture kind: {vector.Kind}")
        };
    }

    private static byte[] EncodeUnsigned(ulong value)
    {
        if (value < 24) return [(byte)value];
        if (value <= byte.MaxValue) return [0x18, (byte)value];
        if (value <= ushort.MaxValue)
        {
            var bytes = new byte[3];
            bytes[0] = 0x19;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(1), (ushort)value);
            return bytes;
        }
        if (value <= uint.MaxValue)
        {
            var bytes = new byte[5];
            bytes[0] = 0x1a;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1), (uint)value);
            return bytes;
        }

        var result = new byte[9];
        result[0] = 0x1b;
        BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(1), value);
        return result;
    }

    private static byte[] DomainHash(string label, ReadOnlySpan<byte> dcbor)
    {
        Require(!label.Any(static c => c > 0x7f), $"DomainHash label must be ASCII: {label}");
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var preimage = new byte[labelBytes.Length + 1 + dcbor.Length];
        labelBytes.CopyTo(preimage, 0);
        preimage[labelBytes.Length] = 0;
        dcbor.CopyTo(preimage.AsSpan(labelBytes.Length + 1));
        return SHA256.HashData(preimage);
    }

    private static void VerifyProtocolVersion(string path)
    {
        var fixture = ReadJson<ProtocolVersionFixture>(path);
        var bytes = Convert.FromBase64String(fixture.Base64);
        var message = ProtocolVersionV1.Parser.ParseFrom(bytes);
        Require(message.Major == fixture.Major, "ProtocolVersionV1.major mismatch.");
        Require(message.Minor == fixture.Minor, "ProtocolVersionV1.minor mismatch.");
        Require(message.ToByteArray().AsSpan().SequenceEqual(bytes), "ProtocolVersionV1 fixture is not canonical for the generated schema.");
    }

    private static void VerifySchemaFiles(string repoRoot, string path)
    {
        var fixture = ReadJson<SchemaSourceManifest>(path);
        foreach (var relative in fixture.RequiredFiles)
        {
            Require(File.Exists(Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar))), $"Missing schema source: {relative}");
        }
    }

    private static void GeneratePersistenceSeed(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        var seed = new
        {
            fixture_version = "1.0",
            world_seed_hex = new string('0', 64),
            world_id_hex = new string('0', 31) + "1",
            simulation_step = 0,
            note = "Seed manifest for SIM-03 persistence fixture generation; no persistence binary authority is implied."
        };
        File.WriteAllText(output, JsonSerializer.Serialize(seed, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static T ReadJson<T>(string path) where T : class
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
           ?? throw new InvalidDataException($"Could not deserialize fixture: {path}");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(current.FullName, "docs", "protocols", "schema")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenRegex();

    private sealed record StableTokenFixture(List<string> Valid, List<string> Invalid);
    private sealed record Sha256Vector(string Name, string InputUtf8, string ExpectedHex);
    private sealed record DeterminismFixture(List<DcborVector> Dcbor, List<DomainHashVector> DomainHash, List<DerivedIdVector> DerivedId);
    private sealed record DcborVector(string Name, string Kind, ulong? UnsignedValue, ulong? MapKey, ulong? MapValue, string ExpectedHex);
    private sealed record DomainHashVector(string Name, string Label, string DcborHex, string ExpectedHashHex);
    private sealed record DerivedIdVector(string Name, string Label, string DcborHex, string ExpectedTrunc128Hex);
    private sealed record ProtocolVersionFixture(uint Major, uint Minor, string Base64);
    private sealed record SchemaSourceManifest(List<string> RequiredFiles);
}
