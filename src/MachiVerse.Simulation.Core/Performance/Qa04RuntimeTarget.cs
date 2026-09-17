using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04DomainExecutionReceiptV1(
    ulong BasisStep,
    int WorkerCount,
    int FrozenOperationCount,
    IReadOnlyList<DomainCandidateOutputV1> DomainOutputs);

/// <summary>
/// Runtime-owned QA-04 execution boundary. The worker count accepted here is passed directly to
/// DomainRuntimeExecutorV1 and therefore controls real domain execution concurrency rather than
/// report metadata. This surface intentionally stops before candidate merge/finalization; the
/// assembled release adapter must continue through the ordinary authoritative runtime path.
/// </summary>
public sealed class Qa04DomainExecutionTargetV1
{
    private static readonly IReadOnlyList<int> CanonicalWorkerCountsValue = Array.AsReadOnly(new[] { 1, 4, 8, 16 });
    private static readonly byte[] CanonicalWorldSeedDigest = SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes());

    private readonly int _workerCount;
    private readonly StandardDomainExecutionPlanV1 _plan;
    private readonly IReadOnlyCollection<IDomainRuntimeV1> _runtimes;

    public Qa04DomainExecutionTargetV1(
        int workerCount,
        IReadOnlyCollection<IDomainRuntimeV1> runtimes,
        IEnumerable<DomainSameStepDependencyV1>? sameStepDependencies = null)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        if (!CanonicalWorkerCountsValue.Contains(workerCount))
            throw new InvalidDataException("qa04.target.worker-count-not-canonical");

        var plan = StandardDomainExecutionPlanV1.Create(sameStepDependencies);
        var expected = plan.Entries.Select(static entry => entry.DomainToken).ToHashSet();
        var actual = new HashSet<StableToken>();
        foreach (var runtime in runtimes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (!actual.Add(runtime.DomainToken))
                throw new InvalidDataException("qa04.target.duplicate-domain-runtime");
        }
        if (!expected.SetEquals(actual))
            throw new InvalidDataException("qa04.target.domain-runtime-coverage-mismatch");

        _workerCount = workerCount;
        _plan = plan;
        _runtimes = runtimes.ToArray();
    }

    public static IReadOnlyList<int> CanonicalWorkerCounts => CanonicalWorkerCountsValue;
    public int WorkerCount => _workerCount;

    public async Task<Qa04DomainExecutionReceiptV1> ExecuteDomainsAsync(
        WorldStateV1 state,
        OperationSchedulerStateV1 scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        RequireCanonicalWorldIdentity(state);

        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
            _plan,
            state,
            frozen,
            _runtimes,
            _workerCount,
            cancellationToken).ConfigureAwait(false);

        return new Qa04DomainExecutionReceiptV1(
            frozen.BasisStep,
            _workerCount,
            frozen.ScheduledOperations.Count,
            outputs);
    }

    public static void RequireCanonicalWorldIdentity(WorldStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Header.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.target.world-id-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(state.Header.WorldSeedDigest, CanonicalWorldSeedDigest))
            throw new InvalidDataException("qa04.target.world-seed-digest-mismatch");
    }
}
