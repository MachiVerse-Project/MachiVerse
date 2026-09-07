using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

internal static class CrossDomainTransactionStepBindingV1
{
    public static void Validate(
        IReadOnlyList<CrossDomainTransactionCandidateV1> transactions,
        IReadOnlyList<MutationIntentCandidateV1> intents,
        IReadOnlyList<ConflictGroupResolutionV1> resolutions,
        IReadOnlyList<PartitionCandidateV1> partitions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(partitions);

        var intentById = intents.ToDictionary(static intent => intent.IntentId);
        var resolutionByIntentId = resolutions
            .SelectMany(static resolution => resolution.Outcomes)
            .ToDictionary(static outcome => outcome.Intent.IntentId);
        var partitionById = partitions.ToDictionary(static partition => partition.PartitionId);
        var validTransactions = transactions.Where(static candidate => candidate.CanFinalize).ToArray();

        foreach (var transaction in validTransactions)
        {
            foreach (var participant in transaction.Participants)
            {
                if (!partitionById.TryGetValue(participant.PartitionId, out var partition))
                    throw new InvalidDataException("step-candidate.transaction-participant-effect-missing");
                if (partition.OwnerDomain != participant.DomainToken)
                    throw new InvalidDataException("step-candidate.transaction-participant-effect-owner-mismatch");
                if (!CryptographicOperations.FixedTimeEquals(partition.CandidateDigest, participant.CandidateEffectDigest))
                    throw new InvalidDataException("step-candidate.transaction-participant-effect-digest-mismatch");

                foreach (var intentId in participant.IntentIds)
                {
                    if (!intentById.TryGetValue(intentId, out var intent))
                        throw new InvalidDataException("step-candidate.transaction-participant-intent-missing");
                    if (intent.TargetDomain != participant.DomainToken || intent.TargetPartitionId != participant.PartitionId)
                        throw new InvalidDataException("step-candidate.transaction-participant-intent-target-mismatch");
                    if (intent.RequiredTransactionKind is { } requiredKind && requiredKind != transaction.TransactionKind)
                        throw new InvalidDataException("step-candidate.transaction-participant-intent-transaction-kind-mismatch");
                    if (!resolutionByIntentId.TryGetValue(intentId, out var outcome))
                        throw new InvalidDataException("step-candidate.transaction-participant-intent-resolution-missing");
                    if (outcome.Disposition != MutationIntentDispositionV1.Effective)
                        throw new InvalidDataException("step-candidate.transaction-participant-intent-not-effective");
                }
            }
        }

        foreach (var outcome in resolutionByIntentId.Values
                     .Where(static outcome => outcome.Disposition == MutationIntentDispositionV1.Effective))
        {
            var requiredKind = outcome.Intent.RequiredTransactionKind;
            if (requiredKind is null)
                continue;

            var coverageCount = validTransactions.Count(transaction =>
                transaction.TransactionKind == requiredKind &&
                transaction.Participants.Any(participant => participant.IntentIds.Contains(outcome.Intent.IntentId)));
            if (coverageCount == 0)
                throw new InvalidDataException("step-candidate.transaction-required-intent-uncovered");
            if (coverageCount != 1)
                throw new InvalidDataException("step-candidate.transaction-required-intent-duplicate-coverage");
        }
    }
}
