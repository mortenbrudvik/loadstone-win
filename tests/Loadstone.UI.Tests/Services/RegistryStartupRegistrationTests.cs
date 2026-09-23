using System.IO;
using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Microsoft.Win32;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public sealed class RegistryStartupRegistrationTests : IDisposable
{
    private const string Parent = @"Software\LoadstoneTests";
    private readonly string _root = Parent + @"\" + Guid.NewGuid().ToString("N");
    private string RunKey => _root + @"\Run";
    private string ApprovedKey => _root + @"\StartupApproved";
    private static readonly string Exe = Environment.ProcessPath ?? throw new InvalidOperationException("the test host has no path");

    private RegistryStartupRegistration NewRegistration(string? exe = null) => new(RunKey, ApprovedKey, exe ?? Exe);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

        // Leave no residue in the user's registry, but only when this run's last test key is gone: a
        // parent holding anything else is not ours to delete.
        using (var parent = Registry.CurrentUser.OpenSubKey(Parent))
        {
            if (parent is null || parent.SubKeyCount != 0 || parent.ValueCount != 0)
            {
                return;
            }
        }

        try
        {
            Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Another test - in this class, or in a second run of the suite alongside this one -
            // created its own key under the parent between the check above and this delete, and
            // DeleteSubKey refuses a key that has subkeys. Leaving the parent behind is harmless;
            // failing somebody else's teardown is not.
        }
    }

    [Fact]
    public void Status_WhenTheExeIsGone_IsUnknown()
    {
        var registration = NewRegistration();
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue("Loadstone", @"C:\no\such\Loadstone.exe");
        }

        registration.Status.ShouldBe(StartupStatus.Unknown);
    }

    [Fact]
    public void Status_WithoutAValue_IsNotRegistered()
    {
        NewRegistration().Status.ShouldBe(StartupStatus.NotRegistered);
    }

    [Fact]
    public async Task Enable_WritesTheQuotedExePath_AndStatusIsEnabled()
    {
        var registration = NewRegistration();
        (await registration.EnableAsync()).ShouldBeTrue();

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe($"\"{Exe}\"");
        registration.Status.ShouldBe(StartupStatus.Enabled);
    }

    [Fact]
    public async Task Disable_RemovesTheValue()
    {
        var registration = NewRegistration();
        (await registration.EnableAsync()).ShouldBeTrue();
        (await registration.DisableAsync()).ShouldBeTrue();
        registration.Status.ShouldBe(StartupStatus.NotRegistered);

        // Unregistering something already unregistered is not a failure.
        (await registration.DisableAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Status_WhenTaskManagerDisabledIt_SaysSo()
    {
        var registration = NewRegistration();
        await registration.EnableAsync();
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue("Loadstone", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        registration.Status.ShouldBe(StartupStatus.DisabledInTaskManager);
    }

    [Fact]
    public async Task Enable_ClearsTaskManagersDisable()
    {
        var registration = NewRegistration();
        await registration.EnableAsync();
        using (var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey))
        {
            approved.SetValue("Loadstone", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        (await registration.EnableAsync()).ShouldBeTrue();
        registration.Status.ShouldBe(StartupStatus.Enabled);
    }

    // The registry-denied path (Status reporting Unknown, and both writes reporting false rather
    // than throwing) is deliberately NOT tested here. Proving it at this level means putting a Deny
    // ACE on a key in the developer's own HKCU hive and then taking it off again, and that removal
    // runs through the very denial it just created: when the restore fails, the run leaves behind a
    // key its owner can no longer open or delete. A unit suite should not be able to do that to the
    // machine it runs on. What actually matters - that a tray click survives an unreadable registry
    // instead of ending the process - is pinned at the consuming seam in SettingsViewModelTests,
    // which substitutes IStartupRegistration and needs no real ACL.

    /// <summary>
    /// InitializeAsync rewrites an entry that is stale; it does not create one. Symptom if this
    /// regresses: unticking Launch at login and relaunching puts the login item straight back.
    /// The stale-rewrite half this test used to assert as well is the subject of
    /// <see cref="InitializeAsync_RewritesAPathThatIsNoLongerThere"/>, so it is asserted there and
    /// only set up here.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WhenTheEntryWasRemoved_DoesNotPutItBack()
    {
        var registration = NewRegistration(exe: @"C:\gone\Loadstone.exe");
        await registration.EnableAsync();
        await registration.DisableAsync();

        await registration.InitializeAsync();

        registration.Status.ShouldBe(StartupStatus.NotRegistered);
    }

    /// <summary>
    /// A second copy on disk was registered on purpose. Rewriting on any difference would repoint the
    /// user's login item at whichever build happens to run, a build output directory included.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WhenTheRegisteredPathStillExists_LeavesItAlone()
    {
        var stillThere = Environment.ProcessPath.ShouldNotBeNull();
        await NewRegistration(stillThere).EnableAsync();

        await NewRegistration(@"C:\new\Loadstone.exe").InitializeAsync();

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe($"\"{stillThere}\"");
    }

    /// <summary>
    /// InitializeAsync is what App calls on every launch; it replaced RefreshPathIfStale on the
    /// interface so that a packaged implementation can do its own asynchronous setup there.
    /// Symptom if this regresses: an installed copy whose exe moved loses its login item.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RewritesAPathThatIsNoLongerThere()
    {
        var registration = NewRegistration(exe: @"C:\gone\Loadstone.exe");
        await registration.EnableAsync();

        await NewRegistration().InitializeAsync();

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe($"\"{Exe}\"");
    }
}
