namespace MachiVerse.View.State;

public enum PredictionEntryState
{
    Active,
    Frozen,
    AwaitingConfirmed
}

public sealed record PredictionEntry(
    string OperationId,
    string SemanticTarget,
    ulong SourceConfirmedBasisStep,
    byte[] SourceConfirmedToken,
    ulong LocalPredictionEpoch,
    byte[] PredictedPayload,
    PredictionEntryState State,
    ulong? AwaitingConfirmedThroughStep,
    string? LastReasonCode);

public sealed class PredictionStore
{
    private readonly Dictionary<string, PredictionEntry> _entries = new(StringComparer.Ordinal);
    private ulong _nextEpoch;

    public IReadOnlyList<PredictionEntry> Entries => _entries.Values
        .OrderBy(static entry => entry.OperationId, StringComparer.Ordinal)
        .Select(Clone)
        .ToArray();

    public event Action? Changed;

    public PredictionEntry Add(
        ReadOnlySpan<byte> operationId,
        string semanticTarget,
        ConfirmedWorldSnapshot source,
        ReadOnlySpan<byte> predictedPayload)
    {
        ValidateId128(operationId, nameof(operationId));
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(semanticTarget))
            throw new InvalidDataException("Prediction semantic target must be non-empty.");
        if (source.ContinuityToken.Length == 0)
            throw new InvalidDataException("Prediction source requires a confirmed continuity token reference.");

        var key = Convert.ToHexStringLower(operationId);
        if (_entries.TryGetValue(key, out var existing))
        {
            if (!string.Equals(existing.SemanticTarget, semanticTarget, StringComparison.Ordinal)
                || existing.SourceConfirmedBasisStep != source.BasisStep
                || !existing.SourceConfirmedToken.AsSpan().SequenceEqual(source.ContinuityToken)
                || !existing.PredictedPayload.AsSpan().SequenceEqual(predictedPayload))
            {
                throw new InvalidDataException("Same OperationId cannot be rebound to a different local prediction.");
            }
            return Clone(existing);
        }

        var entry = new PredictionEntry(
            key,
            semanticTarget,
            source.BasisStep,
            source.ContinuityToken.ToArray(),
            checked(++_nextEpoch),
            predictedPayload.ToArray(),
            PredictionEntryState.Active,
            AwaitingConfirmedThroughStep: null,
            LastReasonCode: null);
        _entries.Add(key, entry);
        Changed?.Invoke();
        return Clone(entry);
    }

    public bool TryGet(ReadOnlySpan<byte> operationId, out PredictionEntry? entry)
    {
        ValidateId128(operationId, nameof(operationId));
        if (_entries.TryGetValue(Convert.ToHexStringLower(operationId), out var found))
        {
            entry = Clone(found);
            return true;
        }
        entry = null;
        return false;
    }

    public void FreezeAll(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("Reason code must be non-empty.", nameof(reasonCode));

        var changed = false;
        foreach (var key in _entries.Keys.ToArray())
        {
            var entry = _entries[key];
            if (entry.State == PredictionEntryState.Frozen && string.Equals(entry.LastReasonCode, reasonCode, StringComparison.Ordinal))
                continue;
            _entries[key] = entry with { State = PredictionEntryState.Frozen, LastReasonCode = reasonCode };
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    public void MarkAwaitingConfirmed(ReadOnlySpan<byte> operationId, ulong? throughStep, string reasonCode)
    {
        ValidateId128(operationId, nameof(operationId));
        var key = Convert.ToHexStringLower(operationId);
        if (!_entries.TryGetValue(key, out var entry)) return;
        _entries[key] = entry with
        {
            State = PredictionEntryState.AwaitingConfirmed,
            AwaitingConfirmedThroughStep = throughStep,
            LastReasonCode = reasonCode
        };
        Changed?.Invoke();
    }

    public bool Remove(ReadOnlySpan<byte> operationId)
    {
        ValidateId128(operationId, nameof(operationId));
        var removed = _entries.Remove(Convert.ToHexStringLower(operationId));
        if (removed) Changed?.Invoke();
        return removed;
    }

    public bool Remove(string operationIdHex)
    {
        var removed = _entries.Remove(operationIdHex);
        if (removed) Changed?.Invoke();
        return removed;
    }

    private static PredictionEntry Clone(PredictionEntry entry)
        => entry with
        {
            SourceConfirmedToken = entry.SourceConfirmedToken.ToArray(),
            PredictedPayload = entry.PredictedPayload.ToArray()
        };

    private static void ValidateId128(ReadOnlySpan<byte> value, string field)
    {
        if (value.Length != 16) throw new InvalidDataException($"{field} must be Id128.");
        var allZero = true;
        foreach (var octet in value)
        {
            if (octet == 0) continue;
            allZero = false;
            break;
        }
        if (allZero) throw new InvalidDataException($"{field} must not be ZERO.");
    }
}
