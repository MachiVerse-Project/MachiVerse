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
            ContentRef = new PartitionRecordRefV1(
                SocietyInformationClaimPayloadV1.PartitionId,
                OpaqueId128.Parse("ffffffffffffffffffffffffffffffff")),
        };
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                InformationDeliveryPayloadV1.PartitionId,
                missingContent.ToStandardPayload(),
                materialization.References),
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

        RunRemainingInformation(materialization);
    }

    private static void RunRemainingInformation(Qa04InformationDeliveryCanonicalMaterializationV1 deliveryMaterialization)
    {
        Qa04RemainingInformationCanonicalAuthorityV1.ValidateCanonicalContract();
        var remaining = Qa04RemainingInformationCanonicalAuthorityV1.MaterializeCanonical(
            deliveryMaterialization.SocietyAuthority,
            deliveryMaterialization.ServiceAuthority);

        Require(remaining.MaterializedRecordCount == 15_000 &&
                remaining.MediaDistribution.ItemCount == 5_000 &&
                remaining.RecordStore.ItemCount == 10_000,
            "Approved remaining Information authority must materialize exactly 5,000 media + 10,000 record-store records.");

        var claims = remaining.SocietyAuthority.InformationClaims.RecordsCanonical
            .ToDictionary(static record => record.RecordId);
        var organizations = remaining.SocietyAuthority.Organizations.RecordsCanonical
            .ToDictionary(static record => record.RecordId);
        var communications = remaining.ServiceAuthority.CommunicationServices.RecordsCanonical
            .ToDictionary(static record => record.RecordId);

        var media = remaining.MediaRecordsByOrdinal;
        Require(media.Count == 5_000 &&
                media.Select(static record => record.RecordId).Distinct().Count() == 5_000 &&
                media.Select(static record => record.Payload.ClaimRef).Distinct().Count() == 5_000 &&
                media.Select(static record => record.Payload.PublisherRef).Distinct().Count() == 5_000,
            "MediaDistribution identity/claim/publisher mapping must be one-to-one across 5,000 records.");

        foreach (var record in media)
        {
            Require(claims.ContainsKey(record.Payload.ClaimRef.RecordId),
                "MediaDistribution claim_ref must resolve to actual InformationClaim authority.");
            Require(organizations.ContainsKey(record.Payload.PublisherRef.RecordId),
                "MediaDistribution publisher_ref must resolve to actual Organization authority.");
            Require(record.Payload.ChannelRefs.Count == 1 &&
                    communications.TryGetValue(record.Payload.ChannelRefs[0].RecordId, out var channel),
                "MediaDistribution channel_refs must contain exactly one actual CommunicationService.");
            Require(record.Payload.AudienceScopeRefs.Count == 1 &&
                    record.Payload.AudienceScopeRefs[0] == channel!.Payload.ServiceScopeRef,
                "MediaDistribution audience scope must equal the selected CommunicationService service_scope_ref.");
            Require(record.Payload.PublishedStep == 0 &&
                    record.Payload.ReachCount == 0 &&
                    record.Payload.Status.Value == "published",
                "MediaDistribution genesis payload drifted.");
        }

        Qa04RemainingInformationCanonicalAuthorityV1.ValidateMediaSecondaryIndexRebuild(remaining.MediaDistribution);
        Require(Qa04RemainingInformationSnapshotRecoveryEvidenceV1.VerifyMediaDistribution(remaining) == 5_000,
            "MediaDistribution Snapshot/recovery must semantically recover all 5,000 records.");

        var firstMedia = media[0];
        var secondMedia = media[1];
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { ClaimRef = secondMedia.Payload.ClaimRef }, remaining),
            "Wrong media claim_ref must fail closed.");
        var missingMediaClaim = firstMedia.Payload with
        {
            ClaimRef = new PartitionRecordRefV1(
                SocietyInformationClaimPayloadV1.PartitionId,
                OpaqueId128.Parse("fffffffffffffffffffffffffffffffe")),
        };
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                InformationMediaDistributionPayloadV1.PartitionId,
                missingMediaClaim.ToStandardPayload(),
                remaining.References),
            "Missing media claim_ref must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { PublisherRef = secondMedia.Payload.PublisherRef }, remaining),
            "Wrong media publisher_ref must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { ChannelRefs = secondMedia.Payload.ChannelRefs.ToArray() }, remaining),
            "Wrong media channel_ref must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { AudienceScopeRefs = Array.Empty<PartitionRecordRefV1>() }, remaining),
            "Missing media audience scope must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with
            {
                ChannelRefs = new[] { firstMedia.Payload.ChannelRefs[0], secondMedia.Payload.ChannelRefs[0] },
            }, remaining),
            "Non-canonical media channel list must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { PublishedStep = 1 }, remaining),
            "Media published_step drift must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { ReachCount = 1 }, remaining),
            "Media reach_count drift must fail closed.");
        ExpectInvalid(() => ValidateMutatedMedia(firstMedia, firstMedia.Payload with { Status = new StableToken("active") }, remaining),
            "Media status drift must fail closed.");
        ExpectInvalid(() => Qa04RemainingInformationCanonicalAuthorityV1.ValidateMediaIdentityUniqueness(new[] { firstMedia, firstMedia }),
            "Duplicate MediaDistribution identity/relation must fail closed.");

        var stores = remaining.RecordStoreRecordsByOrdinal;
        Require(stores.Count == 10_000 &&
                stores.Select(static record => record.RecordId).Distinct().Count() == 10_000 &&
                stores.Select(static record => record.Payload.SubjectRefs[0]).Distinct().Count() == 10_000,
            "RecordStore identity/subject mapping must be one-to-one across 10,000 records.");

        foreach (var record in stores)
        {
            Require(record.Payload.SubjectRefs.Count == 1 &&
                    claims.TryGetValue(record.Payload.SubjectRefs[0].RecordId, out var claim),
                "RecordStore subject must be exactly one actual InformationClaim.");
            Require(record.Payload.RecordKind.Value == "perf.information-claim-record" &&
                    record.Payload.AuthorityRef is null &&
                    record.Payload.SupersedesRef is null &&
                    record.Payload.Version == 1 &&
                    record.Payload.CreatedStep == claim!.Payload.CreatedStep &&
                    record.Payload.Available,
                "RecordStore genesis scalar/optional authority drifted.");
            Require(CryptographicOperations.FixedTimeEquals(record.Payload.ContentDigest, claim.Payload.ContentDigest),
                "RecordStore content_digest must copy the InformationClaim digest byte-for-byte.");
        }

        Qa04RemainingInformationCanonicalAuthorityV1.ValidateRecordStoreSecondaryIndexRebuild(remaining.RecordStore);
        Require(Qa04RemainingInformationSnapshotRecoveryEvidenceV1.VerifyRecordStore(remaining) == 10_000,
            "RecordStore Snapshot/recovery must semantically recover all 10,000 records.");

        var firstStore = stores[0];
        var secondStore = stores[1];
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { SubjectRefs = secondStore.Payload.SubjectRefs.ToArray() }, remaining),
            "Wrong RecordStore subject must fail closed.");
        var missingStoreSubject = firstStore.Payload with
        {
            SubjectRefs = new[]
            {
                new PartitionRecordRefV1(
                    SocietyInformationClaimPayloadV1.PartitionId,
                    OpaqueId128.Parse("fffffffffffffffffffffffffffffffd")),
            },
        };
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                InformationRecordStorePayloadV1.PartitionId,
                missingStoreSubject.ToStandardPayload(),
                remaining.References),
            "Missing RecordStore subject must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { SubjectRefs = Array.Empty<PartitionRecordRefV1>() }, remaining),
            "Empty RecordStore subject list must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with
            {
                SubjectRefs = new[] { firstStore.Payload.SubjectRefs[0], secondStore.Payload.SubjectRefs[0] },
            }, remaining),
            "Non-canonical RecordStore subject list must fail closed.");

        var storeDigest = firstStore.Payload.ContentDigest.ToArray();
        storeDigest[0] ^= 0x01;
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { ContentDigest = storeDigest }, remaining),
            "RecordStore digest drift must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { RecordKind = new StableToken("record") }, remaining),
            "RecordStore kind drift must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { AuthorityRef = firstStore.Payload.SubjectRefs[0] }, remaining),
            "RecordStore authority injection must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with
            {
                SupersedesRef = new PartitionRecordRefV1(InformationRecordStorePayloadV1.PartitionId, firstStore.RecordId),
            }, remaining),
            "RecordStore predecessor injection must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { Version = 2 }, remaining),
            "RecordStore version drift must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { CreatedStep = 1 }, remaining),
            "RecordStore created_step drift must fail closed.");
        ExpectInvalid(() => ValidateMutatedStore(firstStore, firstStore.Payload with { Available = false }, remaining),
            "RecordStore availability drift must fail closed.");
        ExpectInvalid(() => Qa04RemainingInformationCanonicalAuthorityV1.ValidateRecordStoreIdentityUniqueness(new[] { firstStore, firstStore }),
            "Duplicate RecordStore identity/relation must fail closed.");
    }

    private static void ValidateMutated(
        DomainRecordEnvelopeV1<InformationDeliveryPayloadV1> source,
        InformationDeliveryPayloadV1 payload,
        Qa04InformationDeliveryCanonicalMaterializationV1 materialization)
        => Qa04InformationDeliveryCanonicalAuthorityV1.ValidateCanonicalRecord(
            0,
            CopyWithPayload(source, payload),
            materialization);

    private static void ValidateMutatedMedia(
        DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1> source,
        InformationMediaDistributionPayloadV1 payload,
        Qa04RemainingInformationCanonicalMaterializationV1 materialization)
        => Qa04RemainingInformationCanonicalAuthorityV1.ValidateCanonicalMediaRecord(
            0,
            CopyWithPayload(source, payload),
            materialization);

    private static void ValidateMutatedStore(
        DomainRecordEnvelopeV1<InformationRecordStorePayloadV1> source,
        InformationRecordStorePayloadV1 payload,
        Qa04RemainingInformationCanonicalMaterializationV1 materialization)
        => Qa04RemainingInformationCanonicalAuthorityV1.ValidateCanonicalRecordStoreRecord(
            0,
            CopyWithPayload(source, payload),
            materialization);

    private static DomainRecordEnvelopeV1<TPayload> CopyWithPayload<TPayload>(
        DomainRecordEnvelopeV1<TPayload> record,
        TPayload payload)
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
