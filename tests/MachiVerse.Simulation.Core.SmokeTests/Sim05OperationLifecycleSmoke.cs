using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim05OperationLifecycleSmoke
{
    internal static void Run()
    {
        var config = new CoreConfigCoordinator().LoadStartup("""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"
""");
        var policy = OperationSchedulingPolicyV1.FromConfig(config);
        Require(policy.OwnerConfigGeneration == 1, "Scheduling policy must carry its owner ConfigGeneration.");
        Require(policy.MinLeadSteps == 2 && policy.DefaultDeadlineWindowSteps == 90 && policy.GraceSteps == 15,
            "Scheduling policy must use the canonical Core Config values.");
        Require(policy.LatePolicy == OperationLatePolicyV1.DeferWithinGrace,
            "Default late policy must be defer-within-grace.");

        var admission = new OperationSchedulingAdmissionV1(
            admissionBasisStep: 100,
            schedulingPolicyGeneration: 1,
            requestedNotBeforeStep: null,
            requestedDeadlineStep: null);

        var onTime = OperationSchedulingPlannerV1.Plan(
            policy,
            admission,
            new OperationSchedulingBarrierV1(NextSchedulableStep: 101, PauseActive: false, PauseBasisStep: null),
            reportedCandidateStep: 102);
        Require(onTime.Kind == OperationSchedulingDecisionKindV1.Scheduled, "On-time Operation must schedule.");
        Require(onTime.CanonicalCandidateStep == 102 && onTime.EffectiveStep == 102,
            "Canonical candidate/effective Step mismatch.");
        Require(onTime.EffectiveDeadlineStep == 192 && onTime.GraceLimitStep == 207,
            "Policy deadline/grace calculation mismatch.");
        Require(onTime.ResultCode == "operation.scheduled" && !onTime.WasLate,
            "On-time scheduling result mismatch.");

        var deferred = OperationSchedulingPlannerV1.Plan(
            policy,
            admission,
            new OperationSchedulingBarrierV1(NextSchedulableStep: 193, PauseActive: false, PauseBasisStep: null));
        Require(deferred.Kind == OperationSchedulingDecisionKindV1.Scheduled && deferred.EffectiveStep == 193,
            "Late Operation inside grace must schedule at the open barrier.");
        Require(deferred.ResultCode == "world.late-deferred" && deferred.WasLate,
            "Late-deferred result code mismatch.");

        var graceExceeded = OperationSchedulingPlannerV1.Plan(
            policy,
            admission,
            new OperationSchedulingBarrierV1(NextSchedulableStep: 208, PauseActive: false, PauseBasisStep: null));
        Require(graceExceeded.Kind == OperationSchedulingDecisionKindV1.TerminalRejected && graceExceeded.EffectiveStep is null,
            "Operation after grace must terminal-reject without an effective Step.");
        Require(graceExceeded.ResultCode == "world.deadline-exceeded",
            "Grace-exceeded result code mismatch.");

        var rejectPolicy = new OperationSchedulingPolicyV1(
            ownerConfigGeneration: 1,
            minLeadSteps: 2,
            defaultDeadlineWindowSteps: 90,
            graceSteps: 15,
            OperationLatePolicyV1.Reject);
        var rejectedLate = OperationSchedulingPlannerV1.Plan(
            rejectPolicy,
            admission,
            new OperationSchedulingBarrierV1(NextSchedulableStep: 193, PauseActive: false, PauseBasisStep: null));
        Require(rejectedLate.Kind == OperationSchedulingDecisionKindV1.TerminalRejected,
            "REJECT late policy must not defer inside grace.");

        var pausePolicy = new OperationSchedulingPolicyV1(
            ownerConfigGeneration: 1,
            minLeadSteps: 0,
            defaultDeadlineWindowSteps: null,
            graceSteps: 0,
            OperationLatePolicyV1.DeferWithinGrace);
        var pauseAdmission = new OperationSchedulingAdmissionV1(100, 1, null, null);
        var pauseDecision = OperationSchedulingPlannerV1.Plan(
            pausePolicy,
            pauseAdmission,
            new OperationSchedulingBarrierV1(NextSchedulableStep: 100, PauseActive: true, PauseBasisStep: 100));
        Require(pauseDecision.EffectiveStep == 101,
            "Pause-time admission must not be appended to the stopped transition Step.");

        var constrainedAdmission = new OperationSchedulingAdmissionV1(100, 1, 120, 150);
        var constrained = OperationSchedulingPlannerV1.Plan(
            policy,
            constrainedAdmission,
            new OperationSchedulingBarrierV1(NextSchedulableStep: 100, PauseActive: false, PauseBasisStep: null));
        Require(constrained.CanonicalCandidateStep == 120 && constrained.EffectiveDeadlineStep == 150,
            "Requested not-before/deadline constraints must tighten canonical scheduling.");

        RequireReject(
            () => _ = OperationSchedulingPlannerV1.Plan(
                policy,
                admission,
                new OperationSchedulingBarrierV1(101, false, null),
                reportedCandidateStep: 103),
            "request.invalid");

        RequireReject(
            () => _ = new OperationSchedulingAdmissionV1(100, 1, 120, 119),
            "request.invalid");

        RequireReject(
            () => _ = OperationSchedulingPlannerV1.Plan(
                new OperationSchedulingPolicyV1(1, 1, null, 0, OperationLatePolicyV1.Reject),
                new OperationSchedulingAdmissionV1(ulong.MaxValue, 1, null, null),
                new OperationSchedulingBarrierV1(ulong.MaxValue, false, null)),
            "operation.scheduling-overflow");

        OperationLifecycleRulesV1.RequireTransition(
            OperationLifecycleStateV1.Unseen,
            OperationLifecycleStateV1.AcceptedDurable);
        OperationLifecycleRulesV1.RequireTransition(
            OperationLifecycleStateV1.AcceptedDurable,
            OperationLifecycleStateV1.ScheduledDurable);
        OperationLifecycleRulesV1.RequireTransition(
            OperationLifecycleStateV1.ScheduledDurable,
            OperationLifecycleStateV1.TerminalDurable);
        OperationLifecycleRulesV1.RequireTransition(
            OperationLifecycleStateV1.Unseen,
            OperationLifecycleStateV1.TerminalDurable,
            CoreOperationResultStatusV1.Rejected);

        RequireReject(
            () => OperationLifecycleRulesV1.RequireTransition(
                OperationLifecycleStateV1.AcceptedDurable,
                OperationLifecycleStateV1.TerminalDurable),
            "operation.lifecycle-transition-invalid:AcceptedDurable:TerminalDurable");
        RequireReject(
            () => OperationLifecycleRulesV1.RequireTransition(
                OperationLifecycleStateV1.Unseen,
                OperationLifecycleStateV1.TerminalDurable,
                CoreOperationResultStatusV1.Failed),
            "operation.lifecycle-transition-invalid:Unseen:TerminalDurable");

        var operationId = OpaqueId128.Parse("00000000000000000000000000000501");
        var digest = Enumerable.Repeat((byte)0x42, 32).ToArray();
        var tombstone = new OperationDedupTombstoneV1(
            operationId,
            digest,
            CoreOperationResultStatusV1.Rejected,
            new StableToken("world.deadline-exceeded"),
            effectiveStep: null,
            terminalHistorySequence: 10);
        Require(tombstone.OperationId == operationId && tombstone.TerminalHistorySequence == 10,
            "Terminal tombstone identity/history boundary mismatch.");

        try
        {
            _ = new OperationDedupTombstoneV1(
                operationId,
                digest,
                CoreOperationResultStatusV1.Accepted,
                new StableToken("operation.accepted"),
                null,
                10);
            throw new InvalidOperationException("Non-terminal status must not create a tombstone.");
        }
        catch (ArgumentException)
        {
        }

        VerifySchedulerState();
    }

    private static void VerifySchedulerState()
    {
        var scopeDigest = SHA256.HashData("sim05-scheduler-scope"u8);
        var firstId = OpaqueId128.Parse("00000000000000000000000000000601");
        var secondId = OpaqueId128.Parse("00000000000000000000000000000602");
        var futureId = OpaqueId128.Parse("00000000000000000000000000000603");
        var staleId = OpaqueId128.Parse("00000000000000000000000000000604");

        SameStepOrderKey Key(OpaqueId128 id, int priority) => new(
            phase: 1,
            domainRank: 50,
            conflictScopeDigest: scopeDigest,
            semanticPriority: priority,
            intentId: id);

        var scheduler = new OperationSchedulerStateV1(nextSchedulableStep: 10, freezeStep: null);
        scheduler.AddDurable(new ScheduledOperationRefV1(secondId, 10, Key(secondId, 5)));
        scheduler.AddDurable(new ScheduledOperationRefV1(firstId, 10, Key(firstId, -5)));
        scheduler.AddDurable(new ScheduledOperationRefV1(futureId, 11, Key(futureId, 0)));

        var step10 = scheduler.ForEffectiveStep(10);
        Require(step10.Count == 2 && step10[0].OperationId == firstId && step10[1].OperationId == secondId,
            "Scheduler bucket must follow canonical SameStepOrderKey order, not insertion order.");

        scheduler.FreezeExternalInput(10);
        Require(scheduler.FreezeStep == 10 && scheduler.NextSchedulableStep == 11,
            "Freezing transition S must advance the next schedulable Step to S+1.");

        RequireReject(
            () => scheduler.AddDurable(new ScheduledOperationRefV1(staleId, 9, Key(staleId, 0))),
            "operation.scheduler-past-effective-step");

        scheduler.OpenAfterFinalization(10);
        Require(scheduler.FreezeStep is null && scheduler.NextSchedulableStep == 11,
            "Finalization must keep the scheduler open at no earlier than S+1.");
        Require(scheduler.ForEffectiveStep(10).Count == 0,
            "Finalized transition bucket must be removed to prevent re-enumeration/double apply.");
        Require(scheduler.ForEffectiveStep(11).Count == 1 && scheduler.ForEffectiveStep(11)[0].OperationId == futureId,
            "Finalization must preserve future scheduled Operations.");

        var recoveryId = OpaqueId128.Parse("00000000000000000000000000000605");
        var recoveryScheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: 20,
            freezeStep: null,
            scheduled:
            [
                new ScheduledOperationRefV1(recoveryId, 20, Key(recoveryId, 0)),
            ]);
        recoveryScheduler.OpenAfterFinalization(20);
        Require(recoveryScheduler.NextSchedulableStep == 21 && recoveryScheduler.ForEffectiveStep(20).Count == 0,
            "Recovery-style finalization without an in-memory freeze must still retire S and advance to S+1.");

        RequireReject(
            () => recoveryScheduler.AddDurable(new ScheduledOperationRefV1(staleId, 20, Key(staleId, 0))),
            "operation.scheduler-past-effective-step");

        var mismatchScheduler = new OperationSchedulerStateV1(30, null);
        mismatchScheduler.FreezeExternalInput(30);
        RequireReject(
            () => mismatchScheduler.OpenAfterFinalization(31),
            "operation.scheduler-finalized-step-mismatch");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }

        throw new InvalidOperationException($"Expected SIM-05 rejection: {expectedMessage}");
    }
}
