using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalTransactionKindBindingResultV1(
    Qa04ActiveTransactionDescriptorV1 SourceDescriptor,
    StableToken ProductionKind,
    CrossDomainTransactionCandidateV1 Candidate);

/// <summary>
/// Binds the eleven perf.reference.v1 transaction buckets that have an exact production kind to
/// the ordinary CrossDomainTransactionAssemblerV1 surface.
///
/// This binder deliberately does not decide creation cardinality, the 2% "other registered"
/// allocation, root causality, stable local ordinals, or participant authority. Those values remain
/// explicit inputs or fail closed. The benchmark descriptor TransactionId is source identity only;
/// production candidate identity is re-derived by the production assembler from the production kind
/// and supplied creation inputs.
/// </summary>
public static class Qa04CanonicalTransactionKindBindingV1
{
    private static readonly StableToken OtherRegisteredBucket = new("other-registered-transactions");

    private static readonly IReadOnlyDictionary<StableToken, StableToken> DirectKindMappingsValue =
        Qa04ReferenceScenariosV1.TransactionKinds
            .Where(item => item.KindToken != OtherRegisteredBucket)
            .ToDictionary(
                static item => item.KindToken,
                static item => new StableToken($"transaction.{item.KindToken.Value}"));

    public static IReadOnlyDictionary<StableToken, StableToken> DirectKindMappings => DirectKindMappingsValue;

    public static void ValidateCanonicalContract()
    {
        Qa04TransactionCreationDependencyContractV1.ValidateCanonicalContract();

        if (DirectKindMappingsValue.Count != Qa04TransactionCreationDependencyContractV1.DirectProductionKindMappingCount)
            throw new InvalidDataException("qa04.workload.tx-direct-kind-binding-count-drift");

        foreach (var mapping in DirectKindMappingsValue)
        {
            if (!CrossDomainTransactionKindRegistryV1.Contains(mapping.Value))
                throw new InvalidDataException($"qa04.workload.transaction-kind-unregistered:{mapping.Value.Value}");
            if (mapping.Value.Value != $"transaction.{mapping.Key.Value}")
                throw new InvalidDataException("qa04.workload.tx-direct-kind-binding-drift");
        }

        if (DirectKindMappingsValue.ContainsKey(OtherRegisteredBucket))
            throw new InvalidDataException("qa04.workload.tx-other-kind-must-remain-unbound");
    }

    public static StableToken ResolveProductionKind(StableToken benchmarkKind)
    {
        if (benchmarkKind == OtherRegisteredBucket)
            throw new InvalidDataException("qa04.workload.tx-other-kind-allocation-undefined");
        if (!DirectKindMappingsValue.TryGetValue(benchmarkKind, out var productionKind))
            throw new InvalidDataException($"qa04.workload.tx-benchmark-kind-unregistered:{benchmarkKind.Value}");
        return productionKind;
    }

    public static Qa04CanonicalTransactionKindBindingResultV1 BindKnownDescriptor(
        Qa04ActiveTransactionDescriptorV1 descriptor,
        ulong basisStep,
        CausalityRefV1 rootCausalityRef,
        ulong stableLocalOrdinal,
        IEnumerable<TransactionParticipantCandidateV1> participants)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(rootCausalityRef);
        ArgumentNullException.ThrowIfNull(participants);

        var canonicalDescriptor = Qa04ReferenceScenariosV1.ActiveTransaction(descriptor.Ordinal);
        if (descriptor.KindToken != canonicalDescriptor.KindToken ||
            descriptor.TransactionId != canonicalDescriptor.TransactionId ||
            !descriptor.SubjectIds.SequenceEqual(canonicalDescriptor.SubjectIds))
            throw new InvalidDataException("qa04.workload.tx-source-descriptor-drift");

        var productionKind = ResolveProductionKind(descriptor.KindToken);
        var candidate = CrossDomainTransactionAssemblerV1.Assemble(
            Qa04ReferenceLoadV1.WorldId,
            productionKind,
            basisStep,
            rootCausalityRef,
            descriptor.SubjectIds,
            stableLocalOrdinal,
            participants);
        var canonicalSubjects = descriptor.SubjectIds.Order().ToArray();

        if (candidate.TransactionKind != productionKind ||
            candidate.WorldId != Qa04ReferenceLoadV1.WorldId ||
            candidate.BasisStep != basisStep ||
            !candidate.SubjectRefs.SequenceEqual(canonicalSubjects))
            throw new InvalidDataException("qa04.workload.tx-production-binding-drift");

        return new Qa04CanonicalTransactionKindBindingResultV1(
            descriptor,
            productionKind,
            candidate);
    }
}
