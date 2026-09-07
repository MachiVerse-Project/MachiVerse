using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Protocol;

public sealed class CoreProjectionFrameV1
{
    private const int MutationUnspecified = 0;

    public CoreProjectionFrameV1(IEnumerable<ProjectionRecordV1> records, ReadOnlySpan<byte> projectionSchemaDigest)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (projectionSchemaDigest.Length != 32)
            throw new ArgumentException("Projection schema digest must be 32 bytes.", nameof(projectionSchemaDigest));
        var ordered = records
            .Select(static record => record?.Clone() ?? throw new ArgumentNullException(nameof(records)))
            .OrderBy(static record => record.RecordSchemaId, StringComparer.Ordinal)
            .ThenBy(static record => record.RecordId, ByteStringLexicographicComparerV1.Instance)
            .ToArray();
        var keys = new HashSet<(string SchemaId, string RecordIdHex)>();
        foreach (var record in ordered)
        {
            CoreGatewayWireValidatorV1.ValidateStableToken(record.RecordSchemaId, "record_schema_id");
            CoreGatewayWireValidatorV1.ValidateId128(record.RecordId, "record_id", allowZero: false);
            if (record.RecordSchemaVersion is null || record.RecordSchemaVersion.Major == 0 || record.RecordSchemaVersion.Major > ushort.MaxValue || record.RecordSchemaVersion.Minor > ushort.MaxValue)
                throw new CoreGatewayProtocolException("protocol.schema-unsupported", "Projection record schema version is invalid.");
            if (!Enum.IsDefined(record.MutationKind) || (int)record.MutationKind == MutationUnspecified)
                throw new CoreGatewayProtocolException("protocol.field-out-of-range", "Projection mutation kind is invalid.");
            var key = (record.RecordSchemaId, Convert.ToHexString(record.RecordId.Span));
            if (!keys.Add(key))
                throw new CoreGatewayProtocolException("protocol.malformed", "Projection frame contains duplicate record keys.");
        }
        Records = Array.AsReadOnly(ordered);
        ProjectionSchemaDigest = projectionSchemaDigest.ToArray();
    }

    public IReadOnlyList<ProjectionRecordV1> Records { get; }
    public byte[] ProjectionSchemaDigest { get; }
}

public interface ICoreStateProjectionSourceV1
{
    CoreProjectionFrameV1 BuildFull(WorldStateV1 state);
    CoreProjectionFrameV1 BuildDelta(WorldStateV1 previousState, WorldStateV1 currentState);
}

public sealed record CoreStatePublicationBundleV1(
    ulong BasisStep,
    byte[] StateContinuityToken,
    StatePublicationV1 Publication,
    IReadOnlyList<StatePublicationChunkV1> Chunks);

public interface IProtocolPublicationIdSourceV1
{
    OpaqueId128 Next();
}

public sealed class RandomProtocolPublicationIdSourceV1 : IProtocolPublicationIdSourceV1
{
    public OpaqueId128 Next()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes);
        while (bytes.IndexOfAnyExcept((byte)0) < 0);
        return OpaqueId128.FromBytes(bytes);
    }
}

public sealed class CoreConfirmedPublicationCoordinatorV1
{
    private const int MutationUpsert = 1;
    private const int PublicationFull = 1;
    private const int PublicationDelta = 2;
    private const int ResyncUnspecified = 0;
    private const int ResyncContinueIfPossible = 1;

    private readonly SqlitePersistenceStore _store;
    private readonly ICoreStateProjectionSourceV1 _projectionSource;
    private readonly IProtocolPublicationIdSourceV1 _publicationIds;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PublishedAuthoritySnapshotV1? _lastPublished;

    public CoreConfirmedPublicationCoordinatorV1(
        SqlitePersistenceStore store,
        ICoreStateProjectionSourceV1 projectionSource,
        IProtocolPublicationIdSourceV1? publicationIds = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _projectionSource = projectionSource ?? throw new ArgumentNullException(nameof(projectionSource));
        _publicationIds = publicationIds ?? new RandomProtocolPublicationIdSourceV1();
    }

