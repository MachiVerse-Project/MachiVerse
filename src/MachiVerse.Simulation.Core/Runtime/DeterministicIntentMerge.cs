using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed class ConflictScopeV1
{
    public ConflictScopeV1(
        StableToken domain,
        StableToken targetKind,
        ReadOnlySpan<byte> targetId,
        StableToken resource,
        ReadOnlySpan<byte> subkey = default,
        bool hasSubkey = false)
    {
        if (targetId is { Length: < 1 or > 64 })
            throw new ArgumentException("ConflictScope target_id must contain 1..64 bytes.", nameof(targetId));
        if (hasSubkey && subkey.Length > 128)
            throw new ArgumentException("ConflictScope subkey must contain at most 128 bytes.", nameof(subkey));

        Domain = domain;
        TargetKind = targetKind;
        TargetId = targetId.ToArray();
        Resource = resource;
        Subkey = hasSubkey ? subkey.ToArray() : null;
        Digest = ComputeDigest();
    }

    public StableToken Domain { get; }
    public StableToken TargetKind { get; }
    public byte[] TargetId { get; }
    public StableToken Resource { get; }
    public byte[]? Subkey { get; }
    public byte[] Digest { get; }

    public bool SemanticEquals(ConflictScopeV1 other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Domain == other.Domain &&
               TargetKind == other.TargetKind &&
               TargetId.AsSpan().SequenceEqual(other.TargetId) &&
               Resource == other.Resource &&
               ((Subkey is null && other.Subkey is null) ||
                (Subkey is not null && other.Subkey is not null && Subkey.AsSpan().SequenceEqual(other.Subkey)));
    }

    private byte[] ComputeDigest()
        => HashSuite.DomainHash("mv.scope.v1", writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(Domain.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(TargetKind.Value);
            writer.WriteUnsigned(2); writer.WriteBytes(TargetId);
            writer.WriteUnsigned(3); writer.WriteAsciiText(Resource.Value);
            writer.WriteUnsigned(4);
            if (Subkey is null)
            {
                writer.WriteArrayStart(0);
            }
            else
            {
                writer.WriteArrayStart(1);
                writer.WriteBytes(Subkey);
            }
        });
}

public enum ConflictResolutionModeV1
{
    ExclusiveFirstValid = 1,
    Sequential = 2,
    SetMerge = 3,
    DeterministicReduce = 4,
    CustomDeterministic = 5,
}

public sealed class MutationIntentCandidateV1
{
    public MutationIntentCandidateV1(
        OpaqueId128 intentId,
        byte phase,
        StableToken sourceDomain,
        StableToken targetDomain,
        StableToken targetPartitionId,
        ulong basisStep,
        StableToken mutationKind,
        ConflictScopeV1 targetScope,
        int semanticPriority,
        ConflictResolutionModeV1 resolutionMode,
        ReadOnlySpan<byte> semanticPayloadDigest,
        StableToken? requiredTransactionKind = null)
    {
        if (intentId.IsZero) throw new ArgumentException("IntentId ZERO is invalid.", nameof(intentId));
        if (phase > 5) throw new ArgumentOutOfRangeException(nameof(phase), "Standard OrderPhase is 0..5.");
        if (!Enum.IsDefined(resolutionMode)) throw new ArgumentOutOfRangeException(nameof(resolutionMode));
        ArgumentNullException.ThrowIfNull(targetScope);
        if (semanticPayloadDigest.Length != 32)
            throw new ArgumentException("Intent semantic payload digest must be 32 bytes.", nameof(semanticPayloadDigest));
        if (requiredTransactionKind is { } requiredKind &&
            !CrossDomainTransactionKindRegistryV1.Contains(requiredKind))
            throw new InvalidDataException("intent.required-transaction-kind-unregistered");

        var targetPartition = StandardDomainPartitionRegistry.Get(targetPartitionId.Value);
        if (targetPartition.OwnerDomain != targetDomain)
            throw new InvalidDataException("intent.target-owner-mismatch");
        if (targetScope.Domain != targetDomain)
            throw new InvalidDataException("intent.conflict-scope-domain-mismatch");

        var sourceEntries = StandardDomainPartitionRegistry.Entries
            .Where(entry => entry.OwnerDomain == sourceDomain)
            .ToArray();
        if (sourceEntries.Length == 0)
            throw new InvalidDataException("intent.source-domain-unregistered");
        var domainRank = sourceEntries[0].OwnerDomainRank;
        if (sourceEntries.Any(entry => entry.OwnerDomainRank != domainRank))
            throw new InvalidDataException("intent.source-domain-rank-inconsistent");

        IntentId = intentId;
        Phase = phase;
        SourceDomain = sourceDomain;
        TargetDomain = targetDomain;
        TargetPartitionId = targetPartitionId;
        BasisStep = basisStep;
        MutationKind = mutationKind;
        TargetScope = targetScope;
        SemanticPriority = semanticPriority;
        ResolutionMode = resolutionMode;
        SemanticPayloadDigest = semanticPayloadDigest.ToArray();
        RequiredTransactionKind = requiredTransactionKind;
        OrderKey = new SameStepOrderKey(phase, domainRank, targetScope.Digest, semanticPriority, intentId);
    }

