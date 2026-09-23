using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionDetailTransitionStepV1(
    DetailTransitionPlanV1 Plan,
    DetailConservationValidationV1 Conservation,
    DetailSubstateProjectionV1 Projection,
    int NewRequestCount);

/// <summary>
/// Binds the canonical QA-04 Detail request cadence to the ordinary Detail planner and
/// conservation barrier. This helper deliberately owns no persistence: the resulting plan and
/// substate projection are passed to the Step2 finalization path so the decision history record,
/// State(S+1) Detail authority, and transition COMMIT share the same authoritative boundary.
/// </summary>
public static class Qa04ProductionDetailTransitionV1
{
    public static Qa04ProductionDetailTransitionStepV1 Prepare(
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        DetailDirectoryV1 basisDirectory,
        DetailTransitionPolicyV1 policy)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(basisDirectory);
        ArgumentNullException.ThrowIfNull(policy);

        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId ||
            frozenInput.WorldId != basisState.Header.WorldId ||
            frozenInput.BasisStep != basisState.Header.Step ||
            frozenInput.ConfigGeneration != basisState.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(
                frozenInput.ConfigDigest,
                basisState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("qa04.detail-production.basis-authority-drift");

        var bindings = Qa04CanonicalDetailTransitionBindingV1.BindForStep(
            basisState.Header.Step,
            basisState.Header.ConfigGeneration,
            basisState.Diagnostic.ConfigDigest,
            requirement => basisDirectory.GetRegion(
                Qa04DetailRegionCanonicalAuthorityV1.RegionId(requirement.TileIndex)));

        var admitted = bindings.Count == 0
            ? Array.Empty<DetailTransitionCandidateV1>()
            : AdmitBindings(bindings, frozenInput);
        if (admitted.Length != bindings.Count)
            throw new InvalidDataException("qa04.detail-production.admission-cardinality-drift");

        var plan = DetailTransitionPlannerV1.Plan(
            basisDirectory,
            admitted,
            basisState.Header.Step,
            policy);
        ValidateClassification(basisDirectory, admitted, plan);

        // The #394 production binding changes Detail representation authority only. It does not
        // invent domain semantic quantities or references. The ordinary conservation barrier is
        // still executed and must commit the exact selected transition set; later domain-specific
        // conservation material can be supplied by the owning domain without changing this binding.
        var before = new DetailConservationSnapshotV1();
        var after = new DetailConservationSnapshotV1();
        var conservation = DetailConservationInvariantV1.ValidateForTransitions(
            plan.Selected,
            before,
            after);
        if (!conservation.Decision.CanCommit ||
            conservation.Results.Any(static result => result.Outcome != InvariantOutcomeV1.Pass))
            throw new InvalidDataException("qa04.detail-production.conservation-blocked");

        var projection = DetailDirectorySubstateV1.CreatePostTransitionCandidate(
            basisState,
            basisDirectory,
            plan,
            conservation);
        if (projection.Candidate.Kind != StepCoreSubstateKindV1.Detail ||
            projection.Candidate.BasisStep != basisState.Header.Step ||
            projection.Candidate.TargetStep != checked(basisState.Header.Step + 1UL))
            throw new InvalidDataException("qa04.detail-production.substate-projection-drift");

        return new Qa04ProductionDetailTransitionStepV1(
            plan,
            conservation,
            projection,
            admitted.Length);
    }

    private static DetailTransitionCandidateV1[] AdmitBindings(
        IReadOnlyList<Qa04CanonicalDetailTransitionBindingResultV1> bindings,
        FrozenStepInputV1 frozenInput)
    {
        var triggerAuthority = DetailTransitionTriggerAuthorityV1.FromStep(frozenInput);
        return bindings
            .Select(binding => DetailTransitionAdmissionV1.Admit(binding.Request, triggerAuthority))
            .ToArray();
    }

    private static void ValidateClassification(
        DetailDirectoryV1 basisDirectory,
        IReadOnlyCollection<DetailTransitionCandidateV1> admitted,
        DetailTransitionPlanV1 plan)
    {
        if (plan.BasisStep == 0)
            throw new InvalidDataException("qa04.detail-production.plan-step-zero");

        RequireCanonical(plan.Selected, "selected");
        RequireCanonical(plan.Deferred, "deferred");
        RequireCanonical(plan.NotYetEligible, "not-yet-eligible");

        var classified = plan.Selected
            .Concat(plan.Deferred)
            .Concat(plan.NotYetEligible)
            .ToArray();
        var expectedCount = checked(basisDirectory.PendingTransitions.Count + admitted.Count);
        if (classified.Length != expectedCount)
            throw new InvalidDataException("qa04.detail-production.classification-cardinality-drift");

        var uniqueTargets = classified
            .Select(static candidate => (candidate.DetailRegionId, candidate.DomainToken))
            .Distinct()
            .Count();
        if (uniqueTargets != classified.Length)
            throw new InvalidDataException("qa04.detail-production.classification-target-duplicate");
    }

    private static void RequireCanonical(
        IReadOnlyList<DetailTransitionCandidateV1> candidates,
        string decisionClass)
    {
        var canonical = DetailTransitionCanonicalOrderV1.Order(candidates).ToArray();
        if (!canonical.SequenceEqual(candidates))
            throw new InvalidDataException($"qa04.detail-production.{decisionClass}-noncanonical-order");
    }
}
