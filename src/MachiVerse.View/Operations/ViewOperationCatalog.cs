namespace MachiVerse.View.Operations;

public sealed record ViewOperationDescriptor(
    string OperationKind,
    string PayloadSchemaId,
    uint PayloadSchemaMajor,
    uint PayloadSchemaMinor);

public sealed class ViewOperationCatalog
{
    private readonly IReadOnlyDictionary<string, ViewOperationDescriptor> _descriptors;

    public static ViewOperationCatalog Empty { get; } = new(Array.Empty<ViewOperationDescriptor>());

    public ViewOperationCatalog(IEnumerable<ViewOperationDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var map = new Dictionary<string, ViewOperationDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (string.IsNullOrWhiteSpace(descriptor.OperationKind))
                throw new InvalidDataException("View Operation descriptor kind must be non-empty.");
            if (string.IsNullOrWhiteSpace(descriptor.PayloadSchemaId) || descriptor.PayloadSchemaMajor == 0)
                throw new InvalidDataException("View Operation descriptor requires an explicit payload schema.");
            if (!map.TryAdd(descriptor.OperationKind, descriptor))
                throw new InvalidDataException($"Duplicate View Operation descriptor '{descriptor.OperationKind}'.");
        }
        _descriptors = map;
    }

    public IReadOnlyCollection<ViewOperationDescriptor> Descriptors => _descriptors.Values
        .OrderBy(static descriptor => descriptor.OperationKind, StringComparer.Ordinal)
        .ToArray();

    public ViewOperationDescriptor Require(string operationKind)
        => _descriptors.TryGetValue(operationKind, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Unregistered View OperationKind '{operationKind}' is fail-closed.");

    public void Validate(ViewOperationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var descriptor = Require(draft.OperationKind);
        if (!string.Equals(descriptor.PayloadSchemaId, draft.PayloadSchemaId, StringComparison.Ordinal)
            || descriptor.PayloadSchemaMajor != draft.PayloadSchemaMajor
            || descriptor.PayloadSchemaMinor != draft.PayloadSchemaMinor)
        {
            throw new InvalidDataException("View Operation payload schema does not match its registered descriptor.");
        }
    }
}
