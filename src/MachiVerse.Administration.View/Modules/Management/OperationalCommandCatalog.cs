using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

public sealed class OperationalCommandCatalog
{
    private readonly IReadOnlyDictionary<string, CommandDescriptor> _descriptors;

    public OperationalCommandCatalog(IEnumerable<CommandDescriptor>? descriptors = null)
    {
        var map = new Dictionary<string, CommandDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors ?? Array.Empty<CommandDescriptor>())
        {
            ValidateDescriptor(descriptor);
            if (!map.TryAdd(descriptor.CommandKind, descriptor))
            {
                throw new InvalidDataException($"Duplicate operational command kind '{descriptor.CommandKind}'.");
            }
        }

        _descriptors = map;
    }

    public IReadOnlyCollection<CommandDescriptor> Descriptors
        => _descriptors.Values.OrderBy(static x => x.CommandKind, StringComparer.Ordinal).ToArray();

    public bool TryGet(string commandKind, out CommandDescriptor descriptor)
        => _descriptors.TryGetValue(commandKind, out descriptor!);

    public CommandDescriptor Require(string commandKind)
        => TryGet(commandKind, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException(
                $"Operational command '{commandKind}' is not registered. Arbitrary command invocation is forbidden.");

    private static void ValidateDescriptor(CommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireStableToken(descriptor.CommandKind, nameof(descriptor.CommandKind));
        RequireStableToken(descriptor.PayloadSchemaId, nameof(descriptor.PayloadSchemaId));
        RequireStableToken(descriptor.RequiredPermission, nameof(descriptor.RequiredPermission));

        if (descriptor.PayloadSchemaMajor is 0 or > ushort.MaxValue || descriptor.PayloadSchemaMinor > ushort.MaxValue)
        {
            throw new InvalidDataException("Command payload schema version must be within protocol uint16 range and have non-zero major.");
        }

        if (descriptor.AllowedTargetKinds.Count == 0 || descriptor.AllowedTargetKinds.Any(static kind => (int)kind == 0))
        {
            throw new InvalidDataException("Command descriptor must declare at least one concrete target component kind.");
        }
    }

    internal static void RequireStableToken(string value, string fieldName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            throw new InvalidDataException($"{fieldName} must be a StableToken.");
        }

        var first = value[0];
        if (!(first is >= 'a' and <= 'z' || first is >= '0' and <= '9'))
        {
            throw new InvalidDataException($"{fieldName} must be a StableToken.");
        }

        foreach (var ch in value)
        {
            var valid = ch is >= 'a' and <= 'z'
                || ch is >= '0' and <= '9'
                || ch is '.' or '_' or '/' or '-';
            if (!valid)
            {
                throw new InvalidDataException($"{fieldName} must be a StableToken.");
            }
        }
    }
}
