using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;

namespace MachiVerse.Simulation.Core.Runtime;

public static class AlphaStartupConfigAuthorityV1
{
    /// <summary>既存worldは永続化済みConfigを維持する。起動時にConfig履歴を変更しない。</summary>
    public static EffectiveCoreConfig Resolve(
        EffectiveCoreConfig requested, ReadOnlySpan<byte> persistedDigest, string bundledConfigDirectory)
    {
        if (CryptographicOperations.FixedTimeEquals(requested.Digest, persistedDigest)) return requested;
        var standard = new CoreConfigCoordinator().LoadStartup(
            File.ReadAllText(Path.Combine(bundledConfigDirectory, "simulation-core.toml")));
        if (CryptographicOperations.FixedTimeEquals(requested.Digest, standard.Digest))
        {
            var legacy = new CoreConfigCoordinator().LoadStartup(
                File.ReadAllText(Path.Combine(bundledConfigDirectory, "simulation-core-v1-legacy.toml")));
            if (CryptographicOperations.FixedTimeEquals(legacy.Digest, persistedDigest)) return legacy;
        }
        throw new InvalidDataException(
            "alpha.config-digest-mismatch: set MACHIVERSE_CORE_CONFIG to the persisted world's Config; Config migration requires the durable Config change path.");
    }
}