    public async Task<CoreStatePublicationBundleV1> BuildFullAsync(
        WorldStateV1 state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var head = await RequireDurableFinalizedStateAsync(state, cancellationToken);
            var frame = _projectionSource.BuildFull(state);
            if (frame.Records.Any(static record => (int)record.MutationKind != MutationUpsert))
                throw new CoreGatewayProtocolException("protocol.malformed", "FULL publication may contain only UPSERT records.");
            var bundle = BuildBundle((PublicationKindV1)PublicationFull, state.Header.Step, head.StateContinuityToken, baseToken: null, frame);
            _lastPublished = new PublishedAuthoritySnapshotV1(state, head.StateContinuityToken.ToArray(), frame.ProjectionSchemaDigest.ToArray());
            return bundle;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CoreStatePublicationBundleV1> BuildDeltaAsync(
        WorldStateV1 state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var last = _lastPublished ?? throw new CoreGatewayProtocolException("world.resync-required", "No in-process confirmed publication base is retained; FULL is required.");
            var head = await RequireDurableFinalizedStateAsync(state, cancellationToken);
            if (state.Header.WorldId != last.State.Header.WorldId || state.Header.Step <= last.State.Header.Step)
                throw new CoreGatewayProtocolException("protocol.continuity-mismatch", "DELTA state is not a forward continuation of the retained base.");
            var frame = _projectionSource.BuildDelta(last.State, state);
            if (!CryptographicOperations.FixedTimeEquals(frame.ProjectionSchemaDigest, last.ProjectionSchemaDigest))
                throw new CoreGatewayProtocolException("protocol.schema-unsupported", "Projection schema changed across DELTA base; FULL publication is required.");
            var bundle = BuildBundle((PublicationKindV1)PublicationDelta, state.Header.Step, head.StateContinuityToken, last.ContinuityToken, frame);
            _lastPublished = new PublishedAuthoritySnapshotV1(state, head.StateContinuityToken.ToArray(), frame.ProjectionSchemaDigest.ToArray());
            return bundle;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CoreStatePublicationBundleV1> BuildForResyncAsync(
        StateResyncRequestV1 request,
        WorldStateV1 currentState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentState);
        var requestedWorld = OpaqueId128.FromBytes(CoreGatewayWireValidatorV1.ValidateId128(request.WorldId, "world_id", allowZero: false));
        if (requestedWorld != currentState.Header.WorldId)
            throw new CoreGatewayProtocolException("world.not-found", "Resync request targets another WorldId.");
        if (!Enum.IsDefined(request.Preference) || (int)request.Preference == ResyncUnspecified)
            throw new CoreGatewayProtocolException("protocol.field-out-of-range", "Resync preference is unspecified.");
        if (request.HasClientContinuityToken)
            CoreGatewayWireValidatorV1.ValidateHash256(request.ClientContinuityToken, "client_continuity_token");
        if (request.HasClientBasisStep != request.HasClientContinuityToken)
            throw new CoreGatewayProtocolException("protocol.missing-required", "client_basis_step and client_continuity_token must be supplied together.");

        var last = Volatile.Read(ref _lastPublished);
        var canContinue = (int)request.Preference == ResyncContinueIfPossible &&
            request.HasClientBasisStep && request.HasClientContinuityToken &&
            last is not null &&
            request.ClientBasisStep == last.State.Header.Step &&
            CryptographicOperations.FixedTimeEquals(request.ClientContinuityToken.Span, last.ContinuityToken) &&
            currentState.Header.Step > last.State.Header.Step;
        return canContinue
            ? await BuildDeltaAsync(currentState, cancellationToken)
            : await BuildFullAsync(currentState, cancellationToken);
    }

    private async Task<CoreProtocolPersistenceHeadV1> RequireDurableFinalizedStateAsync(
        WorldStateV1 state,
        CancellationToken cancellationToken)
    {
        var head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        if (head.WorldId != state.Header.WorldId)
            throw new CoreGatewayProtocolException("world.not-found", "WorldState does not match persistence WorldId.");
        if (head.FinalizedStep != state.Header.Step)
            throw new CoreGatewayProtocolException("world.invalid-state", "State is not at the durable finalized frontier.");
        if (head.ConfigGeneration != state.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(head.ConfigDigest, state.Diagnostic.ConfigDigest))
            throw new CoreGatewayProtocolException("world.invalid-state", "State Config authority does not match durable persistence head.");
        return head;
    }

    private CoreStatePublicationBundleV1 BuildBundle(
        PublicationKindV1 kind,
        ulong basisStep,
        byte[] continuityToken,
        byte[]? baseToken,
        CoreProjectionFrameV1 frame)
    {
        if (continuityToken.Length != 32)
            throw new InvalidDataException("Durable state continuity token is not 32 bytes.");
        if ((int)kind == PublicationDelta && (baseToken is null || baseToken.Length != 32))
            throw new InvalidDataException("DELTA publication requires a 32-byte base token.");
        if ((int)kind == PublicationFull && baseToken is not null)
            throw new InvalidDataException("FULL publication cannot carry a base token.");

        var publicationId = _publicationIds.Next();
        var chunks = PackChunks(publicationId, frame.Records);
        if (chunks.Count == 0 || chunks.Count > CoreGatewayProtocolRegistryV1.MaxPublicationChunks)
            throw new CoreGatewayProtocolException("protocol.limit-exceeded", "Publication chunk count is outside protocol limits.");
        var publication = new StatePublicationV1
        {
            PublicationId = ByteString.CopyFrom(publicationId.ToBytes()),
            Kind = kind,
            StateContinuityToken = ByteString.CopyFrom(continuityToken),
            ChunkCount = checked((uint)chunks.Count),
            ProjectionSchemaDigest = ByteString.CopyFrom(frame.ProjectionSchemaDigest),
        };
        if (baseToken is not null) publication.BaseStateContinuityToken = ByteString.CopyFrom(baseToken);
        return new CoreStatePublicationBundleV1(
            basisStep,
            continuityToken.ToArray(),
            publication,
            Array.AsReadOnly(chunks.ToArray()));
    }

    private static IReadOnlyList<StatePublicationChunkV1> PackChunks(
        OpaqueId128 publicationId,
        IReadOnlyList<ProjectionRecordV1> records)
    {
        var groups = new List<List<ProjectionRecordV1>>();
        var current = new List<ProjectionRecordV1>();
        foreach (var record in records)
        {
            current.Add(record);
            if (PayloadSize(publicationId, 0, current) <= CoreGatewayProtocolRegistryV1.MaxPublicationChunkBytes)
                continue;

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0)
                groups.Add(current);

            current = [record];
            if (PayloadSize(publicationId, 0, current) > CoreGatewayProtocolRegistryV1.MaxPublicationChunkBytes)
                throw new CoreGatewayProtocolException("protocol.limit-exceeded", "A single projection record exceeds the 1 MiB publication chunk limit.");
        }
        if (current.Count > 0 || groups.Count == 0) groups.Add(current);
        if (groups.Count > CoreGatewayProtocolRegistryV1.MaxPublicationChunks)
            throw new CoreGatewayProtocolException("protocol.limit-exceeded", "Publication requires too many chunks.");

        var chunkCount = checked((uint)groups.Count);
        var result = new List<StatePublicationChunkV1>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var payload = BuildChunkPayload(publicationId, checked((uint)index), groups[index]);
            var bytes = payload.ToByteArray();
            if (bytes.Length > CoreGatewayProtocolRegistryV1.MaxPublicationChunkBytes)
                throw new CoreGatewayProtocolException("protocol.limit-exceeded", "Publication chunk exceeded 1 MiB after canonical packing.");
            result.Add(new StatePublicationChunkV1
            {
                PublicationId = ByteString.CopyFrom(publicationId.ToBytes()),
                ChunkIndex = checked((uint)index),
                ChunkCount = chunkCount,
                UncompressedPayloadDigest = ByteString.CopyFrom(SHA256.HashData(bytes)),
                Compression = (CompressionKindV1)CoreGatewayProtocolRegistryV1.CompressionNone,
                Payload = ByteString.CopyFrom(bytes),
            });
        }
        return result;
    }

    private static int PayloadSize(OpaqueId128 publicationId, uint index, IReadOnlyList<ProjectionRecordV1> records)
        => BuildChunkPayload(publicationId, index, records).CalculateSize();

    private static ProjectionChunkPayloadV1 BuildChunkPayload(
        OpaqueId128 publicationId,
        uint index,
        IReadOnlyList<ProjectionRecordV1> records)
    {
        var payload = new ProjectionChunkPayloadV1
        {
            PublicationId = ByteString.CopyFrom(publicationId.ToBytes()),
            ChunkIndex = index,
        };
        payload.Records.AddRange(records.Select(static record => record.Clone()));
        return payload;
    }

    private sealed record PublishedAuthoritySnapshotV1(
        WorldStateV1 State,
        byte[] ContinuityToken,
        byte[] ProjectionSchemaDigest);
}

internal sealed class ByteStringLexicographicComparerV1 : IComparer<ByteString>
{
    public static readonly ByteStringLexicographicComparerV1 Instance = new();

    public int Compare(ByteString? x, ByteString? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return x.Span.SequenceCompareTo(y.Span);
    }
}
