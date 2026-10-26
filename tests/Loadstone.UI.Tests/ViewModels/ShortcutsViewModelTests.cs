using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.ViewModels;

public class ShortcutsViewModelTests
{
    private readonly InMemorySettings _settings = new();
    private readonly HotkeyCenter _hotkeys;
    private bool _confirmed;
    private int _asked;

    public ShortcutsViewModelTests()
    {
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        _hotkeys = new HotkeyCenter(registrar, _settings, Substitute.For<ICommandDispatcher>(), probe, NullLogger<HotkeyCenter>.Instance);
        _hotkeys.Start();
    }

    /// <summary>The internal constructor: the question is a seam here, and the window puts a ContentDialog behind it.</summary>
    private ShortcutsViewModel NewViewModel() => new(_hotkeys, () =>
    {
        _asked++;
        return Task.FromResult(_confirmed);
    });

    /// <summary>
    /// One click used to erase every recorded chord with nothing in between, and on the Shortcuts
    /// tab that button was the first Tab stop. Declining has to leave the customizations exactly as
    /// they were.
    /// </summary>
    [Fact]
    public async Task RestoreDefaults_WhenDeclined_LeavesEveryShortcutAlone()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        _confirmed = false;

        await NewViewModel().RestoreDefaultsCommand.ExecuteAsync(null);

        _asked.ShouldBe(1);
        _settings.Current.Shortcuts["leftHalf"].ShouldBe("Ctrl+Alt+Shift+Left");
    }

    [Fact]
    public async Task RestoreDefaults_WhenConfirmed_ClearsEveryCustomization()
    {
        _settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
        _confirmed = true;

        await NewViewModel().RestoreDefaultsCommand.ExecuteAsync(null);

        _asked.ShouldBe(1);
        _settings.Current.Shortcuts.ShouldBeEmpty();
    }
}