    public OpaqueId128 IntentId { get; }
    public byte Phase { get; }
    public StableToken SourceDomain { get; }
    public StableToken TargetDomain { get; }
    public StableToken TargetPartitionId { get; }
    public ulong BasisStep { get; }
    public StableToken MutationKind { get; }
    public ConflictScopeV1 TargetScope { get; }
    public int SemanticPriority { get; }
    public ConflictResolutionModeV1 ResolutionMode { get; }
    public byte[] SemanticPayloadDigest { get; }
    public StableToken? RequiredTransactionKind { get; }
    public SameStepOrderKey OrderKey { get; }
}

public sealed class CanonicalConflictGroupV1
{
    internal CanonicalConflictGroupV1(ConflictScopeV1 scope, IReadOnlyList<MutationIntentCandidateV1> orderedCandidates)
    {
        if (orderedCandidates.Count == 0)
            throw new ArgumentException("Conflict group cannot be empty.", nameof(orderedCandidates));
        if (orderedCandidates.Any(candidate => !scope.SemanticEquals(candidate.TargetScope)))
            throw new InvalidDataException("determinism.conflict-scope-mismatch");
        if (orderedCandidates.Select(static candidate => candidate.ResolutionMode).Distinct().Count() != 1)
            throw new InvalidDataException("determinism.conflict-mode-mismatch");

        Scope = scope;
        OrderedCandidates = orderedCandidates;
        ResolutionMode = orderedCandidates[0].ResolutionMode;
    }

    public ConflictScopeV1 Scope { get; }
    public ConflictResolutionModeV1 ResolutionMode { get; }
    public IReadOnlyList<MutationIntentCandidateV1> OrderedCandidates { get; }
}

public enum MutationIntentDispositionV1
{
    Effective = 1,
    ConflictLost = 2,
    PreconditionFailed = 3,
    NormalizedDuplicate = 4,
}

public sealed record ResolvedMutationIntentV1(
    MutationIntentCandidateV1 Intent,
    MutationIntentDispositionV1 Disposition);

public sealed class ConflictGroupResolutionV1
{
    public ConflictGroupResolutionV1(
        CanonicalConflictGroupV1 group,
        IEnumerable<ResolvedMutationIntentV1> outcomes,
        byte[]? aggregateDigest = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (aggregateDigest is not null && aggregateDigest.Length != 32)
            throw new ArgumentException("Aggregate digest must be 32 bytes.", nameof(aggregateDigest));

        var materialized = outcomes.ToArray();
        if (materialized.Length != group.OrderedCandidates.Count)
            throw new InvalidDataException("determinism.conflict-resolution-coverage-mismatch");
        for (var index = 0; index < materialized.Length; index++)
        {
            if (!ReferenceEquals(materialized[index].Intent, group.OrderedCandidates[index]))
                throw new InvalidDataException("determinism.conflict-resolution-order-mismatch");
            if (!Enum.IsDefined(materialized[index].Disposition))
                throw new InvalidDataException("determinism.conflict-resolution-disposition-invalid");
        }

        Group = group;
        Outcomes = Array.AsReadOnly(materialized);
        AggregateDigest = aggregateDigest?.ToArray();
    }

    public CanonicalConflictGroupV1 Group { get; }
    public IReadOnlyList<ResolvedMutationIntentV1> Outcomes { get; }
    public byte[]? AggregateDigest { get; }
}

