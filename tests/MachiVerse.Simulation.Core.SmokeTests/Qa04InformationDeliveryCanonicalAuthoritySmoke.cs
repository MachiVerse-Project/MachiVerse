using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InformationDeliveryCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04InformationDeliveryCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04InformationDeliveryCanonicalAuthorityV1.MaterializeCanonical();
        var records = materialization.RecordsByOrdinal;

        Require(materialization.MaterializedRecordCount == 20_000 &&
                materialization.Partition.ItemCount == 20_000 &&
                records.Count == 20_000 &&
                materialization.RuntimeDeliveries.Count == 20_000,
            "Canonical information.delivery authority must materialize exactly 20,000 records.");

        Require(records.Select(static record => record.RecordId).Distinct().Count() == 20_000 &&
                records.Select(static record => record.Payload.ContentRef).Distinct().Count() == 20_000,
            "Canonical Delivery and InformationClaim identities must both be one-to-one across the 20,000 package.");

        var claims = materialization.SocietyAuthority.InformationClaims.RecordsCanonical
            .ToDictionary(static record => record.RecordId);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            Require(record.Payload.ContentRef.PartitionId.Value == SocietyInformationClaimPayloadV1.PartitionId,
                "Every canonical Delivery content_ref must target society.information_claim.");
            Require(claims.TryGetValue(record.Payload.ContentRef.RecordId, out var claim),
                "Every canonical Delivery content_ref must resolve to an actual InformationClaim.");
            Require(record.Payload.SenderRef == claim!.Payload.ClaimantRef,
                "Every canonical Delivery sender_ref must equal the referenced InformationClaim claimant_ref.");
            Require(CryptographicOperations.FixedTimeEquals(record.Payload.ContentDigest, claim.Payload.ContentDigest),
                "Every canonical Delivery content_digest must copy the referenced InformationClaim digest byte-for-byte.");
            Require(record.Payload.RecipientRefs.Count == 1 &&
                    record.Payload.SenderRef != record.Payload.RecipientRefs[0],
                "Every canonical Delivery must have exactly one non-self Resident recipient.");

            var runtime = materialization.RuntimeDeliveries[i];
            Require(runtime.DeliveryId == record.RecordId &&
                    runtime.SourceRef == record.Payload.SenderRef.RecordId &&
                    runtime.DestinationRef == record.Payload.RecipientRefs[0].RecordId &&
                    runtime.ClaimRef == claim.Payload.ClaimToken &&
                    runtime.EligibleStep == 0 && runtime.Priority == 0 &&
                    runtime.Status == InformationDeliveryStatusV1.Queued,
                "Persistent Delivery genesis must bind exactly to the existing runtime queued lifecycle.");
        }

        var channelGroups = records.GroupBy(static record => record.Payload.ChannelRef).ToArray();
        Require(channelGroups.Length == 10_000 && channelGroups.All(static group => group.Count() == 2),
            "Each canonical CommunicationService must be referenced by exactly two Delivery records.");
        Require(records.All(static record =>
                record.Payload.ChannelRef.PartitionId.Value == InfrastructureCommunicationServicePayloadV1.PartitionId &&
                record.Payload.EligibleStep == 0 &&
                record.Payload.DeliveredStep is null &&
                record.Payload.Priority == 0 &&
                record.Payload.Status.Value == "queued"),
            "Canonical information.delivery genesis payload drifted.");

        Qa04InformationDeliveryCanonicalAuthorityV1.ValidateSecondaryIndexRebuild(materialization.Partition);
        var recovered = Qa04InformationDeliverySnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 20_000,
            "Canonical information.delivery Snapshot/recovery must semantically recover all 20,000 records.");

        var first = records[0];
        var second = records[1];
        var firstContentRefBefore = first.Payload.ContentRef;
        var firstContentDigestBefore = first.Payload.ContentDigest.ToArray();
        var delivered = materialization.RuntimeDeliveries[0].MarkDelivered();
        Require(delivered.Status == InformationDeliveryStatusV1.Delivered &&
                delivered.DeliveryId == materialization.RuntimeDeliveries[0].DeliveryId &&
                delivered.SourceRef == materialization.RuntimeDeliveries[0].SourceRef &&
                delivered.DestinationRef == materialization.RuntimeDeliveries[0].DestinationRef &&
                delivered.ClaimRef == materialization.RuntimeDeliveries[0].ClaimRef &&
                first.Payload.ContentRef == firstContentRefBefore &&
                CryptographicOperations.FixedTimeEquals(first.Payload.ContentDigest, firstContentDigestBefore),
            "Queued -> Delivered runtime transition must not mutate persistent content identity.");

        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                InformationDeliveryPayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Missing InformationClaim/Resident/CommunicationService authority must fail closed.");

        var wrongContent = first.Payload with { ContentRef = second.Payload.ContentRef };
        ExpectInvalid(() => ValidateMutated(first, wrongContent, materialization),
            "Wrong content_ref must fail closed.");

        var missingContent = first.Payload with
        {
            ContentRef = new PartitionRecordRefV1(SocietyInformationClaimPayloadV1.PartitionId, OpaqueId128.Zero),
        };
        ExpectInvalid(() => ValidateMutated(first, missingContent, materialization),
            "Missing content_ref must fail closed.");

        var wrongSender = first.Payload with { SenderRef = first.Payload.RecipientRefs[0] };
        ExpectInvalid(() => ValidateMutated(first, wrongSender, materialization),
            "Sender mismatch must fail closed.");

        var wrongRecipient = first.Payload with { RecipientRefs = second.Payload.RecipientRefs.ToArray() };
        ExpectInvalid(() => ValidateMutated(first, wrongRecipient, materialization),
            "Wrong recipient must fail closed.");

        var selfDelivery = first.Payload with { RecipientRefs = new[] { first.Payload.SenderRef } };
        ExpectInvalid(() => ValidateMutated(first, selfDelivery, materialization),
            "Self-delivery must fail closed for the approved benchmark fixture.");

        var nonCanonicalRecipients = first.Payload with
        {
            RecipientRefs = new[] { second.Payload.RecipientRefs[0], first.Payload.RecipientRefs[0] },
        };
        ExpectInvalid(() => ValidateMutated(first, nonCanonicalRecipients, materialization),
            "Non-canonical recipient list must fail closed.");

        var wrongChannel = first.Payload with { ChannelRef = second.Payload.ChannelRef };
        ExpectInvalid(() => ValidateMutated(first, wrongChannel, materialization),
            "Wrong CommunicationService channel_ref must fail closed.");

        ExpectInvalid(() => Qa04InformationDeliveryCanonicalAuthorityV1.ValidateIdentityUniqueness(new[] { first, first }),
            "Duplicate Delivery identity must fail closed.");

        var badDigestBytes = first.Payload.ContentDigest.ToArray();
        badDigestBytes[0] ^= 0x01;
        var badDigest = first.Payload with { ContentDigest = badDigestBytes };
        ExpectInvalid(() => ValidateMutated(first, badDigest, materialization),
            "Content digest drift must fail closed.");

        var badStatus = first.Payload with { Status = new StableToken("delivered") };
        ExpectInvalid(() => ValidateMutated(first, badStatus, materialization),
            "Genesis status drift must fail closed.");

        var deliveredStepMismatch = first.Payload with { DeliveredStep = 1 };
        ExpectInvalid(() => ValidateMutated(first, deliveredStepMismatch, materialization),
            "Queued Delivery with delivered_step must fail closed.");

        var badPriority = first.Payload with { Priority = 1 };
        ExpectInvalid(() => ValidateMutated(first, badPriority, materialization),
            "Priority genesis drift must fail closed.");

        var badEligibleStep = first.Payload with { EligibleStep = 1 };
        ExpectInvalid(() => ValidateMutated(first, badEligibleStep, materialization),
            "Eligible-step genesis drift must fail closed.");
    }

    private static void ValidateMutated(
        DomainRecordEnvelopeV1<InformationDeliveryPayloadV1> source,
        InformationDeliveryPayloadV1 payload,
        Qa04InformationDeliveryCanonicalMaterializationV1 materialization)
        => Qa04InformationDeliveryCanonicalAuthorityV1.ValidateCanonicalRecord(
            0,
            CopyWithPayload(source, payload),
            materialization);

    private static DomainRecordEnvelopeV1<InformationDeliveryPayloadV1> CopyWithPayload(
        DomainRecordEnvelopeV1<InformationDeliveryPayloadV1> record,
        InformationDeliveryPayloadV1 payload)
        => new(
            record.RecordId,
            record.RecordSchema,
            record.Revision,
            record.CreatedStep,
            record.RetiredStep,
            record.DetailLevel,
            record.LineageRef,
            payload);

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
