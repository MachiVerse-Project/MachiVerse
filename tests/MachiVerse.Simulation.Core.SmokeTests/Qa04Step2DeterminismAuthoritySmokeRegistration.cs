using System.Runtime.CompilerServices;

internal static class Qa04Step2DeterminismAuthoritySmokeRegistration
{
    [ModuleInitializer]
    internal static void Register() => Qa04Step2DeterminismAuthoritySmoke.Run();
}
