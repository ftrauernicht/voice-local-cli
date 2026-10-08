using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class DesktopShortcutPlanTests
{
    [Fact]
    public void BuildsOneShortcutPerOrchestratorMode()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");

        Assert.Equal(
            [OrchestratorMode.Dictate, OrchestratorMode.Live, OrchestratorMode.Call],
            shortcuts.Select(s => s.Mode));
    }

    [Fact]
    public void EachShortcutTargetsDotnetRunAgainstTheUiConsoleProjectWithTheRightMode()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");

        DesktopShortcutDefinition dictate = shortcuts.Single(s => s.Mode == OrchestratorMode.Dictate);

        Assert.Equal(@"C:\dotnet\dotnet.exe", dictate.TargetPath);
        Assert.Equal(@"C:\repo", dictate.WorkingDirectory);
        Assert.Contains(@"src\Cli\VoiceLocalCli.Ui.Console", dictate.Arguments);
        Assert.Contains("--mode dictate", dictate.Arguments);
    }

    [Fact]
    public void EachShortcutPointsAtItsOwnModeSpecificIcon()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");

        Assert.Equal(
            ["dictate.ico", "live.ico", "call.ico"],
            shortcuts.Select(s => Path.GetFileName(s.IconPath)));
    }

    [Fact]
    public void EachShortcutIsWrittenUnderTheSuppliedDesktopDirectory()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(@"C:\repo", @"C:\Users\frank\Desktop", @"C:\dotnet\dotnet.exe");

        Assert.All(shortcuts, s =>
        {
            Assert.StartsWith(@"C:\Users\frank\Desktop", s.ShortcutPath);
            Assert.EndsWith(".lnk", s.ShortcutPath);
        });
    }

    [Theory]
    [InlineData("", @"C:\desktop", @"C:\dotnet.exe")]
    [InlineData(@"C:\repo", "", @"C:\dotnet.exe")]
    [InlineData(@"C:\repo", @"C:\desktop", "")]
    public void RejectsBlankArguments(string repositoryRoot, string desktopDirectory, string dotnetExecutablePath)
    {
        Assert.Throws<ArgumentException>(() =>
            DesktopShortcutPlan.BuildAll(repositoryRoot, desktopDirectory, dotnetExecutablePath));
    }
}
