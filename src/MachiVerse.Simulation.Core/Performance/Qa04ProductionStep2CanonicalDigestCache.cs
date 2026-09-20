using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Run-scoped performance cache for canonical payload digests used by Gate4 Step2.
/// The cache never changes canonical inputs or digest definitions: it only reuses the already-computed
/// digest for the same immutable payload object while the exact same reference resolver is in force.
/// </summary>
public sealed class Qa04ProductionStep2CanonicalDigestCacheV1
{
    private readonly IDomainRecordSchemaResolverV1 _references;
    private readonly ConditionalWeakTable<InfrastructureServiceQueuePayloadV1, byte[]> _infrastructure = new();
    private readonly ConditionalWeakTable<ResidentBehaviorStatePayloadV1, byte[]> _resident = new();
    private readonly ConditionalWeakTable<PhysicalPresencePayloadV1, byte[]> _physical = new();
    private readonly ConditionalWeakTable<SocietyMarketTransactionRecordPayloadV2, byte[]> _market = new();
    private readonly ConditionalWeakTable<GovernanceSecurityIncidentPayloadV1, byte[]> _governance = new();
    private readonly ConditionalWeakTable<EnvironmentHazardPayloadV1, byte[]> _environment = new();
    private readonly ConditionalWeakTable<DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>, byte[]> _infrastructureRecords = new();
    private readonly ConditionalWeakTable<DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>, byte[]> _residentRecords = new();
    private readonly ConditionalWeakTable<DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>, byte[]> _physicalRecords = new();
    private readonly ConditionalWeakTable<DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>, byte[]> _marketRecords = new();
    private readonly ConditionalWeakTable<DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>, byte[]> _governanceRecords = new();
    private readonly ConditionalWeakTable<DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1>, byte[]> _environmentRecords = new();
    private readonly Qa04RecordIdPrefixPartitionDigestCacheV2<InfrastructureServiceQueuePayloadV1> _infrastructureChunks;
    private readonly Qa04RecordIdPrefixPartitionDigestCacheV2<ResidentBehaviorStatePayloadV1> _residentChunks;
    private readonly Qa04RecordIdPrefixPartitionDigestCacheV2<PhysicalPresencePayloadV1> _physicalChunks;
    private readonly Qa04RecordIdPrefixPartitionDigestCacheV2<SocietyMarketTransactionRecordPayloadV2> _marketChunks;
    private readonly Qa04RecordIdPrefixPartitionDigestCacheV2<GovernanceSecurityIncidentPayloadV1> _governanceChunks;
    private readonly Qa04RecordIdPrefixPartitionDigestCacheV2<EnvironmentHazardPayloadV1> _environmentChunks;

    public Qa04ProductionStep2CanonicalDigestCacheV1(IDomainRecordSchemaResolverV1 references)
    {
        _references = references ?? throw new ArgumentNullException(nameof(references));
        _infrastructureChunks = new(record =>
            PartitionStateHeaderV1.EncodeCanonicalRecord(record, Infrastructure(record.Payload)));
        _residentChunks = new(record =>
            PartitionStateHeaderV1.EncodeCanonicalRecord(record, Resident(record.Payload)));
        _physicalChunks = new(record =>
            PartitionStateHeaderV1.EncodeCanonicalRecord(record, Physical(record.Payload)));
        _marketChunks = new(record =>
            PartitionStateHeaderV1.EncodeCanonicalRecord(record, Market(record.Payload)));
        _governanceChunks = new(record =>
            PartitionStateHeaderV1.EncodeCanonicalRecord(record, Governance(record.Payload)));
        _environmentChunks = new(record =>
            PartitionStateHeaderV1.EncodeCanonicalRecord(record, Environment(record.Payload)));
    }

