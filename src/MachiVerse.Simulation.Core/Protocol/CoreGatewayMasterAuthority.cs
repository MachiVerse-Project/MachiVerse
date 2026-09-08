using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Protocol;

public sealed record RegisteredGatewayV1(
    OpaqueId128 GatewayLogicalId,
    OpaqueId128 ComponentInstanceId,
    GatewayReadinessV1 Readiness,
    ulong LastKnownMasterGeneration,
    ulong? ConfirmedBasisStep,
    byte[]? ConfirmedContinuityToken,
    uint PeerConnectionCount,
    uint ViewConnectionCount,
    uint AdminConnectionCount);

public sealed class CoreGatewaySessionRegistryV1
{
    private const int ReadinessUnspecified = 0;
    private readonly object _gate = new();
    private readonly SortedDictionary<OpaqueId128, RegisteredGatewayV1> _gateways = new();

    public IReadOnlyList<RegisteredGatewayV1> Snapshot()
    {
        lock (_gate) return _gateways.Values.ToArray();
    }

    public RegisteredGatewayV1 Register(OpaqueId128 authenticatedGatewayLogicalId, GatewayRegisterV1 register)
    {
        if (authenticatedGatewayLogicalId.IsZero)
            throw new ArgumentException("Authenticated Gateway identity cannot be ZERO.", nameof(authenticatedGatewayLogicalId));
        ArgumentNullException.ThrowIfNull(register);
        var logicalId = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(register.GatewayLogicalId, "gateway_logical_id", false));
        var componentId = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(register.ComponentInstanceId, "component_instance_id", false));
        if (logicalId != authenticatedGatewayLogicalId)
            throw new CoreGatewayProtocolException("auth.unauthorized", "gateway.register identity does not match authenticated certificate identity.");
        if (!Enum.IsDefined(register.Readiness) || (int)register.Readiness == ReadinessUnspecified)
            throw new CoreGatewayProtocolException("protocol.field-out-of-range", "Gateway readiness is unspecified or invalid.");

        var next = new RegisteredGatewayV1(
            logicalId,
            componentId,
            register.Readiness,
            register.LastKnownMasterGeneration,
            null,
            null,
            0,
            0,
            0);
        lock (_gate)
        {
            _gateways[logicalId] = next;
            return next;
        }
    }

    public RegisteredGatewayV1 Heartbeat(OpaqueId128 authenticatedGatewayLogicalId, GatewayHeartbeatV1 heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        var logicalId = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(heartbeat.GatewayLogicalId, "gateway_logical_id", false));
        var componentId = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(heartbeat.ComponentInstanceId, "component_instance_id", false));
        if (logicalId != authenticatedGatewayLogicalId)
            throw new CoreGatewayProtocolException("auth.unauthorized", "gateway.heartbeat identity does not match authenticated certificate identity.");
        if (!Enum.IsDefined(heartbeat.Readiness) || (int)heartbeat.Readiness == ReadinessUnspecified)
            throw new CoreGatewayProtocolException("protocol.field-out-of-range", "Gateway readiness is unspecified or invalid.");
        byte[]? continuity = null;
        if (heartbeat.HasConfirmedContinuityToken)
            continuity = CoreGatewayWireValidatorV1.ValidateHash256(heartbeat.ConfirmedContinuityToken, "confirmed_continuity_token");
        if (heartbeat.HasConfirmedBasisStep != heartbeat.HasConfirmedContinuityToken)
            throw new CoreGatewayProtocolException("protocol.missing-required", "confirmed basis and continuity token must be reported together.");

        lock (_gate)
        {
            if (!_gateways.TryGetValue(logicalId, out var current))
                throw new CoreGatewayProtocolException("request.stale", "Gateway must register before heartbeat.");
            if (current.ComponentInstanceId != componentId)
                throw new CoreGatewayProtocolException("request.stale", "Gateway component instance changed without re-registration.");
            var next = current with
            {
                Readiness = heartbeat.Readiness,
                ConfirmedBasisStep = heartbeat.HasConfirmedBasisStep ? heartbeat.ConfirmedBasisStep : null,
                ConfirmedContinuityToken = continuity,
                PeerConnectionCount = heartbeat.PeerConnectionCount,
                ViewConnectionCount = heartbeat.ViewConnectionCount,
                AdminConnectionCount = heartbeat.AdminConnectionCount,
            };
            _gateways[logicalId] = next;
            return next;
        }
    }

    public void Disconnect(OpaqueId128 authenticatedGatewayLogicalId, OpaqueId128 componentInstanceId)
    {
        lock (_gate)
        {
            if (_gateways.TryGetValue(authenticatedGatewayLogicalId, out var current) && current.ComponentInstanceId == componentInstanceId)
                _gateways.Remove(authenticatedGatewayLogicalId);
        }
    }

    public RegisteredGatewayV1 RequireRegistered(OpaqueId128 logicalId)
    {
        lock (_gate)
            return _gateways.TryGetValue(logicalId, out var gateway)
                ? gateway
                : throw new CoreGatewayProtocolException("component.unavailable", "Gateway is not currently registered.");
    }
}

