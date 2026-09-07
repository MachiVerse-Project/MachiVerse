using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public delegate ValueTask<IReadOnlyList<MutationIntentCandidateV1>> DomainIntentEvaluatorV1(
    DomainRuntimeContextV1 context,
    CancellationToken cancellationToken);

public abstract class DeterministicDomainRuntimeV1 : IDomainRuntimeV1
{
    private readonly DomainIntentEvaluatorV1 _evaluator;

    protected DeterministicDomainRuntimeV1(string domainToken, DomainIntentEvaluatorV1 evaluator)
    {
        DomainToken = new StableToken(domainToken);
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public StableToken DomainToken { get; }

    public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
        DomainRuntimeContextV1 context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.PlanEntry.DomainToken != DomainToken)
            throw new InvalidDataException("domain-runtime.owner-mismatch");
        if (context.FrozenInput.BasisStep != context.State.Header.Step)
            throw new InvalidDataException("domain-runtime.basis-step-mismatch");

        var intents = await _evaluator(context, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("domain-runtime.intent-evaluator-null");
        if (intents.Any(intent => intent.SourceDomain != DomainToken || intent.BasisStep != context.FrozenInput.BasisStep))
            throw new InvalidDataException("domain-runtime.intent-source-mismatch");
        return new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep, intents);
    }
}

public sealed class SpatialDomainRuntimeV1(DomainIntentEvaluatorV1 evaluator)
    : DeterministicDomainRuntimeV1("spatial", evaluator);

public sealed class EnvironmentDomainRuntimeV1(DomainIntentEvaluatorV1 evaluator)
    : DeterministicDomainRuntimeV1("environment", evaluator);

public static class DomainOwnedPartitionCandidateFactoryV1
{
    public static PartitionCandidateV1 CreateSpatial(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, new StableToken("spatial"), partitionId, changeSetDigest);

    public static PartitionCandidateV1 CreateEnvironment(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, new StableToken("environment"), partitionId, changeSetDigest);

    private static PartitionCandidateV1 Create(
        WorldStateV1 state,
        StableToken ownerDomain,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
    {
        ArgumentNullException.ThrowIfNull(state);
        var partitionToken = new StableToken(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionToken.Value);
        if (identity.OwnerDomain != ownerDomain)
            throw new InvalidDataException("domain.partition-candidate-foreign-owner");

        var basis = state.Partitions.Get(partitionToken.Value).Header;
        if (basis.BasisStep > state.Header.Step)
            throw new InvalidDataException("domain.partition-candidate-basis-ahead");
        return new PartitionCandidateV1(
            partitionToken,
            ownerDomain,
            basis.Revision,
            state.Header.Step,
            changeSetDigest);
    }
}
