using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;
using VoiceLocalCli.Infrastructure;

namespace VoiceLocalCli.Architecture.Tests;

/// <summary>
/// Enforces the Dependency Rule at build time: inner layers must not reference outer
/// ones, and only the composition root (VoiceLocalCli.Ui.Console, not referenced from
/// here on purpose -- it's the one project nothing else should know about) may depend on
/// Spectre.Console. These tests read each assembly's referenced-assembly list and fail if
/// a forbidden edge appears.
/// </summary>
public sealed class DependencyRuleTests
{
    [Fact]
    public void DomainReferencesNoOuterProjectOrUiFramework()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(DictationPhase));

        Assert.DoesNotContain(referenced, name =>
            name.StartsWith("VoiceLocalCli.Application", StringComparison.Ordinal)
            || name.StartsWith("VoiceLocalCli.Infrastructure", StringComparison.Ordinal)
            || name.StartsWith("VoiceLocalCli.Ui", StringComparison.Ordinal)
            || name.StartsWith("Spectre.Console", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureUiOrSpectreConsole()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(DictationSession));

        Assert.DoesNotContain(referenced, name =>
            name.StartsWith("VoiceLocalCli.Infrastructure", StringComparison.Ordinal)
            || name.StartsWith("VoiceLocalCli.Ui", StringComparison.Ordinal)
            || name.StartsWith("Spectre.Console", StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastructureDoesNotReferenceUiOrSpectreConsole()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(RealProcessLauncher));

        Assert.DoesNotContain(referenced, name =>
            name.StartsWith("VoiceLocalCli.Ui", StringComparison.Ordinal)
            || name.StartsWith("Spectre.Console", StringComparison.Ordinal));
    }

    private static string[] ReferencedAssemblyNames(Type typeFromAssembly) =>
        [.. typeFromAssembly.Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)];
}
