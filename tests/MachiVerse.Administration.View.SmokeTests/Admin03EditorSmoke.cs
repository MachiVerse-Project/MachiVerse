using System.Runtime.CompilerServices;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Protocol.V1;

internal static class Admin03EditorSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var projection = new ConfigTargetProjection(
            TargetKey: ((ComponentKindV1)2).ToString(),
            ComponentKind: ((ComponentKindV1)2).ToString(),
            LogicalInstanceId: null,
            ConfigGeneration: 7,
            ConfigDigest: new string('a', 64),
            Entries:
            [
                new ConfigEntryProjection(
                    "queue.result-capacity",
                    "{ \"uintValue\": \"8192\" }",
                    "operational",
                    "runtime-safe",
                    Sensitive: false,
                    Redacted: false),
                new ConfigEntryProjection(
                    "auth.oidc.client-secret-ref",
                    null,
                    "operational",
                    "restart-required",
                    Sensitive: true,
                    Redacted: true),
            ],
            Result: new ManagementResultProjection(1, "Success", "ok", 1, "DoNotRetry", string.Empty));

        var editor = new ConfigDraftEditor();
        editor.Begin(projection);
        editor.SetEdit("queue.result-capacity", ConfigDraftValueKind.Uint, "16384");
        Assert(editor.Edits.Count == 1);
        Assert(editor.Edits[0].Value.UintValue == 16384);

        var management = new ManagementProjectionStore(new OperationalCommandCatalog());
        var draft = editor.BuildDraft(management);
        Assert(draft.BaseConfigGeneration == 7);
        Assert(draft.Edits.Count == 1);

        AssertThrows<FormatException>(() =>
            editor.SetEdit("queue.result-capacity", ConfigDraftValueKind.Uint, "-1"));
        AssertThrows<InvalidOperationException>(() =>
            editor.SetEdit("auth.oidc.client-secret-ref", ConfigDraftValueKind.String, "secret-ref"));

        editor.DiscardLocalDraft();
        Assert(editor.Target is null);
        Assert(editor.Edits.Count == 0);
    }

    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("ADMIN-03 editor smoke assertion failed.");
        }
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
