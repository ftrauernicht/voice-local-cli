using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class CreateDesktopShortcutsUseCaseTests
{
    [Fact]
    public void WritesAllThreeShortcutsThroughTheWriter()
    {
        var writer = new FakeShortcutWriter();
        var useCase = new CreateDesktopShortcutsUseCase(writer);

        IReadOnlyList<DesktopShortcutDefinition> result =
            useCase.Run(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");

        Assert.Equal(3, writer.CreateCalls.Count);
        Assert.Equal(result, writer.CreateCalls);
    }

    [Fact]
    public void ReturnsTheSamePlanDesktopShortcutPlanWouldBuild()
    {
        var writer = new FakeShortcutWriter();
        var useCase = new CreateDesktopShortcutsUseCase(writer);

        IReadOnlyList<DesktopShortcutDefinition> result =
            useCase.Run(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");
        IReadOnlyList<DesktopShortcutDefinition> expected =
            DesktopShortcutPlan.BuildAll(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");

        Assert.Equal(expected, result);
    }
}
