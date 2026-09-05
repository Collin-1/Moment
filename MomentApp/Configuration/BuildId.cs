namespace MomentApp.Configuration;

/// <summary>
/// A short token that changes on every build, used to version client assets.
/// </summary>
/// <remarks>
/// Derived from the assembly's Module Version Id, which the compiler regenerates on each
/// compile. That means no build tooling, no environment variable and no git dependency — the
/// value is simply correct, including in the Docker image.
/// </remarks>
public static class BuildId
{
    public static readonly string Value =
        typeof(Program).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..10];
}
