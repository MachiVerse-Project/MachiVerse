namespace MachiVerse.Administration.View.Modules.Management;

public sealed class AdminOperationCatalog
{
    private readonly IReadOnlyDictionary<string, AdminOperationDescriptor> _descriptors;

    public AdminOperationCatalog(IEnumerable<AdminOperationDescriptor>? descriptors = null)
    {
        var map = new Dictionary<string, AdminOperationDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors ?? Array.Empty<AdminOperationDescriptor>())
        {
            Validate(descriptor);
            if (!map.TryAdd(descriptor.OperationKind, descriptor))
            {
                throw new InvalidDataException($"Duplicate Admin operation kind '{descriptor.OperationKind}'.");
            }
        }
        _descriptors = map;
    }

    public IReadOnlyCollection<AdminOperationDescriptor> Descriptors
        => _descriptors.Values.OrderBy(static value => value.OperationKind, StringComparer.Ordinal).ToArray();

    public AdminOperationDescriptor Require(string operationKind)
        => _descriptors.TryGetValue(operationKind, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException(
                $"Admin operation '{operationKind}' is not registered. Undefined simulation operations are forbidden.");

    private static void Validate(AdminOperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        AdminSessionProjectionStore.ValidateStableToken(descriptor.OperationKind, nameof(descriptor.OperationKind));
        AdminSessionProjectionStore.ValidateStableToken(descriptor.PayloadSchemaId, nameof(descriptor.PayloadSchemaId));
        AdminSessionProjectionStore.ValidateStableToken(descriptor.RequiredPermission, nameof(descriptor.RequiredPermission));
        if (descriptor.PayloadSchemaMajor is 0 or > ushort.MaxValue || descriptor.PayloadSchemaMinor > ushort.MaxValue)
        {
            throw new InvalidDataException("Admin operation payload schema version is outside protocol range.");
        }
        if (!string.Equals(descriptor.RequiredPermission, AdminPermissionTokens.OperationSubmit, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Simulation Admin Operation descriptors must use canonical admin.operation.submit permission.");
        }
    }
}
