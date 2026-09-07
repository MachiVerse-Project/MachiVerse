using MachiVerse.Protocol.V1;

namespace MachiVerse.View.State;

public sealed record PredictionCorrection(
    string OperationId,
    string ReasonCode,
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

    public event Action<PredictionCorrection>? Corrected;

    public void OnTerminal(ReadOnlySpan<byte> operationId, ResultV1 terminalResult, ulong? effectiveStep)
    {
        ArgumentNullException.ThrowIfNull(terminalResult);
        var status = (int)terminalResult.Status;
        if (status is 6 or 7)
        {
            if (_predictions.Remove(operationId))
            {
                Corrected?.Invoke(new PredictionCorrection(
                    Convert.ToHexStringLower(operationId),
                    terminalResult.Code,
                    _confirmed.Current?.BasisStep));
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
                Corrected?.Invoke(new PredictionCorrection(
                    entry.OperationId,
                    entry.State == PredictionEntryState.Frozen
                        ? "prediction.rebased-after-confirmed-swap"
                        : "prediction.confirmed-authoritative",
                    snapshot.BasisStep));
            }
        }
    }

    public void Dispose() => _confirmed.Changed -= OnConfirmedChanged;
}
