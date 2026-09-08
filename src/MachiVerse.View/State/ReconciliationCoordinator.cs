using MachiVerse.Protocol.V1;

namespace MachiVerse.View.State;

public sealed record PredictionCorrection(
    string OperationId,
    string ReasonCode,
    ulong? ConfirmedBasisStep);

public sealed record PredictionReconciliationTrace(
    ulong Sequence,
    string OperationId,
    string ReasonCode,
    ulong SourceConfirmedBasisStep,
    string SourceConfirmedTokenHex,
    ulong? AwaitingConfirmedThroughStep,
    ulong? ConfirmedBasisStep);

public sealed class ReconciliationCoordinator : IDisposable
{
    private readonly ConfirmedWorldStore _confirmed;
    private readonly PredictionStore _predictions;

    public ReconciliationCoordinator(ConfirmedWorldStore confirmed, PredictionStore predictions)
    {
        _confirmed = confirmed ?? throw new ArgumentNullException(nameof(confirmed));
        _predictions = predictions ?? throw new ArgumentNullException(nameof(predictions));
        _confirmed.Changed += OnConfirmedChanged;
    }

    public ulong CorrectionCount { get; private set; }
    public PredictionReconciliationTrace? LastTrace { get; private set; }

    public event Action<PredictionCorrection>? Corrected;

    public void OnTerminal(ReadOnlySpan<byte> operationId, ResultV1 terminalResult, ulong? effectiveStep)
    {
        ArgumentNullException.ThrowIfNull(terminalResult);
        var status = (int)terminalResult.Status;
        if (status is 6 or 7)
        {
            if (_predictions.TryGet(operationId, out var rejectedPrediction)
                && rejectedPrediction is not null
                && _predictions.Remove(operationId))
            {
                RecordCorrection(
                    rejectedPrediction,
                    string.IsNullOrEmpty(terminalResult.Code) ? "operation.rejected" : terminalResult.Code,
                    _confirmed.Current?.BasisStep);
            }
            return;
        }

        if (status is not (1 or 4 or 5))
            throw new InvalidDataException("Terminal Operation result must use a terminal ResultStatus.");

        _predictions.MarkAwaitingConfirmed(
            operationId,
            effectiveStep,
            string.IsNullOrEmpty(terminalResult.Code) ? "operation.terminal" : terminalResult.Code);
    }

    private void OnConfirmedChanged()
    {
        var snapshot = _confirmed.Current;
        if (snapshot is null) return;

        foreach (var entry in _predictions.Entries)
        {
            var shouldRemove = entry.State switch
            {
                PredictionEntryState.Frozen => true,
                PredictionEntryState.AwaitingConfirmed when !entry.AwaitingConfirmedThroughStep.HasValue => true,
                PredictionEntryState.AwaitingConfirmed when snapshot.BasisStep >= entry.AwaitingConfirmedThroughStep.Value => true,
                _ => false
            };
            if (!shouldRemove) continue;

            if (_predictions.Remove(entry.OperationId))
            {
                RecordCorrection(
                    entry,
                    entry.State == PredictionEntryState.Frozen
                        ? "prediction.rebased-after-confirmed-swap"
                        : "prediction.confirmed-authoritative",
                    snapshot.BasisStep);
            }
        }
    }

    private void RecordCorrection(PredictionEntry entry, string reasonCode, ulong? confirmedBasisStep)
    {
        var sequence = checked(++CorrectionCount);
        LastTrace = new PredictionReconciliationTrace(
            sequence,
            entry.OperationId,
            reasonCode,
            entry.SourceConfirmedBasisStep,
            Convert.ToHexStringLower(entry.SourceConfirmedToken),
            entry.AwaitingConfirmedThroughStep,
            confirmedBasisStep);
        Corrected?.Invoke(new PredictionCorrection(entry.OperationId, reasonCode, confirmedBasisStep));
    }

    public void Dispose() => _confirmed.Changed -= OnConfirmedChanged;
}
