namespace VoiceLocalCli.Architecture.Tests;

/// <summary>
/// Guards the governance itself: these tests fail if someone removes a shared quality gate. They
/// walk up from the test binary to the repo root and assert the key settings are still present.
/// </summary>
public sealed class BuildSettingsTests
{
    [Fact]
    public void WarningsAreErrorsAndCodeStyleIsEnforced()
    {
        string props = ReadRepoFile("Directory.Build.props");

        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", props);
        Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", props);
    }

    [Fact]
    public void PackageVersionsAreManagedCentrally()
    {
        string props = ReadRepoFile("Directory.Packages.props");

        Assert.Contains("<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>", props);
    }

    [Fact]
    public void NuGetSourcesAreCleared()
    {
        string config = ReadRepoFile("nuget.config");

        Assert.Contains("<clear />", config);
    }

    [Fact]
    public void TheSdkIsPinned()
    {
        string globalJson = ReadRepoFile("global.json");

        Assert.Contains("\"version\"", globalJson);
        Assert.Contains("\"rollForward\"", globalJson);
    }

    private static string ReadRepoFile(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {fileName} in any parent directory.");
    }
}