public sealed record CoreMasterAuthoritySnapshotV1(
    ulong MasterGeneration,
    OpaqueId128? CurrentMasterGatewayId);

public sealed class CoreMasterAuthorityCoordinatorV1
{
    private const int ReadinessResyncing = 2;
    private const int ReadinessReady = 3;
    private const int RoleNonMaster = 1;
    private const int RoleMaster = 2;
    private const int RoleTransition = 3;

    private readonly SqlitePersistenceStore _store;
    private readonly CoreGatewaySessionRegistryV1 _sessions;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private CoreMasterAuthoritySnapshotV1? _current;

    public CoreMasterAuthorityCoordinatorV1(SqlitePersistenceStore store, CoreGatewaySessionRegistryV1 sessions)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public CoreMasterAuthoritySnapshotV1 Current
        => Volatile.Read(ref _current) ?? throw new InvalidOperationException("Master authority coordinator has not been recovered.");

    public async Task<CoreMasterAuthoritySnapshotV1> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        var recovered = new CoreMasterAuthoritySnapshotV1(head.MasterGeneration, null);
        Volatile.Write(ref _current, recovered);
        return recovered;
    }

    public async Task<CoreMasterAuthoritySnapshotV1> AssignMasterAsync(
        OpaqueId128 gatewayLogicalId,
        StableToken reasonCode,
        CancellationToken cancellationToken = default)
    {
        if (gatewayLogicalId.IsZero) throw new ArgumentException("Gateway logical id cannot be ZERO.", nameof(gatewayLogicalId));

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var gateway = _sessions.RequireRegistered(gatewayLogicalId);
            if (!IsAssignmentEligible(gateway))
                throw new CoreGatewayProtocolException("component.unavailable", "Gateway is not eligible for Master assignment.");
            return await AssignMasterUnderGateAsync(gatewayLogicalId, reasonCode, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    /// <summary>
    /// Reconciles the Core-owned Master authority with the currently registered Gateway set.
    /// Automatic election only chooses READY gateways and uses the registry's canonical logical-id order.
    /// RESYNCING remains eligible for an explicit recovery assignment through AssignMasterAsync, but cannot
    /// win an automatic election before it has a confirmed synchronized projection.
    /// </summary>
    public async Task<CoreMasterAuthoritySnapshotV1> ReconcileAsync(
        StableToken reasonCode,
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var current = Current;
            var sessions = _sessions.Snapshot();
            if (current.CurrentMasterGatewayId is { } currentMaster)
            {
                var stillEligible = sessions.Any(gateway =>
                    gateway.GatewayLogicalId == currentMaster && IsAutomaticElectionEligible(gateway));
                if (stillEligible) return current;
            }

            var candidate = sessions
                .Where(IsAutomaticElectionEligible)
                .OrderBy(static gateway => gateway.GatewayLogicalId)
                .FirstOrDefault();
            if (candidate is null)
            {
                if (current.CurrentMasterGatewayId is null) return current;
                var unavailable = current with { CurrentMasterGatewayId = null };
                Volatile.Write(ref _current, unavailable);
                return unavailable;
            }

            return await AssignMasterUnderGateAsync(candidate.GatewayLogicalId, reasonCode, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public void RequireCurrentMaster(ulong masterGeneration, OpaqueId128 gatewayLogicalId)
    {
        var current = Current;
        if (masterGeneration != current.MasterGeneration)
            throw new CoreGatewayProtocolException("master.stale-generation", "Submitted MasterGeneration is stale.");
        if (current.CurrentMasterGatewayId is null)
            throw new CoreGatewayProtocolException("component.resyncing", "Master authority has not been re-established.");
        if (current.CurrentMasterGatewayId.Value != gatewayLogicalId)
            throw new CoreGatewayProtocolException("auth.unauthorized", "Submitting Gateway is not the current Master.");
    }

    public MasterGenerationStateV1 ToWireState()
    {
        var current = Current;
        var wire = new MasterGenerationStateV1 { MasterGeneration = current.MasterGeneration };
        if (current.CurrentMasterGatewayId is { } id)
            wire.CurrentMasterGatewayId = ByteString.CopyFrom(id.ToBytes());
        return wire;
    }

    public GatewayRoleStateV1 ToRoleState(OpaqueId128 gatewayLogicalId)
    {
        var current = Current;
        var role = current.CurrentMasterGatewayId switch
        {
            null => (GatewayRoleV1)RoleTransition,
            { } id when id == gatewayLogicalId => (GatewayRoleV1)RoleMaster,
            _ => (GatewayRoleV1)RoleNonMaster,
        };
        var wire = new GatewayRoleStateV1
        {
            GatewayLogicalId = ByteString.CopyFrom(gatewayLogicalId.ToBytes()),
            Role = role,
            MasterGeneration = current.MasterGeneration,
        };
        if (current.CurrentMasterGatewayId is { } masterId)
            wire.CurrentMasterGatewayId = ByteString.CopyFrom(masterId.ToBytes());
        return wire;
    }

    private async Task<CoreMasterAuthoritySnapshotV1> AssignMasterUnderGateAsync(
        OpaqueId128 gatewayLogicalId,
        StableToken reasonCode,
        CancellationToken cancellationToken)
    {
        var current = Current;
        if (current.CurrentMasterGatewayId == gatewayLogicalId) return current;
        if (current.MasterGeneration == ulong.MaxValue) throw new OverflowException("MasterGeneration cannot wrap.");

        var nextGeneration = current.MasterGeneration + 1;
        var head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        if (head.MasterGeneration != current.MasterGeneration)
            throw new CoreGatewayProtocolException("master.stale-generation", "Persisted MasterGeneration changed concurrently.");
        var anchor = await _store.ReadHistoryAnchorAsync(cancellationToken);
        if (anchor.Sequence == ulong.MaxValue) throw new OverflowException("HistorySequence cannot wrap.");

        var physicalPayload = EncodeMasterGenerationChanged(current.MasterGeneration, nextGeneration, reasonCode);
        var previousGeneration = current.MasterGeneration;
        var history = HistoryRecordMaterial.Create(
            head.WorldId,
            anchor.Sequence + 1,
            anchor.Digest,
            "master.generation.changed.v1",
            "persistence.master-generation-changed",
            1,
            0,
            physicalPayload,
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(previousGeneration);
                writer.WriteUnsigned(1); writer.WriteUnsigned(nextGeneration);
                writer.WriteUnsigned(2); writer.WriteAsciiText(reasonCode.Value);
            });

        await _store.PersistMasterGenerationChangeAsync(previousGeneration, nextGeneration, history, cancellationToken);
        var next = new CoreMasterAuthoritySnapshotV1(nextGeneration, gatewayLogicalId);
        Volatile.Write(ref _current, next);
        return next;
    }

    private static bool IsAssignmentEligible(RegisteredGatewayV1 gateway)
        => (int)gateway.Readiness is ReadinessResyncing or ReadinessReady;

    private static bool IsAutomaticElectionEligible(RegisteredGatewayV1 gateway)
        => (int)gateway.Readiness == ReadinessReady;

    private static byte[] EncodeMasterGenerationChanged(ulong previous, ulong next, StableToken reasonCode)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            output.WriteTag(1, WireFormat.WireType.Varint);
            output.WriteUInt64(previous);
            output.WriteTag(2, WireFormat.WireType.Varint);
            output.WriteUInt64(next);
            output.WriteTag(3, WireFormat.WireType.LengthDelimited);
            output.WriteString(reasonCode.Value);
            output.Flush();
        }
        return stream.ToArray();
    }
}
