using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Microsoft.Win32;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public sealed class RegistryStartupRegistrationTests : IDisposable
{
    private readonly string _root = @"Software\LoadstoneTests\" + Guid.NewGuid().ToString("N");
    private string RunKey => _root + @"\Run";
    private string ApprovedKey => _root + @"\StartupApproved";
    private const string Exe = @"C:\Program Files\Loadstone\Loadstone.exe";

    private RegistryStartupRegistration NewRegistration(string exe = Exe) => new(RunKey, ApprovedKey, exe);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Status_WithoutAValue_IsNotRegistered()
    {
        NewRegistration().Status.ShouldBe(StartupStatus.NotRegistered);
    }

    [Fact]
    public void Enable_WritesTheQuotedExePath_AndStatusIsEnabled()
    {
        var registration = NewRegistration();
        registration.Enable();

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe($"\"{Exe}\"");
        registration.Status.ShouldBe(StartupStatus.Enabled);
    }

    [Fact]
    public void Disable_RemovesTheValue()
    {
        var registration = NewRegistration();
        registration.Enable();
        registration.Disable();
        registration.Status.ShouldBe(StartupStatus.NotRegistered);
        Should.NotThrow(registration.Disable);
    }

    [Fact]
    public void Status_WhenTaskManagerDisabledIt_SaysSo()
    {
        var registration = NewRegistration();
        registration.Enable();
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue("Loadstone", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        registration.Status.ShouldBe(StartupStatus.DisabledInTaskManager);
    }

    [Fact]
    public void Enable_ClearsTaskManagersDisable()
    {
        var registration = NewRegistration();
        registration.Enable();
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue("Loadstone", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        registration.Enable();
        registration.Status.ShouldBe(StartupStatus.Enabled);
    }

    [Fact]
    public void RefreshPathIfStale_RewritesAMovedExe_AndLeavesAnUnregisteredEntryAlone()
    {
        NewRegistration(@"C:\old\Loadstone.exe").Enable();
        var moved = NewRegistration(@"C:\new\Loadstone.exe");
        moved.RefreshPathIfStale();
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe("\"C:\\new\\Loadstone.exe\"");

        moved.Disable();
        moved.RefreshPathIfStale();
        moved.Status.ShouldBe(StartupStatus.NotRegistered);
    }
}
