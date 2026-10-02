using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04RemainingInformationCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04RemainingInformationCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04RemainingInformationCanonicalAuthorityV1.MaterializeCanonical();
        Require(materialization.MaterializedRecordCount == 15_000 &&
                materialization.MediaDistribution.ItemCount == 5_000 &&
                materialization.RecordStore.ItemCount == 10_000,
            "Approved remaining Information authority must materialize exactly 15,000 records.");

        ProveMedia(materialization);
        ProveRecordStore(materialization);
    }

    private static void ProveMedia(Qa04RemainingInformationCanonicalMaterializationV1 materialization)
    {
        var records = materialization.MediaRecordsByOrdinal;
        Require(records.Count == 5_000 &&
                records.Select(static record => record.RecordId).Distinct().Count() == 5_000 &&
                records.Select(static record => record.Payload.ClaimRef).Distinct().Count() == 5_000 &&
                records.Select(static record => record.Payload.PublisherRef).Distinct().Count() == 5_000,
            "MediaDistribution identities, claims and publishers must all be one-to-one.");

        var claims = materialization.SocietyAuthority.InformationClaims.RecordsCanonical.ToDictionary(static record => record.RecordId);
        var organizations = materialization.SocietyAuthority.Organizations.RecordsCanonical.ToDictionary(static record => record.RecordId);
        var communications = materialization.ServiceAuthority.CommunicationServices.RecordsCanonical.ToDictionary(static record => record.RecordId);

        foreach (var record in records)
        {
            Require(claims.ContainsKey(record.Payload.ClaimRef.RecordId),
                "MediaDistribution claim_ref must resolve to actual InformationClaim authority.");
            Require(organizations.ContainsKey(record.Payload.PublisherRef.RecordId),
                "MediaDistribution publisher_ref must resolve to actual Organization authority.");
            Require(record.Payload.ChannelRefs.Count == 1,
                "MediaDistribution must have exactly one CommunicationService channel.");
            if (!communications.TryGetValue(record.Payload.ChannelRefs[0].RecordId, out var channel))
                throw new InvalidOperationException("MediaDistribution channel_ref must resolve to actual CommunicationService authority.");
            Require(record.Payload.AudienceScopeRefs.Count == 1 &&
                    record.Payload.AudienceScopeRefs[0] == channel.Payload.ServiceScopeRef,
                "MediaDistribution audience scope must equal the selected CommunicationService service_scope_ref.");
            Require(record.Payload.PublishedStep == 0 && record.Payload.ReachCount == 0 &&
                    record.Payload.Status.Value == "published",
                "MediaDistribution genesis payload drifted.");
        }

        Qa04RemainingInformationCanonicalAuthorityV1.ValidateMediaSecondaryIndexRebuild(materialization.MediaDistribution);
        Require(Qa04RemainingInformationSnapshotRecoveryEvidenceV1.VerifyMediaDistribution(materialization) == 5_000,
            "MediaDistribution Snapshot/recovery must recover all 5,000 records.");

        var first = records[0];
        var second = records[1];
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { ClaimRef = second.Payload.ClaimRef }, materialization),
            "Wrong media claim must fail closed.");
        var missingClaim = first.Payload with
        {
            ClaimRef = new PartitionRecordRefV1(
                SocietyInformationClaimPayloadV1.PartitionId,
                OpaqueId128.Parse("fffffffffffffffffffffffffffffffe")),
        };
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                InformationMediaDistributionPayloadV1.PartitionId,
                missingClaim.ToStandardPayload(),
                materialization.References),
            "Missing media claim must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { PublisherRef = second.Payload.PublisherRef }, materialization),
            "Wrong media publisher must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { ChannelRefs = second.Payload.ChannelRefs.ToArray() }, materialization),
            "Wrong media channel must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { AudienceScopeRefs = new[] { first.Payload.ClaimRef } }, materialization),
            "Wrong media audience scope must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { ChannelRefs = Array.Empty<PartitionRecordRefV1>() }, materialization),
            "Non-canonical media channel cardinality must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { PublishedStep = 1 }, materialization),
            "Media published_step drift must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { ReachCount = 1 }, materialization),
            "Media reach_count drift must fail closed.");
        ExpectInvalid(() => ValidateMedia(first, first.Payload with { Status = new StableToken("active") }, materialization),
            "Media status drift must fail closed.");
        ExpectInvalid(() => Qa04RemainingInformationCanonicalAuthorityV1.ValidateMediaIdentityUniqueness(new[] { first, first }),
            "Duplicate media identity/relation must fail closed.");
    }

    private static void ProveRecordStore(Qa04RemainingInformationCanonicalMaterializationV1 materialization)
    {
        var records = materialization.RecordStoreRecordsByOrdinal;
        Require(records.Count == 10_000 &&
                records.Select(static record => record.RecordId).Distinct().Count() == 10_000 &&
                records.Select(static record => record.Payload.SubjectRefs.Single()).Distinct().Count() == 10_000,
            "RecordStore identities and InformationClaim subjects must be one-to-one.");

        var claims = materialization.SocietyAuthority.InformationClaims.RecordsCanonical.ToDictionary(static record => record.RecordId);
        foreach (var record in records)
        {
            Require(record.Payload.SubjectRefs.Count == 1,
                "RecordStore must have exactly one InformationClaim subject.");
            if (!claims.TryGetValue(record.Payload.SubjectRefs[0].RecordId, out var claim))
                throw new InvalidOperationException("RecordStore subject must resolve to actual InformationClaim authority.");
            Require(record.Payload.RecordKind.Value == "perf.information-claim-record" &&
                    record.Payload.AuthorityRef is null && record.Payload.SupersedesRef is null &&
                    record.Payload.Version == 1 && record.Payload.CreatedStep == claim.Payload.CreatedStep &&
                    record.Payload.Available,
                "RecordStore genesis scalar/optional authority drifted.");
            Require(CryptographicOperations.FixedTimeEquals(record.Payload.ContentDigest, claim.Payload.ContentDigest),
                "RecordStore content_digest must copy InformationClaim content_digest byte-for-byte.");
        }

        Qa04RemainingInformationCanonicalAuthorityV1.ValidateRecordStoreSecondaryIndexRebuild(materialization.RecordStore);
        Require(Qa04RemainingInformationSnapshotRecoveryEvidenceV1.VerifyRecordStore(materialization) == 10_000,
            "RecordStore Snapshot/recovery must recover all 10,000 records.");

        var first = records[0];
        var second = records[1];
        ExpectInvalid(() => ValidateStore(first, first.Payload with { SubjectRefs = second.Payload.SubjectRefs.ToArray() }, materialization),
            "Wrong RecordStore subject must fail closed.");
        var missingSubject = first.Payload with
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
                missingSubject.ToStandardPayload(),
                materialization.References),
            "Missing RecordStore subject must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with { SubjectRefs = Array.Empty<PartitionRecordRefV1>() }, materialization),
            "Empty RecordStore subject list must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with
            {
                SubjectRefs = new[] { first.Payload.SubjectRefs[0], second.Payload.SubjectRefs[0] },
            }, materialization),
            "Non-canonical RecordStore subject list must fail closed.");

        var badDigestBytes = first.Payload.ContentDigest.ToArray();
        badDigestBytes[0] ^= 0x01;
        ExpectInvalid(() => ValidateStore(first, first.Payload with { ContentDigest = badDigestBytes }, materialization),
            "RecordStore digest drift must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with { RecordKind = new StableToken("record") }, materialization),
            "RecordStore kind drift must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with { AuthorityRef = first.Payload.SubjectRefs[0] }, materialization),
            "RecordStore authority injection must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with
            {
                SupersedesRef = new PartitionRecordRefV1(InformationRecordStorePayloadV1.PartitionId, first.RecordId),
            }, materialization),
            "RecordStore predecessor injection must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with { Version = 2 }, materialization),
            "RecordStore version drift must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with { CreatedStep = 1 }, materialization),
            "RecordStore created_step drift must fail closed.");
        ExpectInvalid(() => ValidateStore(first, first.Payload with { Available = false }, materialization),
            "RecordStore availability drift must fail closed.");
        ExpectInvalid(() => Qa04RemainingInformationCanonicalAuthorityV1.ValidateRecordStoreIdentityUniqueness(new[] { first, first }),
            "Duplicate RecordStore identity/relation must fail closed.");
    }

    private static void ValidateMedia(
        DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1> source,
        InformationMediaDistributionPayloadV1 payload,
        Qa04RemainingInformationCanonicalMaterializationV1 materialization)
        => Qa04RemainingInformationCanonicalAuthorityV1.ValidateCanonicalMediaRecord(
            0,
            CopyWithPayload(source, payload),
            materialization);

    private static void ValidateStore(
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
