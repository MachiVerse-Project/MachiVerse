using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Protocol;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

/// <summary>
/// Local-only INT-01 bootstrap. This adapter deliberately does not replace the release transport
/// authentication design; it exists only so the single-Gateway Alpha can exercise the real SIM-14
/// gRPC boundary and durable state before INT-02 multi-Gateway/mTLS acceptance.
/// </summary>
public sealed record AlphaSingleGatewayOptions(
    string CoreConfigPath,
    string PersistenceRoot,
    OpaqueId128 WorldId,
    WorldSeed256 WorldSeed,
    OpaqueId128 CoreInstanceId,
    OpaqueId128 GatewayLogicalId,
    OpaqueId128 GatewayComponentInstanceId)
{
    private const string DefaultWorldId = "00000000000000000000000000000001";
    private const string DefaultCoreInstanceId = "00000000000000000000000000000002";
    private const string DefaultGatewayLogicalId = "00000000000000000000000000000003";
    private const string DefaultGatewayComponentInstanceId = "00000000000000000000000000000004";
    private const string DefaultWorldSeedHex = "0000000000000000000000000000000000000000000000000000000000000000";

    public static AlphaSingleGatewayOptions FromEnvironment()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MACHIVERSE_ALPHA_LOCAL"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "INT-01 local runtime is opt-in. Set MACHIVERSE_ALPHA_LOCAL=1; release transport authentication is not configured by this bootstrap.");

        return new AlphaSingleGatewayOptions(
            Environment.GetEnvironmentVariable("MACHIVERSE_CORE_CONFIG")
                ?? Path.Combine(AppContext.BaseDirectory, "config", "simulation-core.toml"),
            Environment.GetEnvironmentVariable("MACHIVERSE_WORLD_ROOT") ?? ".machiverse/alpha",
            ParseId("MACHIVERSE_WORLD_ID", DefaultWorldId),
            ParseSeed(Environment.GetEnvironmentVariable("MACHIVERSE_WORLD_SEED_HEX") ?? DefaultWorldSeedHex),
            ParseId("MACHIVERSE_CORE_INSTANCE_ID", DefaultCoreInstanceId),
            ParseId("MACHIVERSE_GATEWAY_LOGICAL_ID", DefaultGatewayLogicalId),
            ParseId("MACHIVERSE_GATEWAY_COMPONENT_INSTANCE_ID", DefaultGatewayComponentInstanceId));
    }

    private static OpaqueId128 ParseId(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable) ?? fallback;
        var id = OpaqueId128.Parse(value);
        if (id.IsZero) throw new InvalidDataException($"{variable} cannot be ZERO.");
        return id;
    }

    private static WorldSeed256 ParseSeed(string value)
    {
        if (value.Length != 64 || value.Any(static c => c is >= 'A' and <= 'F'))
            throw new InvalidDataException("MACHIVERSE_WORLD_SEED_HEX must be 64 lowercase hexadecimal digits.");
        try
        {
            return new WorldSeed256(Convert.FromHexString(value));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("MACHIVERSE_WORLD_SEED_HEX is not canonical hexadecimal.", ex);
        }
    }
}

public sealed class AlphaSingleGatewayRuntime
{
    private AlphaSingleGatewayRuntime(
        SqlitePersistenceStore store,
        CoreGatewayNegotiationProfileV1 negotiationProfile,
        CoreGatewayEnvelopeFactoryV1 envelopeFactory,
        IAuthenticatedGatewayIdentityResolverV1 identityResolver,
        CoreGatewaySessionRegistryV1 sessions,
        CoreMasterAuthorityCoordinatorV1 master,
        CoreGatewayOperationProtocolV1 operations,
        CoreConfirmedPublicationCoordinatorV1 publications,
        ICoreGatewayWorldAuthorityV1 worldAuthority)
    {
        Store = store;
        NegotiationProfile = negotiationProfile;
        EnvelopeFactory = envelopeFactory;
        IdentityResolver = identityResolver;
        Sessions = sessions;
        Master = master;
        Operations = operations;
        Publications = publications;
        WorldAuthority = worldAuthority;
    }

