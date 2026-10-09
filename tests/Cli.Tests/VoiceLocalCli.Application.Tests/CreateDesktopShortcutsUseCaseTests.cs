using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class CreateDesktopShortcutsUseCaseTests
{
    private static readonly DesktopShortcutLaunchTarget LaunchTarget = new(
        TargetPath: @"C:\dotnet\dotnet.exe",
        ArgumentsPrefix: @"run --project ""C:\repo\src\Cli\VoiceLocalCli.Ui.Console"" --",
        WorkingDirectory: @"C:\repo");

    [Fact]
    public void WritesAllThreeShortcutsThroughTheWriter()
    {
        var writer = new FakeShortcutWriter();
        var useCase = new CreateDesktopShortcutsUseCase(writer);

        IReadOnlyList<DesktopShortcutDefinition> result =
            useCase.Run(LaunchTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");

        Assert.Equal(3, writer.CreateCalls.Count);
        Assert.Equal(result, writer.CreateCalls);
    }

    [Fact]
    public void ReturnsTheSamePlanDesktopShortcutPlanWouldBuild()
    {
        var writer = new FakeShortcutWriter();
        var useCase = new CreateDesktopShortcutsUseCase(writer);

        IReadOnlyList<DesktopShortcutDefinition> result =
            useCase.Run(LaunchTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");
        IReadOnlyList<DesktopShortcutDefinition> expected =
            DesktopShortcutPlan.BuildAll(LaunchTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");

        Assert.Equal(expected, result);
    }
}
