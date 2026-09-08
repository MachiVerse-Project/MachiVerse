using System.Globalization;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

public enum ConfigDraftValueKind
{
    Bool,
    Int,
    Uint,
    Double,
    String,
    BytesBase64,
}

public sealed class ConfigDraftEditor
{
    private readonly Dictionary<string, ConfigChangeEdit> _edits = new(StringComparer.Ordinal);

    public ConfigTargetProjection? Target { get; private set; }

    public IReadOnlyList<ConfigChangeEdit> Edits
        => _edits.Values.OrderBy(static edit => edit.Key, StringComparer.Ordinal).ToArray();

    public void Begin(ConfigTargetProjection target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ConfigGeneration == 0)
        {
            throw new InvalidOperationException("A local draft requires a confirmed ConfigGeneration.");
        }

        Target = target;
        _edits.Clear();
    }

    public void SetEdit(string key, ConfigDraftValueKind kind, string text)
    {
        var target = Target ?? throw new InvalidOperationException("Begin a Config draft before editing values.");
        var field = target.Entries.SingleOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Config key '{key}' is not present in the current projection.");
        if (field.Sensitive)
        {
            throw new InvalidOperationException("Sensitive Config fields cannot be edited from a redacted projection.");
        }

        _edits[key] = new ConfigChangeEdit(key, Parse(kind, text));
    }

    public void RemoveEdit(string key)
        => _edits.Remove(key);

    public ConfigChangeDraft BuildDraft(IManagementModuleBoundary management)
    {
        ArgumentNullException.ThrowIfNull(management);
        var target = Target ?? throw new InvalidOperationException("Begin a Config draft before building it.");
        return management.CreateDraft(target).WithEdits(Edits);
    }

    public void DiscardLocalDraft()
    {
        Target = null;
        _edits.Clear();
    }

    private static ConfigValueWireV1 Parse(ConfigDraftValueKind kind, string text)
        => kind switch
        {
            ConfigDraftValueKind.Bool when bool.TryParse(text, out var value) => new ConfigValueWireV1 { BoolValue = value },
            ConfigDraftValueKind.Int when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => new ConfigValueWireV1 { IntValue = value },
            ConfigDraftValueKind.Uint when ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => new ConfigValueWireV1 { UintValue = value },
            ConfigDraftValueKind.Double when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) => new ConfigValueWireV1 { DoubleValue = value },
            ConfigDraftValueKind.String => new ConfigValueWireV1 { StringValue = text },
            ConfigDraftValueKind.BytesBase64 => new ConfigValueWireV1 { BytesValue = ParseBase64(text) },
            _ => throw new FormatException($"Value '{text}' is invalid for Config type {kind}.")
        };

    private static ByteString ParseBase64(string text)
    {
        try
        {
            return ByteString.CopyFrom(Convert.FromBase64String(text));
        }
        catch (FormatException ex)
        {
            throw new FormatException("Bytes Config value must be valid Base64.", ex);
        }
    }
}
