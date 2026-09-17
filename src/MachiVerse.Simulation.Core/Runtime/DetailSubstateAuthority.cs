using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed record DetailSubstateProjectionV1(
    DetailDirectoryV1 ResultingDirectory,
    StepCoreSubstateCandidateV1 Candidate);

/// <summary>
/// Canonical WorldState binding for the authoritative DetailDirectory.
/// Planner source binding and conservation validation remain owned by the existing detail runtime;
/// this adapter only projects the validated result into the candidate-bound core.detail-state ref.
/// </summary>
public static class DetailDirectorySubstateV1
{
    private static readonly SchemaRefV1 Schema = new("core.detail-state");

    public static WorldSubstateRefV1 Canonicalize(DetailDirectoryV1 directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (directory.Regions.Count == 0 && directory.PendingTransitions.Count == 0)
            return WorldStateV1.EmptySubstate(Schema.SchemaId.Value);

        var authorityDigest = directory.ComputeAuthorityDigest();
        var digest = HashSuite.DomainHash("mv.core-detail-state.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteAsciiText(Schema.SchemaId.Value);
            writer.WriteUnsigned(1); writer.WriteBytes(authorityDigest);
        });
        return new WorldSubstateRefV1(Schema, digest);
    }

    public static DetailSubstateProjectionV1 CreatePostTransitionCandidate(
        WorldStateV1 basisState,
        DetailDirectoryV1 basisDirectory,
        DetailTransitionPlanV1 plan,
        DetailConservationValidationV1 conservationValidation)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(basisDirectory);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(conservationValidation);
        if (plan.BasisStep != basisState.Header.Step)
            throw new InvalidDataException("detail-substate.plan-basis-step-mismatch");

        var canonicalBasis = Canonicalize(basisDirectory);
        if (canonicalBasis.Schema != basisState.DetailState.Schema ||
            !CryptographicOperations.FixedTimeEquals(
                canonicalBasis.CanonicalDigest,
                basisState.DetailState.CanonicalDigest))
            throw new InvalidDataException("detail-substate.basis-directory-mismatch");

        var resultingDirectory = basisDirectory.Apply(plan, conservationValidation);
        var candidate = new StepCoreSubstateCandidateV1(
            StepCoreSubstateKindV1.Detail,
            basisState.Header.Step,
            basisState.DetailState,
            Canonicalize(resultingDirectory));
        return new DetailSubstateProjectionV1(resultingDirectory, candidate);
    }
}
