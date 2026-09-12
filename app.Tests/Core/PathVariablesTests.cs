using System.IO;
using Clockwork.Core;
using Xunit;

public class PathVariablesTests
{
    [Fact]
    public void ToPortable_converts_user_desktop_path()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var testPath = Path.Combine(userProfile, "Desktop", "myapp.exe");
        var got = PathVariables.ToPortable(testPath);
        Assert.Equal(@"%USERPROFILE%\Desktop\myapp.exe", got);
    }

    [Fact]
    public void ToPortable_converts_appdata_roaming_path()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var testPath = Path.Combine(appData, "myconfig.json");
        var got = PathVariables.ToPortable(testPath);
        Assert.Equal(@"%APPDATA%\myconfig.json", got);
    }

    [Fact]
    public void ToPortable_converts_local_appdata_path()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var testPath = Path.Combine(localAppData, "test.log");
        var got = PathVariables.ToPortable(testPath);
        Assert.Equal(@"%LOCALAPPDATA%\test.log", got);
    }

    [Fact]
    public void ToPortable_converts_program_data_path()
    {
        var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var testPath = Path.Combine(progData, "shared.db");
        var got = PathVariables.ToPortable(testPath);
        Assert.Equal(@"%PROGRAMDATA%\shared.db", got);
    }

    [Fact]
    public void ToPortable_leaves_unrelated_path_intact()
    {
        const string p = @"D:\Games\Launcher.exe";
        Assert.Equal(p, PathVariables.ToPortable(p));
    }

    [Fact]
    public void ToPortable_leaves_already_portable_token_intact()
    {
        const string p = @"%USERPROFILE%\Downloads\file.zip";
        Assert.Equal(p, PathVariables.ToPortable(p));
    }

    [Fact]
    public void Resolve_expands_valid_environment_variables()
    {
        var resolved = PathVariables.Resolve(@"%USERPROFILE%\Desktop", out var unres);
        Assert.False(unres);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"), resolved);
    }

    [Fact]
    public void Resolve_detects_unresolved_variable_name()
    {
        var resolved = PathVariables.Resolve(@"%NON_EXISTENT_VAR_CLOCKWORK_TEST%\folder", out var unres);
        Assert.True(unres);
        Assert.Contains("%NON_EXISTENT_VAR_CLOCKWORK_TEST%", resolved);
    }

    [Fact]
    public void GetCommonFolders_returns_expected_entries()
    {
        var list = PathVariables.GetCommonFolders(isZh: true);
        Assert.NotEmpty(list);
        Assert.Contains(list, x => x.Expression == @"%USERPROFILE%\Desktop");
        Assert.Contains(list, x => x.Expression == @"%USERPROFILE%\Downloads");
        Assert.Contains(list, x => x.Expression.Contains("Startup"));
    }

    [Fact]
    public void GetSystemVariables_returns_expected_entries()
    {
        var list = PathVariables.GetSystemVariables(isZh: true);
        Assert.NotEmpty(list);
        Assert.Contains(list, x => x.Expression == "%USERPROFILE%");
        Assert.Contains(list, x => x.Expression == "%APPDATA%");
        Assert.Contains(list, x => x.Expression == "%TEMP%");
    }
}
