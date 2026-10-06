using System.Reflection;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04PrefixDigestGcSmoke
{
    public static void Run()
    {
        // Independent run-scoped caches share the generic code, but never mutable cache state.
        Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(CheckHistory))).GetAwaiter().GetResult();
        Console.WriteLine("QA-04 prefix digest parallel compacting-GC / raw rehash PASS");
    }

    private static void CheckHistory()
    {
        var identity = SocietyMarketTransactionPartitionIdentityV2.Identity;
        var cacheType = typeof(Qa04ProductionStep2CanonicalDigestCacheV1).Assembly
            .GetType("MachiVerse.Simulation.Core.Performance.Qa04RecordIdPrefixPartitionDigestCacheV2`1", true)!
            .MakeGenericType(typeof(byte[]));
        var encodeRecord = typeof(PartitionStateHeaderV1)
            .GetMethod("EncodeCanonicalRecord", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(byte[]))
            .CreateDelegate<Func<DomainRecordEnvelopeV1<byte[]>, byte[], byte[]>>();
        byte[] Encode(DomainRecordEnvelopeV1<byte[]> record)
            => encodeRecord(record, SHA256.HashData(record.Payload));
        var cache = Activator.CreateInstance(cacheType, (Func<DomainRecordEnvelopeV1<byte[]>, byte[]>)Encode)!;
        var createHeader = cacheType.GetMethod("CreateHeader")!;
        var binding = Qa04CanonicalOperationBindingV1.Bind(
            Qa04ReferenceLoadV1.OperationsForStep(1).First(d => d.FamilyToken.Value == "society-market-payment-contract"), 1);
        var records = new List<DomainRecordEnvelopeV1<byte[]>>();
        DomainPartitionStateV1<byte[]>? frozenState = null;
        PartitionStateHeaderV1? frozenHeader = null;

        for (ulong step = 1; step <= 160; step++)
        {
            var changes = new List<Qa04CanonicalOperationMutationChangeV1>();
            if (step == 1)
            {
                // Multiple records in one prefix, widely separated prefixes, and variable byte lengths.
                for (var index = 0; index < 24; index++)
                    records.Add(NewRecord(index, 0, identity.RecordSchema));
            }
            else if (step % 5 != 0)
            {
                var reviseIndex = checked((int)(step % 24));
                var revised = records[reviseIndex].Revise(new byte[checked((int)(step % 47) + 1)]);
                revised.Payload[0] = checked((byte)step);
                records[reviseIndex] = revised;
                changes.Add(Change(revised, "revise"));
                var added = NewRecord(records.Count, step, identity.RecordSchema);
                records.Add(added);
                changes.Add(Change(added, "create"));
                changes.Reverse(); // Input order must not influence normalized prefix order.
            }
            var state = new DomainPartitionStateV1<byte[]>(identity, records);
            if (step % 16 == 0)
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            var cached = Cached(state, step, changes);
            var raw = RecordIdPrefixPartitionDigestV2.CreateHeader(state, step + 1, step,
                DetailLevelV1.D0Entity, Encode);
            Require(cached, raw);
            Require(Cached(state, step, changes), raw); // Exact replay is side-effect free.
            if (step == 80) { frozenState = state; frozenHeader = cached; }
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Require(frozenHeader!, RecordIdPrefixPartitionDigestV2.CreateHeader(frozenState!, 81, 80,
            DetailLevelV1.D0Entity, Encode));
        // Jumping to an unrelated Step must rebuild from canonical state, including empty deltas.
        var finalState = new DomainPartitionStateV1<byte[]>(identity, records);
        Require(Cached(finalState, 200, []), RecordIdPrefixPartitionDigestV2.CreateHeader(finalState, 201, 200,
            DetailLevelV1.D0Entity, Encode));

        PartitionStateHeaderV1 Cached(DomainPartitionStateV1<byte[]> state, ulong step,
            IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
            => (PartitionStateHeaderV1)createHeader.Invoke(cache, [state, step + 1, step, DetailLevelV1.D0Entity, changes])!;

        Qa04CanonicalOperationMutationChangeV1 Change(DomainRecordEnvelopeV1<byte[]> record, string mode)
            => new(binding.SourceDescriptor.FamilyToken, binding.Operation.OperationKind,
                binding.SourceDescriptor.OperationId, binding.Operation.ImmutablePayloadDigest.ToByteArray(),
                binding.OrderKey, identity.PartitionId, record.RecordId, record.RecordSchema, record.Revision,
                record.CreatedStep, record.DetailLevel, new StableToken(mode));
    }

    private static DomainRecordEnvelopeV1<byte[]> NewRecord(int index, ulong step, SchemaRefV1 schema)
    {
        var id = new byte[16];
        id[0] = (index % 3) switch { 0 => (byte)0, 1 => (byte)128, _ => (byte)255 };
        if (index % 3 == 2) id[1] = 252; // Include the final legal 14-bit prefix.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(id.AsSpan(12), index + 1);
        var payload = new byte[index % 53 + 1];
        payload[0] = checked((byte)index);
        return new(OpaqueId128.FromBytes(id), schema, 1, step, null, DetailLevelV1.D0Entity, null, payload);
    }

    private static void Require(PartitionStateHeaderV1 cached, PartitionStateHeaderV1 raw)
    {
        if (cached.DigestAlgorithm != PartitionCanonicalDigestAlgorithmV1.RecordIdPrefixV2 ||
            cached.ItemCount != raw.ItemCount || !cached.CanonicalDigest.AsSpan().SequenceEqual(raw.CanonicalDigest))
            throw new InvalidOperationException("QA-04 prefix digest drift after compaction / incremental merge.");
    }
}