    public SqlitePersistenceStore Store { get; }
    public CoreGatewayNegotiationProfileV1 NegotiationProfile { get; }
    public CoreGatewayEnvelopeFactoryV1 EnvelopeFactory { get; }
    public IAuthenticatedGatewayIdentityResolverV1 IdentityResolver { get; }
    public CoreGatewaySessionRegistryV1 Sessions { get; }
    public CoreMasterAuthorityCoordinatorV1 Master { get; }
    public CoreGatewayOperationProtocolV1 Operations { get; }
    public CoreConfirmedPublicationCoordinatorV1 Publications { get; }
    public ICoreGatewayWorldAuthorityV1 WorldAuthority { get; }

    public static async Task<AlphaSingleGatewayRuntime> CreateAsync(
        AlphaSingleGatewayOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configText = await File.ReadAllTextAsync(options.CoreConfigPath, cancellationToken);
        var config = new CoreConfigCoordinator().LoadStartup(configText);
        var paths = ResolvePersistencePaths(options);
        var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken);

        CoreProtocolPersistenceHeadV1 head;
        try
        {
            head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.meta-not-initialized")
        {
            var genesis = CreateGenesis(options.WorldId, options.WorldSeed, config.Digest);
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(options.WorldId, genesis.RecordDigest);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    options.WorldId,
                    PersistenceLayout.ReadCurrent(paths),
                    options.WorldSeed,
                    continuity,
                    config.Generation,
                    config.Digest,
                    1),
                genesis,
                cancellationToken);
            head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
        }

        if (!CryptographicOperations.FixedTimeEquals(head.ConfigDigest, config.Digest))
            throw new InvalidDataException(
                "alpha.config-digest-mismatch: persisted world Config differs from config/simulation-core.toml; use the Config change path or a fresh Alpha world root.");

        var sessions = new CoreGatewaySessionRegistryV1();
        var master = new CoreMasterAuthorityCoordinatorV1(store, sessions);
        await master.RecoverAsync(cancellationToken);

        // INT-01 has exactly one configured local Gateway. Re-selection is durable on each Core start,
        // matching the SIM-14 rule that process recovery never restores a Gateway identity as authority.
        sessions.Register(
            options.GatewayLogicalId,
            new GatewayRegisterV1
            {
                GatewayLogicalId = ByteString.CopyFrom(options.GatewayLogicalId.ToBytes()),
                ComponentInstanceId = ByteString.CopyFrom(options.GatewayComponentInstanceId.ToBytes()),
                LastKnownMasterGeneration = master.Current.MasterGeneration,
                Readiness = (GatewayReadinessV1)3,
            });
        await master.AssignMasterAsync(
            options.GatewayLogicalId,
            new StableToken("master.alpha-single-gateway"),
            cancellationToken);

        head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
        var state = BuildEmptyConfirmedState(options, head);
        var worldAuthority = new AlphaWorldAuthorityV1(state, OperationSchedulingPolicyV1.FromConfig(config));
        var operations = new CoreGatewayOperationProtocolV1(
            store,
            master,
            new AlphaDurableOperationIngressV1(store, options.WorldId));
        var publications = new CoreConfirmedPublicationCoordinatorV1(store, new AlphaEmptyProjectionSourceV1());

        return new AlphaSingleGatewayRuntime(
            store,
            new CoreGatewayNegotiationProfileV1(),
            new CoreGatewayEnvelopeFactoryV1(options.CoreInstanceId),
            new AlphaLoopbackGatewayIdentityResolverV1(options.GatewayLogicalId),
            sessions,
            master,
            operations,
            publications,
            worldAuthority);
    }

    private static WorldPersistencePaths ResolvePersistencePaths(AlphaSingleGatewayOptions options)
    {
        var generationOne = PersistenceLayout.Resolve(options.PersistenceRoot, options.WorldId, 1);
        if (!File.Exists(generationOne.CurrentPath))
        {
            PersistenceLayout.EnsureGenerationDirectories(generationOne);
            PersistenceLayout.WriteCurrentAsync(generationOne, 1).GetAwaiter().GetResult();
            return generationOne;
        }

        var currentGeneration = PersistenceLayout.ReadCurrent(generationOne);
        var current = PersistenceLayout.Resolve(options.PersistenceRoot, options.WorldId, currentGeneration);
        PersistenceLayout.EnsureGenerationDirectories(current);
        return current;
    }

    private static HistoryRecordMaterial CreateGenesis(
        OpaqueId128 worldId,
        WorldSeed256 seed,
        byte[] configDigest)
        => HistoryRecordMaterial.Create(
            worldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: worldId.ToBytes().Concat(seed.ToBytes()).Concat(configDigest).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
                writer.WriteUnsigned(3); writer.WriteBytes(configDigest);
            });

    private static WorldStateV1 BuildEmptyConfirmedState(
        AlphaSingleGatewayOptions options,
        CoreProtocolPersistenceHeadV1 head)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: head.FinalizedStep,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(Encoding.ASCII.GetBytes(
                    $"alpha-empty:{entry.PartitionId.Value}:{head.FinalizedStep}")))));
        var header = new WorldStateHeaderV1(
            options.WorldId,
            head.FinalizedStep,
            SHA256.HashData(options.WorldSeed.ToBytes()),
            head.ConfigGeneration,
            head.MasterGeneration,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            head.ConfigDigest);
    }
}

