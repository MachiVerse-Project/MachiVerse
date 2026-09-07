using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public enum ConflictResolutionModeV1
{
    ExclusiveFirstValid = 1,
    Sequential = 2,
    SetMerge = 3,
    DeterministicReduce = 4,
    CustomDeterministic = 5,
}

public sealed class ConflictScopeV1
{
    public ConflictScopeV1(
        StableToken domain,
        StableToken targetKind,
        byte[] targetId,
        StableToken resource,
        byte[]? subkey = null)
    {
        if (string.IsNullOrEmpty(domain.Value)) throw new ArgumentException("Domain is required.", nameof(domain));
        if (string.IsNullOrEmpty(targetKind.Value)) throw new ArgumentException("Target kind is required.", nameof(targetKind));
        if (string.IsNullOrEmpty(resource.Value)) throw new ArgumentException("Resource is required.", nameof(resource));
        ArgumentNullException.ThrowIfNull(targetId);
        if (targetId.Length is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(targetId), "ConflictScope target_id must be 1..64 bytes.");
        if (subkey is { Length: > 128 })
            throw new ArgumentOutOfRangeException(nameof(subkey), "ConflictScope subkey must be at most 128 bytes.");

        Domain = domain;
        TargetKind = targetKind;
        TargetId = targetId.ToArray();
        Resource = resource;
        Subkey = subkey?.ToArray();
        CanonicalDigest = ComputeDigest();
    }

    public StableToken Domain { get; }
    public StableToken TargetKind { get; }
    public byte[] TargetId { get; }
    public StableToken Resource { get; }
    public byte[]? Subkey { get; }
    public byte[] CanonicalDigest { get; }

