using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class PartitionStateHeaderAsyncStreamingSmoke
{
    [ModuleInitializer]
    internal static void Run()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var identity = StandardDomainPartitionRegistry.Get("spatial.terrain_geometry");
        var records = new[]
        {
            Record(identity, "00000000000000000000000000000201", "a"),
            Record(identity, "00000000000000000000000000000202", "b"),
        };
        static byte[] Digest(string value) => SHA256.HashData(Encoding.ASCII.GetBytes(value));

        var sync = PartitionStateHeaderStreamingV1.CreateCanonical(
            identity,
            revision: 3,
            basisStep: 7,
            detailLevel: DetailLevelV1.D0Entity,
            itemCount: 2,
            records,
            Digest);
        var asyncHeader = await PartitionStateHeaderAsyncStreamingV1.CreateCanonicalAsync(
            identity,
            revision: 3,
            basisStep: 7,
            detailLevel: DetailLevelV1.D0Entity,
            itemCount: 2,
            Async(records),
            Digest);

        Require(sync.PartitionId == asyncHeader.PartitionId &&
                sync.OwnerDomain == asyncHeader.OwnerDomain &&
                sync.Schema == asyncHeader.Schema &&
                sync.Revision == asyncHeader.Revision &&
                sync.BasisStep == asyncHeader.BasisStep &&
                sync.DetailLevel == asyncHeader.DetailLevel &&
                sync.ItemCount == asyncHeader.ItemCount &&
                sync.CanonicalDigest.AsSpan().SequenceEqual(asyncHeader.CanonicalDigest),
            "Async partition streaming hash must be byte-for-byte identical to the synchronous streaming path.");
    }

    private static DomainRecordEnvelopeV1<string> Record(
        DomainPartitionIdentityV1 identity,
        string id,
        string payload)
        => new(
            OpaqueId128.Parse(id),
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);

    private static async IAsyncEnumerable<DomainRecordEnvelopeV1<string>> Async(
        IEnumerable<DomainRecordEnvelopeV1<string>> records)
    {
        foreach (var record in records)
        {
            await Task.Yield();
            yield return record;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