public static class DeterministicIntentMergerV1
{
    public static IReadOnlyList<MutationIntentCandidateV1> CanonicalOrder(
        IEnumerable<MutationIntentCandidateV1> intents,
        ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(intents);
        var ordered = intents.OrderBy(static intent => intent.OrderKey).ToArray();
        if (ordered.Select(static intent => intent.IntentId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("determinism.duplicate-intent-id");
        if (ordered.Any(intent => intent.BasisStep != basisStep))
            throw new InvalidDataException("determinism.intent-basis-step-mismatch");
        return Array.AsReadOnly(ordered);
    }

    public static IReadOnlyList<CanonicalConflictGroupV1> GroupByConflictScope(
        IEnumerable<MutationIntentCandidateV1> intents,
        ulong basisStep)
    {
        var ordered = CanonicalOrder(intents, basisStep);
        var groups = new List<CanonicalConflictGroupV1>();
        foreach (var digestGroup in ordered.GroupBy(
                     static intent => Convert.ToHexString(intent.TargetScope.Digest),
                     StringComparer.Ordinal))
        {
            var candidates = digestGroup.ToArray();
            var scope = candidates[0].TargetScope;
            foreach (var candidate in candidates.Skip(1))
            {
                if (!scope.SemanticEquals(candidate.TargetScope))
                    throw new InvalidDataException("determinism.conflict-scope-digest-collision");
            }
            groups.Add(new CanonicalConflictGroupV1(scope, Array.AsReadOnly(candidates)));
        }

        return groups
            .OrderBy(static group => group.Scope.Digest, ByteArrayLexicographicComparer.Instance)
            .ToArray();
    }

    public static ConflictGroupResolutionV1 ResolveExclusiveFirstValid(
        CanonicalConflictGroupV1 group,
        Func<MutationIntentCandidateV1, bool> precondition)
    {
        RequireMode(group, ConflictResolutionModeV1.ExclusiveFirstValid);
        ArgumentNullException.ThrowIfNull(precondition);
        var winnerFound = false;
        var outcomes = new ResolvedMutationIntentV1[group.OrderedCandidates.Count];
        for (var index = 0; index < group.OrderedCandidates.Count; index++)
        {
            var candidate = group.OrderedCandidates[index];
            if (winnerFound)
            {
                outcomes[index] = new ResolvedMutationIntentV1(candidate, MutationIntentDispositionV1.ConflictLost);
                continue;
            }

            if (precondition(candidate))
            {
                winnerFound = true;
                outcomes[index] = new ResolvedMutationIntentV1(candidate, MutationIntentDispositionV1.Effective);
            }
            else
            {
                outcomes[index] = new ResolvedMutationIntentV1(candidate, MutationIntentDispositionV1.PreconditionFailed);
            }
        }
        return new ConflictGroupResolutionV1(group, outcomes);
    }

    public static ConflictGroupResolutionV1 ResolveSequential(CanonicalConflictGroupV1 group)
    {
        RequireMode(group, ConflictResolutionModeV1.Sequential);
        return AllEffective(group);
    }

    public static ConflictGroupResolutionV1 ResolveSetMerge(
        CanonicalConflictGroupV1 group,
        Func<MutationIntentCandidateV1, ReadOnlyMemory<byte>> identitySelector)
    {
        RequireMode(group, ConflictResolutionModeV1.SetMerge);
        ArgumentNullException.ThrowIfNull(identitySelector);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outcomes = group.OrderedCandidates.Select(candidate =>
        {
            var identity = identitySelector(candidate);
            if (identity.IsEmpty)
                throw new InvalidDataException("determinism.set-merge-identity-empty");
            var key = Convert.ToHexString(identity.Span);
            return new ResolvedMutationIntentV1(
                candidate,
                seen.Add(key) ? MutationIntentDispositionV1.Effective : MutationIntentDispositionV1.NormalizedDuplicate);
        }).ToArray();
        return new ConflictGroupResolutionV1(group, outcomes);
    }

    public static ConflictGroupResolutionV1 ResolveDeterministicReduce(
        CanonicalConflictGroupV1 group,
        Func<IReadOnlyList<MutationIntentCandidateV1>, byte[]> canonicalReducer)
    {
        RequireMode(group, ConflictResolutionModeV1.DeterministicReduce);
        ArgumentNullException.ThrowIfNull(canonicalReducer);
        var digest = canonicalReducer(group.OrderedCandidates)
            ?? throw new InvalidDataException("determinism.reduce-digest-null");
        if (digest.Length != 32)
            throw new InvalidDataException("determinism.reduce-digest-length");
        return new ConflictGroupResolutionV1(
            group,
            group.OrderedCandidates.Select(static candidate =>
                new ResolvedMutationIntentV1(candidate, MutationIntentDispositionV1.Effective)),
            digest);
    }

    public static ConflictGroupResolutionV1 ResolveCustomDeterministic(
        CanonicalConflictGroupV1 group,
        Func<CanonicalConflictGroupV1, ConflictGroupResolutionV1> resolver)
    {
        RequireMode(group, ConflictResolutionModeV1.CustomDeterministic);
        ArgumentNullException.ThrowIfNull(resolver);
        var result = resolver(group) ?? throw new InvalidDataException("determinism.custom-resolver-null");
        if (!ReferenceEquals(result.Group, group))
            throw new InvalidDataException("determinism.custom-resolver-group-mismatch");
        return result;
    }

    private static ConflictGroupResolutionV1 AllEffective(CanonicalConflictGroupV1 group)
        => new(
            group,
            group.OrderedCandidates.Select(static candidate =>
                new ResolvedMutationIntentV1(candidate, MutationIntentDispositionV1.Effective)));

    private static void RequireMode(CanonicalConflictGroupV1 group, ConflictResolutionModeV1 expected)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.ResolutionMode != expected)
            throw new InvalidDataException($"determinism.conflict-mode-expected:{expected}");
    }

    private sealed class ByteArrayLexicographicComparer : IComparer<byte[]>
    {
        public static ByteArrayLexicographicComparer Instance { get; } = new();
        public int Compare(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return left.AsSpan().SequenceCompareTo(right);
        }
    }
}
