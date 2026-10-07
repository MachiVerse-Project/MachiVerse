using System.Reflection;
using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class MarketSharedCanonicalIndexSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var prototype = Qa04MarketMaterializerV1.CreateMarketState(
            0, Qa04SpatialTileScopeAuthorityV1.ScopeRef, out _);
        var append = typeof(SocietyMarketTransactionPartitionStateV2).GetMethod(
            "WithAdditions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = new SocietyMarketTransactionPartitionStateV2(Array.Empty<SocietyMarketTransactionRecordMaterialV2>());
        var expected = new SortedDictionary<OpaqueId128, SocietyMarketTransactionRecordMaterialV2>();
        SocietyMarketTransactionPartitionStateV2? cut = null;
        for (var batch = 0; batch < 256; batch++)
        {
            var additions = Enumerable.Range(0, 64).Select(i =>
            {
                var ordinal = checked((uint)(batch * 64 + i + 1));
                var id = new OpaqueId128(((UInt128)(ordinal * 2654435761U) << 64) | ordinal);
                return new SocietyMarketTransactionRecordMaterialV2(id, 1, (ulong)batch,
                    null, prototype.DetailLevel, null, prototype.Payload);
            }).Reverse().ToArray();
            var prior = state;
            state = (SocietyMarketTransactionPartitionStateV2)append.Invoke(state, [additions, "test.market-collision"])!;
            foreach (var record in additions) expected.Add(record.RecordId, record);
            Require(state.State.ItemCount == (ulong)expected.Count &&
                state.RecordSet.RecordsCanonical.Count == expected.Count, "Market count drift after AVL additions");
            Require(prior.State.ItemCount == (ulong)(expected.Count - additions.Length), "Prior Market state was mutated");
            RequireSharedIndex(state);
            foreach (var record in additions)
            {
                Require(state.RecordSet.TryGet(record.RecordId, out var material) && ReferenceEquals(material, record),
                    "Material lookup changed identity");
                Require(state.State.TryGet(record.RecordId, out var first) &&
                    state.State.TryGet(record.RecordId, out var second) && ReferenceEquals(first, second) &&
                    ReferenceEquals(first!.Payload, record.Payload), "Envelope lookup must be stable and lossless");
            }
            if (batch == 127) cut = state;
        }
        Require(state.RecordSet.RecordsCanonical.Select(r => r.RecordId).SequenceEqual(expected.Keys),
            "Market canonical order changed");
        var legacy = new DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2>(state.State.Identity,
            expected.Values.Select(r => new DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>(
                r.RecordId, r.RecordSchema, r.Revision, r.CreatedStep, r.RetiredStep,
                r.DetailLevel, r.LineageRef, r.Payload)));
        Require(Header(state.State).CanonicalDigest.SequenceEqual(Header(legacy).CanonicalDigest),
            "Shared Market index changed canonical bytes");
        Require(cut!.State.ItemCount == 8192 && cut.RecordSet.RecordsCanonical.Count == 8192,
            "Live updates changed frozen Market count");
        var cutHeader = Header(cut.State);
        var provider = new SocietyMarketTransactionSnapshotSectionProviderV2();
        var snapshot = provider.Create(new SocietyMarketTransactionSnapshotAuthorityV2(cut, cutHeader));
        provider.CreateSemanticVerifier(cutHeader).Verify(snapshot.Fragments);
        try
        {
            append.Invoke(state, [new[] { expected.Values.First() }, "test.market-collision"]);
            throw new InvalidOperationException("Duplicate Market record was accepted");
        }
        catch (TargetInvocationException failure) when (failure.InnerException is InvalidDataException invalid &&
            invalid.Message == "test.market-collision") { }
        Require(state.State.ItemCount == (ulong)expected.Count, "Rejected append mutated Market state");
        VerifyProjectedUpdates(cut, prototype);
        VerifyDamagedCountIsRejected(append, prototype);
        Console.WriteLine("Market shared canonical index / legacy digest / frozen recovery PASS");
    }

    private static void VerifyProjectedUpdates(SocietyMarketTransactionPartitionStateV2 cut,
        SocietyMarketTransactionRecordMaterialV2 prototype)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var added = new DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>(
            new OpaqueId128(UInt128.MaxValue), prototype.RecordSchema, 1, 256, null,
            prototype.DetailLevel, null, prototype.Payload);
        var type = cut.State.GetType();
        var updated = (DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2>)
            type.GetMethod("WithAdditions", flags)!.Invoke(cut.State, [new[] { added }, "test.projected-collision"])!;
        Require(updated.ItemCount == cut.State.ItemCount + 1 && updated.TryGet(added.RecordId, out var actual) &&
            actual!.Revision == added.Revision && ReferenceEquals(actual.Payload, added.Payload),
            "Projected additions changed material or count");
        var replaced = (DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2>)
            type.GetMethod("WithReplacements", flags)!.Invoke(updated,
                [new[] { added.Revise(added.Payload) }, "test.projected-missing"])!;
        Require(replaced.ItemCount == updated.ItemCount && replaced.TryGet(added.RecordId, out var revised) &&
            revised!.Revision == 2 && updated.TryGet(added.RecordId, out var original) && original!.Revision == 1 &&
            !cut.State.TryGet(added.RecordId, out _), "Projected replacements mutated an older generation");
    }

    private static void VerifyDamagedCountIsRejected(MethodInfo append,
        SocietyMarketTransactionRecordMaterialV2 prototype)
    {
        var basis = new SocietyMarketTransactionPartitionStateV2(new[] { prototype });
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var map = typeof(SocietyMarketTransactionRecordSetV2).GetField("_records", flags)!.GetValue(basis.RecordSet)!;
        var root = map.GetType().GetField("_root", flags)!.GetValue(map)!;
        root.GetType().GetField("<Count>k__BackingField", flags)!.SetValue(root, 2);
        var addition = new SocietyMarketTransactionRecordMaterialV2(new OpaqueId128(UInt128.MaxValue),
            1, 256, null, prototype.DetailLevel, null, prototype.Payload);
        try
        {
            append.Invoke(basis, [new[] { addition }, "test.market-collision"]);
            throw new InvalidOperationException("Corrupt cached count was accepted");
        }
        catch (TargetInvocationException failure) when (failure.InnerException is InvalidDataException invalid &&
            invalid.Message.StartsWith("society.market-transaction-v2.addition-item-count expected=3 actual=2 additions=1",
                StringComparison.Ordinal)) { }
    }

    private static void RequireSharedIndex(SocietyMarketTransactionPartitionStateV2 state)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var source = typeof(SocietyMarketTransactionRecordSetV2).GetField("_records", flags)!.GetValue(state.RecordSet);
        var projected = state.State.GetType().GetField("_records", flags)!.GetValue(state.State)!;
        Require(ReferenceEquals(source, projected.GetType().GetField("_source", flags)?.GetValue(projected)),
            "Market material and envelopes must share one canonical index");
    }

    private static PartitionStateHeaderV1 Header(DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2> state)
    {
        var encode = typeof(PartitionStateHeaderV1).GetMethod("EncodeCanonicalRecord", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(SocietyMarketTransactionRecordPayloadV2))
            .CreateDelegate<Func<DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>, byte[], byte[]>>();
        return RecordIdPrefixPartitionDigestV2.CreateHeader(state, 1, 256, DetailLevelV1.D2RegionalAggregate,
            record => encode(record, SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(record.Payload)));
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
