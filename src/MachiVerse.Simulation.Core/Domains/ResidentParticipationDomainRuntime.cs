using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public sealed class ResidentDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public ResidentDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("resident", intentEvaluator, partitionCandidateEvaluator)
    {
    }
}

public sealed class ParticipationDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public ParticipationDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("participation", intentEvaluator, partitionCandidateEvaluator)
    {
    }
}

public static class ResidentParticipationPartitionCandidateFactoryV1
{
    private static readonly StableToken ResidentOwner = new("resident");
    private static readonly StableToken ParticipationOwner = new("participation");

    public static PartitionCandidateV1 CreateResident(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, ResidentOwner, partitionId, changeSetDigest);

    public static PartitionCandidateV1 CreateParticipation(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, ParticipationOwner, partitionId, changeSetDigest);

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

public static class ResidentParticipationRuntimeGateV1
{
    public static async Task<IReadOnlyList<string>> ExecuteSemanticSnapshotAsync(
        WorldStateV1 state,
        FrozenStepInputV1 frozen,
        MutationIntentCandidateV1 residentIntent,
        int workerCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(residentIntent);
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));

        var plan = StandardDomainExecutionPlanV1.Create();
        var runtimes = plan.Entries.Select(entry => entry.DomainToken.Value switch
        {
            "resident" => (IDomainRuntimeV1)new ResidentDomainRuntimeV1(
                (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([residentIntent]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                    ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                        context.State,
                        "resident.goal_plan",
                        System.Security.Cryptography.SHA256.HashData("sim09-resident-goal"u8))
                ])),
            "participation" => new ParticipationDomainRuntimeV1(
                static (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                    ResidentParticipationPartitionCandidateFactoryV1.CreateParticipation(
                        context.State,
                        "participation.binding",
                        System.Security.Cryptography.SHA256.HashData("sim09-participation-binding"u8))
                ])),
            _ => new NoOpRuntime(entry.DomainToken),
        }).ToArray();

        var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
            plan,
            state,
            frozen,
            runtimes,
            workerCount,
            cancellationToken);

        return Array.AsReadOnly(outputs.Select(output =>
            output.DomainToken.Value + ":" +
            string.Join(',', output.Intents.Select(static intent =>
                intent.MutationKind.Value + "->" + intent.TargetPartitionId.Value + "=" + Convert.ToHexString(intent.SemanticPayloadDigest))) + ":" +
            string.Join(',', output.LocalPartitionCandidates.Select(static candidate =>
                candidate.PartitionId.Value + "=" + Convert.ToHexString(candidate.CandidateDigest))))
            .ToArray());
    }

    private sealed class NoOpRuntime(StableToken domainToken) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep));
        }
    }
}