    public bool SemanticallyEquals(ConflictScopeV1 other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Domain == other.Domain &&
               TargetKind == other.TargetKind &&
               Resource == other.Resource &&
               TargetId.AsSpan().SequenceEqual(other.TargetId) &&
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

public sealed class MutationIntentEnvelopeV1
{
    public MutationIntentEnvelopeV1(
        OpaqueId128 intentId,
        ulong basisStep,
        StableToken sourceDomain,
        StableToken targetDomain,
        StableToken mutationKind,
        ConflictScopeV1 targetScope,
        SameStepOrderKey orderKey,
        byte[] payloadDigest)
    {
        if (intentId.IsZero) throw new ArgumentException("IntentId ZERO is invalid.", nameof(intentId));
        if (string.IsNullOrEmpty(sourceDomain.Value)) throw new ArgumentException("Source domain is required.", nameof(sourceDomain));
        if (string.IsNullOrEmpty(targetDomain.Value)) throw new ArgumentException("Target domain is required.", nameof(targetDomain));
        if (string.IsNullOrEmpty(mutationKind.Value)) throw new ArgumentException("Mutation kind is required.", nameof(mutationKind));
        ArgumentNullException.ThrowIfNull(targetScope);
        ArgumentNullException.ThrowIfNull(orderKey);
        ArgumentNullException.ThrowIfNull(payloadDigest);
        if (payloadDigest.Length != 32)
            throw new ArgumentException("Mutation payload digest must be exactly 32 bytes.", nameof(payloadDigest));
        if (targetScope.Domain != targetDomain)
            throw new InvalidDataException("mutation.target-scope-domain-mismatch");
        if (orderKey.IntentId != intentId)
            throw new InvalidDataException("mutation.order-key-intent-id-mismatch");
        if (!orderKey.ConflictScopeDigest.SequenceEqual(targetScope.CanonicalDigest))
            throw new InvalidDataException("mutation.conflict-scope-digest-mismatch");

        IntentId = intentId;
        BasisStep = basisStep;
        SourceDomain = sourceDomain;
        TargetDomain = targetDomain;
        MutationKind = mutationKind;
        TargetScope = targetScope;
        OrderKey = orderKey;
        PayloadDigest = payloadDigest.ToArray();
    }

    public OpaqueId128 IntentId { get; }
    public ulong BasisStep { get; }
    public StableToken SourceDomain { get; }
    public StableToken TargetDomain { get; }
    public StableToken MutationKind { get; }
    public ConflictScopeV1 TargetScope { get; }
    public SameStepOrderKey OrderKey { get; }
    public byte[] PayloadDigest { get; }
}

public sealed record MutationKindRegistrationV1(
    StableToken MutationKind,
    StableToken TargetDomain,
    ConflictResolutionModeV1 ConflictMode)
{
    public void Validate()
    {
        if (string.IsNullOrEmpty(MutationKind.Value))
            throw new InvalidDataException("mutation.registration-kind-invalid");
        if (string.IsNullOrEmpty(TargetDomain.Value))
            throw new InvalidDataException("mutation.registration-target-domain-invalid");
        if (!Enum.IsDefined(ConflictMode))
            throw new InvalidDataException("mutation.registration-conflict-mode-invalid");
    }
}

public sealed record CanonicalConflictGroupV1(
    ConflictScopeV1 Scope,
    ConflictResolutionModeV1 Mode,
    IReadOnlyList<MutationIntentEnvelopeV1> OrderedCandidates);

public sealed class CanonicalIntentMergeResultV1
{
    public CanonicalIntentMergeResultV1(
        IReadOnlyList<MutationIntentEnvelopeV1> orderedIntents,
        IReadOnlyList<CanonicalConflictGroupV1> conflictGroups)
    {
        OrderedIntents = orderedIntents;
        ConflictGroups = conflictGroups;
    }

    public IReadOnlyList<MutationIntentEnvelopeV1> OrderedIntents { get; }
    public IReadOnlyList<CanonicalConflictGroupV1> ConflictGroups { get; }
}

public static class CanonicalIntentMergerV1
{
    public static CanonicalIntentMergeResultV1 Build(
        FrozenStepInputV1 frozenInput,
        IEnumerable<MutationIntentEnvelopeV1> intents,
        IEnumerable<MutationKindRegistrationV1> registrations)
    {
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(registrations);

        var registry = new Dictionary<string, MutationKindRegistrationV1>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            registration.Validate();
            if (!registry.TryAdd(registration.MutationKind.Value, registration))
                throw new InvalidDataException("mutation.registration-duplicate-kind");
        }

        var ordered = intents.OrderBy(static intent => intent.OrderKey, SameStepOrderKeyComparer.Instance).ToArray();
        var ids = new HashSet<OpaqueId128>();
        foreach (var intent in ordered)
        {
            ArgumentNullException.ThrowIfNull(intent);
            if (intent.BasisStep != frozenInput.BasisStep)
                throw new InvalidDataException("mutation.basis-step-mismatch");
            if (!ids.Add(intent.IntentId))
                throw new InvalidDataException("mutation.intent-id-duplicate");
            if (!registry.TryGetValue(intent.MutationKind.Value, out var registration))
                throw new InvalidDataException($"mutation.kind-unregistered:{intent.MutationKind.Value}");
            if (registration.TargetDomain != intent.TargetDomain)
                throw new InvalidDataException("mutation.target-owner-mismatch");
        }

        var grouped = new Dictionary<string, List<MutationIntentEnvelopeV1>>(StringComparer.Ordinal);
        foreach (var intent in ordered)
        {
            var key = Convert.ToHexString(intent.TargetScope.CanonicalDigest);
            if (!grouped.TryGetValue(key, out var bucket))
            {
                bucket = [];
                grouped.Add(key, bucket);
            }
            else if (!bucket[0].TargetScope.SemanticallyEquals(intent.TargetScope))
            {
                throw new InvalidDataException("mutation.conflict-scope-digest-collision");
            }
            bucket.Add(intent);
        }

        var groups = new List<CanonicalConflictGroupV1>(grouped.Count);
        foreach (var bucket in grouped.Values)
        {
            var firstRegistration = registry[bucket[0].MutationKind.Value];
            foreach (var candidate in bucket)
            {
                if (registry[candidate.MutationKind.Value].ConflictMode != firstRegistration.ConflictMode)
                    throw new InvalidDataException("mutation.conflict-mode-mismatch");
            }
            groups.Add(new CanonicalConflictGroupV1(
                bucket[0].TargetScope,
                firstRegistration.ConflictMode,
                Array.AsReadOnly(bucket.ToArray())));
        }

        groups.Sort(static (left, right) =>
            left.Scope.CanonicalDigest.AsSpan().SequenceCompareTo(right.Scope.CanonicalDigest));

        return new CanonicalIntentMergeResultV1(
            Array.AsReadOnly(ordered),
            Array.AsReadOnly(groups.ToArray()));
    }

    public static ExclusiveFirstValidResolutionV1 ResolveExclusiveFirstValid(
        CanonicalConflictGroupV1 group,
        Func<MutationIntentEnvelopeV1, bool> precondition)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(precondition);
        if (group.Mode != ConflictResolutionModeV1.ExclusiveFirstValid)
            throw new InvalidOperationException("Conflict group is not exclusive_first_valid.");

        MutationIntentEnvelopeV1? winner = null;
        var rejected = new List<OpaqueId128>();
        foreach (var candidate in group.OrderedCandidates)
        {
            if (winner is null && precondition(candidate))
            {
                winner = candidate;
                continue;
            }
            rejected.Add(candidate.IntentId);
        }

        return new ExclusiveFirstValidResolutionV1(
            winner?.IntentId,
            Array.AsReadOnly(rejected.ToArray()));
    }

    private sealed class SameStepOrderKeyComparer : IComparer<SameStepOrderKey>
    {
        public static SameStepOrderKeyComparer Instance { get; } = new();
        public int Compare(SameStepOrderKey? left, SameStepOrderKey? right)
            => left?.CompareTo(right) ?? (right is null ? 0 : -1);
    }
}

public sealed record ExclusiveFirstValidResolutionV1(
    OpaqueId128? WinnerIntentId,
    IReadOnlyList<OpaqueId128> RejectedIntentIds);