    internal Qa04RecordIdPrefixPartitionDigestCacheV2<InfrastructureServiceQueuePayloadV1> InfrastructureChunks
        => _infrastructureChunks;
    internal Qa04RecordIdPrefixPartitionDigestCacheV2<ResidentBehaviorStatePayloadV1> ResidentChunks
        => _residentChunks;
    internal Qa04RecordIdPrefixPartitionDigestCacheV2<PhysicalPresencePayloadV1> PhysicalChunks
        => _physicalChunks;
    internal Qa04RecordIdPrefixPartitionDigestCacheV2<SocietyMarketTransactionRecordPayloadV2> MarketChunks
        => _marketChunks;
    internal Qa04RecordIdPrefixPartitionDigestCacheV2<GovernanceSecurityIncidentPayloadV1> GovernanceChunks
        => _governanceChunks;
    internal Qa04RecordIdPrefixPartitionDigestCacheV2<EnvironmentHazardPayloadV1> EnvironmentChunks
        => _environmentChunks;

    public byte[] Infrastructure(InfrastructureServiceQueuePayloadV1 payload)
        => _infrastructure.GetValue(payload, value => StandardDomainPayloadCanonicalDigestV1.Compute(
            InfrastructureServiceQueuePayloadV1.PartitionId,
            value.ToStandardPayload(),
            references: _references));

    public byte[] Resident(ResidentBehaviorStatePayloadV1 payload)
        => _resident.GetValue(payload, value => StandardDomainPayloadCanonicalDigestV1.Compute(
            ResidentBehaviorStatePayloadV1.PartitionId,
            value.ToStandardPayload(),
            references: _references));

    public byte[] Physical(PhysicalPresencePayloadV1 payload)
        => _physical.GetValue(payload, value => StandardDomainPayloadCanonicalDigestV1.Compute(
            PhysicalPresencePayloadV1.PartitionId,
            value.ToStandardPayload(),
            references: _references));

    public byte[] Market(SocietyMarketTransactionRecordPayloadV2 payload)
        => _market.GetValue(payload, value =>
            SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(value, _references));

    public byte[] Governance(GovernanceSecurityIncidentPayloadV1 payload)
        => _governance.GetValue(payload, value => StandardDomainPayloadCanonicalDigestV1.Compute(
            GovernanceSecurityIncidentPayloadV1.PartitionId,
            value.ToStandardPayload(),
            references: _references));

    public byte[] Environment(EnvironmentHazardPayloadV1 payload)
        => _environment.GetValue(payload, value => StandardDomainPayloadCanonicalDigestV1.Compute(
            EnvironmentHazardPayloadV1.PartitionId,
            value.ToStandardPayload(),
            references: _references));

    public byte[] InfrastructureRecord(
        DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1> record)
        => record.CreatedStep == 0
            ? _infrastructureRecords.GetValue(
                record,
                value => PartitionStateHeaderV1.EncodeCanonicalRecord(value, Infrastructure(value.Payload)))
            : PartitionStateHeaderV1.EncodeCanonicalRecord(record, Infrastructure(record.Payload));

    public byte[] ResidentRecord(
        DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1> record)
        => _residentRecords.GetValue(
            record,
            value => PartitionStateHeaderV1.EncodeCanonicalRecord(value, Resident(value.Payload)));

    public byte[] PhysicalRecord(
        DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> record)
        => _physicalRecords.GetValue(
            record,
            value => PartitionStateHeaderV1.EncodeCanonicalRecord(value, Physical(value.Payload)));

    public byte[] MarketRecord(
        DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2> record)
        => record.CreatedStep == 0
            ? _marketRecords.GetValue(
                record,
                value => PartitionStateHeaderV1.EncodeCanonicalRecord(value, Market(value.Payload)))
            : PartitionStateHeaderV1.EncodeCanonicalRecord(record, Market(record.Payload));

    public byte[] GovernanceRecord(
        DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1> record)
        => record.CreatedStep == 0
            ? _governanceRecords.GetValue(
                record,
                value => PartitionStateHeaderV1.EncodeCanonicalRecord(value, Governance(value.Payload)))
            : PartitionStateHeaderV1.EncodeCanonicalRecord(record, Governance(record.Payload));

    public byte[] EnvironmentRecord(
        DomainRecordEnvelopeV1<EnvironmentHazardPayloadV1> record)
        => record.CreatedStep == 0
            ? _environmentRecords.GetValue(
                record,
                value => PartitionStateHeaderV1.EncodeCanonicalRecord(value, Environment(value.Payload)))
            : PartitionStateHeaderV1.EncodeCanonicalRecord(record, Environment(record.Payload));
}