internal sealed class AlphaLoopbackGatewayIdentityResolverV1(OpaqueId128 gatewayLogicalId)
    : IAuthenticatedGatewayIdentityResolverV1
{
    public OpaqueId128 ResolveAuthenticatedGatewayLogicalId(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var peer = context.Peer ?? string.Empty;
        if (!peer.StartsWith("ipv4:127.0.0.1:", StringComparison.Ordinal) &&
            !peer.StartsWith("ipv6:[::1]:", StringComparison.Ordinal) &&
            !peer.StartsWith("ipv6:::1:", StringComparison.Ordinal))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "auth.unauthenticated: INT-01 local identity resolver accepts loopback peers only."));
        }
        return gatewayLogicalId;
    }
}

internal sealed class AlphaWorldAuthorityV1(
    WorldStateV1 state,
    OperationSchedulingPolicyV1 schedulingPolicy) : ICoreGatewayWorldAuthorityV1
{
    public Task<WorldStateV1> GetCurrentFinalizedStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state);
    }

    public OperationSchedulingPolicyV1 GetCurrentSchedulingPolicy() => schedulingPolicy;
}

internal sealed class AlphaDurableOperationIngressV1(
    SqlitePersistenceStore store,
    OpaqueId128 worldId) : ICoreGatewayDurableOperationIngressV1
{
    public async Task<OperationDurableObservationV1> SubmitDurablyAsync(
        StandardOperationV1 operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var operationId = OpaqueId128.FromBytes(operation.OperationId.Span);
        var digest = operation.ImmutablePayloadDigest.ToByteArray();
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken);
        if (anchor.Sequence == ulong.MaxValue) throw new OverflowException("HistorySequence cannot wrap.");

        var history = HistoryRecordMaterial.Create(
            worldId,
            anchor.Sequence + 1,
            anchor.Digest,
            "operation.accepted.v1",
            "persistence.operation-accepted",
            1,
            0,
            operation.ToByteArray(),
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(digest);
                writer.WriteUnsigned(2); writer.WriteAsciiText(operation.OperationKind);
            });
        var accepted = await store.PersistAcceptedOperationAsync(operationId, digest, history, cancellationToken);
        return new OperationDurableObservationV1(
            OperationLifecycleStateV1.AcceptedDurable,
            Duplicate: accepted.Status == DurableAcceptanceStatus.Duplicate,
            accepted.AcceptedSequence,
            ScheduledSequence: null,
            EffectiveStep: null,
            TerminalSequence: null,
            TerminalStatus: null,
            ResultCode: null);
    }
}

internal sealed class AlphaEmptyProjectionSourceV1 : ICoreStateProjectionSourceV1
{
    private static readonly byte[] ProjectionSchemaDigest = SHA256.HashData("machiverse.alpha.empty-projection.v1"u8);

    public CoreProjectionFrameV1 BuildFull(WorldStateV1 state)
        => new(Array.Empty<ProjectionRecordV1>(), ProjectionSchemaDigest);

    public CoreProjectionFrameV1 BuildDelta(WorldStateV1 previousState, WorldStateV1 currentState)
        => new(Array.Empty<ProjectionRecordV1>(), ProjectionSchemaDigest);
}
