namespace MachiVerse.Gateway.Audit;

public sealed record ProtectedAdminAuditResultV1<T>(
    T Result,
    bool ResultAuditCommitted);

public sealed class ProtectedAdminAuditGateV1
{
    private readonly IAuditWriterV1 _auditWriter;
    private readonly Action<Exception>? _postMutationAuditFailureObserver;

    public ProtectedAdminAuditGateV1(
        IAuditWriterV1 auditWriter,
        Action<Exception>? postMutationAuditFailureObserver = null)
    {
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _postMutationAuditFailureObserver = postMutationAuditFailureObserver;
    }

    public async ValueTask<ProtectedAdminAuditResultV1<T>> ForwardAsync<T>(
        AuditRecordDraftV1 requestAudit,
        Func<CancellationToken, ValueTask<T>> forward,
        Func<T, AuditRecordDraftV1?>? resultAuditFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestAudit);
        ArgumentNullException.ThrowIfNull(forward);

        try
        {
            _ = await _auditWriter.AppendAsync(requestAudit, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProtectedAdminAuditUnavailableException("component.unavailable", ex);
        }

        var result = await forward(cancellationToken).ConfigureAwait(false);
        if (resultAuditFactory is null)
            return new ProtectedAdminAuditResultV1<T>(result, true);

        var resultAudit = resultAuditFactory(result);
        if (resultAudit is null)
            return new ProtectedAdminAuditResultV1<T>(result, true);

        try
        {
            _ = await _auditWriter.AppendAsync(resultAudit, cancellationToken).ConfigureAwait(false);
            return new ProtectedAdminAuditResultV1<T>(result, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _postMutationAuditFailureObserver?.Invoke(new InvalidOperationException("Post-mutation audit was cancelled after downstream completion."));
            return new ProtectedAdminAuditResultV1<T>(result, false);
        }
        catch (Exception ex)
        {
            _postMutationAuditFailureObserver?.Invoke(ex);
            return new ProtectedAdminAuditResultV1<T>(result, false);
        }
    }
}

public sealed class ProtectedAdminAuditUnavailableException : InvalidOperationException
{
    public ProtectedAdminAuditUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
