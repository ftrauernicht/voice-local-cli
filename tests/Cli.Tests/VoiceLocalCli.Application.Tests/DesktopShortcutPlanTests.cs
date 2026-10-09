using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class DesktopShortcutPlanTests
{
    private static readonly DesktopShortcutLaunchTarget DevModeTarget = new(
        TargetPath: @"C:\dotnet\dotnet.exe",
        ArgumentsPrefix: @"run --project ""C:\repo\src\Cli\VoiceLocalCli.Ui.Console"" --",
        WorkingDirectory: @"C:\repo");

    private static readonly DesktopShortcutLaunchTarget InstalledModeTarget = new(
        TargetPath: @"C:\Users\frank\AppData\Local\VoiceLocalCli\current\VoiceLocalCli.exe",
        ArgumentsPrefix: string.Empty,
        WorkingDirectory: @"C:\Users\frank\AppData\Local\VoiceLocalCli\current");

    [Fact]
    public void BuildsOneShortcutPerOrchestratorMode()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(DevModeTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");

        Assert.Equal(
            [OrchestratorMode.Dictate, OrchestratorMode.Live, OrchestratorMode.Call],
            shortcuts.Select(s => s.Mode));
    }

    [Fact]
    public void DevModeTargetsDotnetRunAgainstTheUiConsoleProjectWithTheRightMode()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(DevModeTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");

        DesktopShortcutDefinition dictate = shortcuts.Single(s => s.Mode == OrchestratorMode.Dictate);

        Assert.Equal(@"C:\dotnet\dotnet.exe", dictate.TargetPath);
        Assert.Equal(@"C:\repo", dictate.WorkingDirectory);
        Assert.Contains(@"src\Cli\VoiceLocalCli.Ui.Console", dictate.Arguments);
        Assert.Contains("--mode dictate", dictate.Arguments);
    }

    [Fact]
    public void InstalledModeTargetsTheExeDirectlyWithNoDotnetRunWrapper()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(InstalledModeTarget, @"C:\Users\frank\Desktop", @"C:\install\icons");

        DesktopShortcutDefinition call = shortcuts.Single(s => s.Mode == OrchestratorMode.Call);

        Assert.Equal(@"C:\Users\frank\AppData\Local\VoiceLocalCli\current\VoiceLocalCli.exe", call.TargetPath);
        Assert.Equal("--mode call", call.Arguments);
        Assert.DoesNotContain("dotnet", call.Arguments);
    }

    [Fact]
    public void EachShortcutPointsAtItsOwnModeSpecificIconUnderTheSuppliedIconsDirectory()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(DevModeTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");

        Assert.All(shortcuts, s => Assert.StartsWith(@"C:\repo\icons", s.IconPath));
        Assert.Equal(
            ["dictate.ico", "live.ico", "call.ico"],
            shortcuts.Select(s => Path.GetFileName(s.IconPath)));
    }

    [Fact]
    public void EachShortcutIsWrittenUnderTheSuppliedDesktopDirectory()
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(DevModeTarget, @"C:\Users\frank\Desktop", @"C:\repo\icons");

        Assert.All(shortcuts, s =>
        {
            Assert.StartsWith(@"C:\Users\frank\Desktop", s.ShortcutPath);
            Assert.EndsWith(".lnk", s.ShortcutPath);
        });
    }

    [Fact]
    public void RejectsNullLaunchTarget()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DesktopShortcutPlan.BuildAll(null!, @"C:\desktop", @"C:\icons"));
    }

    [Theory]
    [InlineData("", @"C:\working", @"C:\desktop", @"C:\icons")]
    [InlineData(@"C:\dotnet.exe", "", @"C:\desktop", @"C:\icons")]
    public void RejectsBlankLaunchTargetFields(string targetPath, string workingDirectory, string desktopDirectory, string iconsDirectory)
    {
        var launchTarget = new DesktopShortcutLaunchTarget(targetPath, string.Empty, workingDirectory);

        Assert.Throws<ArgumentException>(() =>
            DesktopShortcutPlan.BuildAll(launchTarget, desktopDirectory, iconsDirectory));
    }

    [Theory]
    [InlineData("", @"C:\icons")]
    [InlineData(@"C:\desktop", "")]
    public void RejectsBlankDesktopOrIconsDirectory(string desktopDirectory, string iconsDirectory)
    {
        Assert.Throws<ArgumentException>(() =>
            DesktopShortcutPlan.BuildAll(DevModeTarget, desktopDirectory, iconsDirectory));
    }
}
