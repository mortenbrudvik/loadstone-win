using System.IO;
using Loadstone.Core.Commands;
using Loadstone.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public sealed class AboutViewModelTests : IDisposable
{
    private readonly List<string> _opened = [];
    private readonly List<string> _copied = [];
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "LoadstoneTests", Guid.NewGuid().ToString("N"));
    private string _logDirectory;
    private bool _shellOpens = true;
    private bool _clipboardTakes = true;
    private Func<TimeSpan, Task> _pause = _ => Task.CompletedTask;
    private int _beeps;

    public AboutViewModelTests()
    {
        _logDirectory = Path.Combine(_scratch, "Logs");
    }

    /// <summary>
    /// The internal constructor, so no test ever hands a URI to the shell or writes the clipboard:
    /// through the public one, a click starts a browser, Explorer on the real log folder, or
    /// overwrites whatever the person running the tests had copied.
    /// </summary>
    private AboutViewModel NewViewModel() => new(
        command => command.DefaultShortcut(),
        NullLogger<AboutViewModel>.Instance,
        uri =>
        {
            _opened.Add(uri);
            return _shellOpens;
        },
        () => _beeps++,
        _logDirectory,
        text =>
        {
            _copied.Add(text);
            return _clipboardTakes;
        },
        span => _pause(span));

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    /// <summary>
    /// The tab shows a version, not a commit: the informational version carries a "+sha" suffix on any
    /// build with source link, and the split is the only thing that keeps it off the tab.
    /// </summary>
    [Fact]
    public void Version_IsTheInformationalVersionWithoutItsBuildMetadata()
    {
        var version = NewViewModel().Version;

        version.ShouldNotBeNullOrWhiteSpace();
        version.ShouldNotContain("+");
        version.ShouldNotBe("?");
    }

    /// <summary>Read from the assembly the project file stamps, so the tab cannot say one thing and the file's properties another.</summary>
    [Fact]
    public void Copyright_IsTheOneTheBuildStamps()
    {
        var copyright = NewViewModel().Copyright;

        copyright.ShouldStartWith("Copyright");
        RepoFile.Text("src/Loadstone.UI/Loadstone.UI.csproj").ShouldContain($"<Copyright>{copyright}</Copyright>");
    }

    [Fact]
    public void EachLink_HandsItsAddressToTheShell()
    {
        var vm = NewViewModel();

        vm.OpenSourceCodeCommand.Execute(null);
        vm.ReportProblemCommand.Execute(null);
        vm.RateInStoreCommand.Execute(null);
        vm.OpenHomepageCommand.Execute(null);
        vm.OpenMacAppCommand.Execute(null);

        _opened.ShouldBe([AboutViewModel.SourceCodeUrl, AboutViewModel.IssuesUrl, AboutViewModel.StoreReviewUri, AboutViewModel.HomepageUrl, AboutViewModel.MacAppUrl]);
        AboutViewModel.HomepageUrl.ShouldBe("https://brudvik.com");
        _beeps.ShouldBe(0);
        vm.Message.ShouldBeEmpty();
    }

    /// <summary>
    /// The Store address is the one a user installs from. The README's badge is the only other place
    /// the product id is written, and a Store listing that moved would be fixed there first.
    /// </summary>
    [Fact]
    public void StoreReviewUri_NamesTheProductTheReadmeInstalls()
    {
        var id = AboutViewModel.StoreReviewUri[(AboutViewModel.StoreReviewUri.LastIndexOf('=') + 1)..];

        id.ShouldNotBeNullOrWhiteSpace();
        RepoFile.Text("README.md").ShouldContain($"get.microsoft.com/installer/download/{id}");
    }

    /// <summary>
    /// A link that nothing handled beeps, and says where it pointed: the address is what the user was
    /// after, and the caption is what a screen reader announces.
    /// </summary>
    [Fact]
    public void ALink_WhenTheShellRefuses_BeepsAndSaysWhereItPointed()
    {
        _shellOpens = false;

        var vm = NewViewModel();
        vm.ReportProblemCommand.Execute(null);

        _beeps.ShouldBe(1);
        vm.Message.ShouldContain(AboutViewModel.IssuesUrl);
    }

    /// <summary>
    /// NLog creates the log directory on first write, so on a run where logging failed - exactly when
    /// a user is sent looking - it is not there yet. The command creates it first, so the shell has
    /// something to open rather than beeping. The path itself is the literal %LOCALAPPDATA%\Loadstone\Logs
    /// in both channels - MSIX does not redirect file-system writes for this package - so the button
    /// is a convenience, not the only honest way to name it.
    /// </summary>
    [Fact]
    public void OpenLogFolder_CreatesTheDirectory_ThenHandsItToTheShell()
    {
        Directory.Exists(_logDirectory).ShouldBeFalse();

        NewViewModel().OpenLogFolderCommand.Execute(null);

        Directory.Exists(_logDirectory).ShouldBeTrue();
        _opened.ShouldBe([_logDirectory]);
        _beeps.ShouldBe(0);
    }

    /// <summary>
    /// A creation that is refused must not escape: a throw from a button click on the UI thread ends
    /// the process and takes the hotkeys and the user's edge snapping with it. The shell still gets
    /// the path, and answers for itself.
    /// </summary>
    [Fact]
    public void OpenLogFolder_StillHandsThePathToTheShell_WhenTheDirectoryCannotBeCreated()
    {
        _logDirectory = Path.Combine(_scratch, "not<a>directory", "Logs");

        Should.NotThrow(() => NewViewModel().OpenLogFolderCommand.Execute(null));

        _opened.ShouldBe([_logDirectory]);
    }

    /// <summary>The path is in the message because the folder is what the user was after.</summary>
    [Fact]
    public void OpenLogFolder_WhenTheShellRefuses_BeepsAndExplains()
    {
        _shellOpens = false;

        var vm = NewViewModel();
        vm.OpenLogFolderCommand.Execute(null);

        _beeps.ShouldBe(1);
        vm.Message.ShouldContain(_logDirectory);
    }

    /// <summary>A later attempt that works must not leave the earlier refusal on screen.</summary>
    [Fact]
    public void OpenLogFolder_WhenTheShellOpensIt_ClearsAnEarlierRefusal()
    {
        _shellOpens = false;
        var vm = NewViewModel();
        vm.OpenLogFolderCommand.Execute(null);
        vm.Message.ShouldNotBeNullOrWhiteSpace();

        _shellOpens = true;
        vm.OpenLogFolderCommand.Execute(null);

        vm.Message.ShouldBeEmpty();
    }

    [Fact]
    public async Task CopyVersion_PutsTheVersionOnTheClipboard_AndSaysSoForAMoment()
    {
        var moment = new TaskCompletionSource();
        _pause = _ => moment.Task;
        var vm = NewViewModel();

        var copying = vm.CopyVersionCommand.ExecuteAsync(null);

        _copied.ShouldBe([vm.Version]);
        vm.CopyMessage.ShouldBe("Copied");

        moment.SetResult();
        await copying;

        vm.CopyMessage.ShouldBeEmpty();
    }

    /// <summary>
    /// Another process holding the clipboard makes the write fail. That must be said, not passed off
    /// as "Copied": the user would paste whatever was there before and take it for the version.
    /// </summary>
    [Fact]
    public async Task CopyVersion_WhenTheClipboardRefuses_SaysSo()
    {
        var moment = new TaskCompletionSource();
        _pause = _ => moment.Task;
        _clipboardTakes = false;
        var vm = NewViewModel();

        var copying = vm.CopyVersionCommand.ExecuteAsync(null);

        vm.CopyMessage.ShouldContain("Could not copy");

        moment.SetResult();
        await copying;
    }

    /// <summary>The miniature desktop asks here for the chord a command is bound to, and the answer is whatever the hotkeys say.</summary>
    [Fact]
    public void ShortcutOf_AnswersFromTheLookupItWasGiven()
    {
        NewViewModel().ShortcutOf(WindowCommand.LeftThird).ShouldBe(WindowCommand.LeftThird.DefaultShortcut());
    }
}
