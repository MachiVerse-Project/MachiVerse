using MachiVerse.Simulation.Core.Configuration;

namespace MachiVerse.Simulation.Core.Runtime;

public enum OperationLatePolicyV1
{
    Reject = 1,
    DeferWithinGrace = 2,
}

public sealed class OperationSchedulingPolicyV1
{
    public OperationSchedulingPolicyV1(
        ulong ownerConfigGeneration,
        uint minLeadSteps,
        uint? defaultDeadlineWindowSteps,
        uint graceSteps,
        OperationLatePolicyV1 latePolicy)
    {
        if (ownerConfigGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(ownerConfigGeneration), "ConfigGeneration starts at 1.");
        if (!Enum.IsDefined(latePolicy))
            throw new ArgumentOutOfRangeException(nameof(latePolicy));

        OwnerConfigGeneration = ownerConfigGeneration;
        MinLeadSteps = minLeadSteps;
        DefaultDeadlineWindowSteps = defaultDeadlineWindowSteps;
        GraceSteps = graceSteps;
        LatePolicy = latePolicy;
    }

    public ulong OwnerConfigGeneration { get; }
    public uint MinLeadSteps { get; }
    public uint? DefaultDeadlineWindowSteps { get; }
    public uint GraceSteps { get; }
    public OperationLatePolicyV1 LatePolicy { get; }

    public static OperationSchedulingPolicyV1 FromConfig(EffectiveCoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var latePolicy = config.Get<string>("scheduling.late-policy") switch
        {
            "reject" => OperationLatePolicyV1.Reject,
            "defer-within-grace" => OperationLatePolicyV1.DeferWithinGrace,
            _ => throw new InvalidDataException("config.invalid-value:scheduling.late-policy"),
        };

        return new OperationSchedulingPolicyV1(
            config.Generation,
            CheckedUInt(config.Get<long>("scheduling.min-lead-steps"), "scheduling.min-lead-steps"),
            CheckedUInt(config.Get<long>("scheduling.default-deadline-window-steps"), "scheduling.default-deadline-window-steps"),
            CheckedUInt(config.Get<long>("scheduling.grace-steps"), "scheduling.grace-steps"),
            latePolicy);
    }

    private static uint CheckedUInt(long value, string path)
    {
        if (value < 0 || value > uint.MaxValue)
            throw new InvalidDataException($"config.invalid-value:{path}");
        return checked((uint)value);
    }
}

public sealed class OperationSchedulingAdmissionV1
{
    public OperationSchedulingAdmissionV1(
        ulong admissionBasisStep,
        ulong schedulingPolicyGeneration,
        ulong? requestedNotBeforeStep,
        ulong? requestedDeadlineStep)
    {
        if (schedulingPolicyGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(schedulingPolicyGeneration), "ConfigGeneration starts at 1.");
        if (requestedNotBeforeStep is not null && requestedDeadlineStep is not null &&
            requestedDeadlineStep.Value < requestedNotBeforeStep.Value)
            throw new InvalidDataException("request.invalid");

        AdmissionBasisStep = admissionBasisStep;
        SchedulingPolicyGeneration = schedulingPolicyGeneration;
        RequestedNotBeforeStep = requestedNotBeforeStep;
        RequestedDeadlineStep = requestedDeadlineStep;
    }

    public ulong AdmissionBasisStep { get; }
    public ulong SchedulingPolicyGeneration { get; }
    public ulong? RequestedNotBeforeStep { get; }
    public ulong? RequestedDeadlineStep { get; }
}

public sealed record OperationSchedulingBarrierV1(
    ulong NextSchedulableStep,
    bool PauseActive,
    ulong? PauseBasisStep)
{
    public void Validate()
    {
        if (PauseActive && PauseBasisStep is null)
            throw new InvalidDataException("operation.pause-basis-required");
        if (!PauseActive && PauseBasisStep is not null)
            throw new InvalidDataException("operation.pause-basis-unexpected");
    }
}

public enum OperationSchedulingDecisionKindV1
{
    Scheduled = 1,
    TerminalRejected = 2,
}

public sealed record OperationSchedulingDecisionV1(
    OperationSchedulingDecisionKindV1 Kind,
    ulong CanonicalCandidateStep,
    ulong? EffectiveDeadlineStep,
    ulong? GraceLimitStep,
    ulong? EffectiveStep,
    string ResultCode,
    bool WasLate);

public static class OperationSchedulingPlannerV1
{
    public static OperationSchedulingDecisionV1 Plan(
        OperationSchedulingPolicyV1 policy,
        OperationSchedulingAdmissionV1 admission,
        OperationSchedulingBarrierV1 barrier,
        ulong? reportedCandidateStep = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(barrier);
        barrier.Validate();

        if (policy.OwnerConfigGeneration != admission.SchedulingPolicyGeneration)
            throw new InvalidDataException("operation.scheduling-policy-generation-mismatch");

        var canonicalCandidate = AddChecked(admission.AdmissionBasisStep, policy.MinLeadSteps);
        if (admission.RequestedNotBeforeStep is { } notBefore)
            canonicalCandidate = Math.Max(canonicalCandidate, notBefore);

        if (reportedCandidateStep is { } reported && reported != canonicalCandidate)
            throw new InvalidDataException("request.invalid");

        ulong? policyDeadline = policy.DefaultDeadlineWindowSteps is { } deadlineWindow
            ? AddChecked(canonicalCandidate, deadlineWindow)
            : null;
        var effectiveDeadline = MinNullable(policyDeadline, admission.RequestedDeadlineStep);
        ulong? graceLimit = effectiveDeadline is { } deadline
            ? AddChecked(deadline, policy.GraceSteps)
            : null;

        var targetStep = Math.Max(canonicalCandidate, barrier.NextSchedulableStep);
        if (barrier.PauseActive)
        {
            var pauseFloor = AddChecked(barrier.PauseBasisStep!.Value, 1);
            targetStep = Math.Max(targetStep, pauseFloor);
        }

        if (effectiveDeadline is null || targetStep <= effectiveDeadline.Value)
        {
            return new OperationSchedulingDecisionV1(
                OperationSchedulingDecisionKindV1.Scheduled,
                canonicalCandidate,
                effectiveDeadline,
                graceLimit,
                targetStep,
                "operation.scheduled",
                WasLate: false);
        }

        if (policy.LatePolicy == OperationLatePolicyV1.DeferWithinGrace &&
            graceLimit is { } limit && targetStep <= limit)
        {
            return new OperationSchedulingDecisionV1(
                OperationSchedulingDecisionKindV1.Scheduled,
                canonicalCandidate,
                effectiveDeadline,
                graceLimit,
                targetStep,
                "world.late-deferred",
                WasLate: true);
        }

        return new OperationSchedulingDecisionV1(
            OperationSchedulingDecisionKindV1.TerminalRejected,
            canonicalCandidate,
            effectiveDeadline,
            graceLimit,
            EffectiveStep: null,
            "world.deadline-exceeded",
            WasLate: true);
    }

    private static ulong AddChecked(ulong left, uint right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("operation.scheduling-overflow", ex);
        }
    }

    private static ulong? MinNullable(ulong? left, ulong? right)
        => (left, right) switch
        {
            (null, null) => null,
            ({ } value, null) => value,
            (null, { } value) => value,
            ({ } a, { } b) => Math.Min(a, b),
        };
}
