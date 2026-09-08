using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Loadstone.Core.Commands;
using Loadstone.UI.Controls;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Controls;

public class RecorderKeyHandlerTests
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;

    [Fact]
    public void Handle_ModifierWithKey_Commits()
    {
        var action = RecorderKeyHandler.Handle(CtrlAlt, 'E');
        action.Kind.ShouldBe(RecorderActionKind.Commit);
        action.Shortcut.ShouldBe(new Shortcut(CtrlAlt, 'E'));
    }

    [Fact]
    public void Handle_FunctionKeyAlone_Commits()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.F1 + 4).ShouldBe(new RecorderAction(RecorderActionKind.Commit, new Shortcut(KeyModifiers.None, VirtualKeys.F1 + 4)));
    }

    [Fact]
    public void Handle_PlainKeyAlone_IsIgnored()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, 'E').Kind.ShouldBe(RecorderActionKind.Ignore);
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Left).Kind.ShouldBe(RecorderActionKind.Ignore);
    }

    [Theory]
    [InlineData(VirtualKeys.ShiftKey)]
    [InlineData(VirtualKeys.ControlKey)]
    [InlineData(VirtualKeys.MenuKey)]
    [InlineData(VirtualKeys.LWin)]
    [InlineData(VirtualKeys.RMenu)]
    public void Handle_LoneModifier_IsIgnored_EvenWithOthersHeld(int modifierKey)
    {
        RecorderKeyHandler.Handle(CtrlAlt, modifierKey).Kind.ShouldBe(RecorderActionKind.Ignore);
        RecorderKeyHandler.Handle(KeyModifiers.None, modifierKey).Kind.ShouldBe(RecorderActionKind.Ignore);
    }

    [Fact]
    public void Handle_Escape_Cancels()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Escape).Kind.ShouldBe(RecorderActionKind.Cancel);
        RecorderKeyHandler.Handle(CtrlAlt, VirtualKeys.Escape).Kind.ShouldBe(RecorderActionKind.Cancel);
    }

    [Fact]
    public void Handle_BackspaceOrDeleteAlone_Clears_ButWithModifiersCommits()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Backspace).Kind.ShouldBe(RecorderActionKind.Clear);
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Delete).Kind.ShouldBe(RecorderActionKind.Clear);
        RecorderKeyHandler.Handle(CtrlAlt, VirtualKeys.Backspace).ShouldBe(new RecorderAction(RecorderActionKind.Commit, new Shortcut(CtrlAlt, VirtualKeys.Backspace)));
    }

    /// <summary>
    /// The two translations between WPF's keyboard and the pure handler above, which the handler's own
    /// tests cannot see. Both sit under every chord the recorder produces: a swapped pair here, or a
    /// lost <see cref="Key.System"/> branch, would rewrite or refuse every default shortcut in the
    /// product while all six tests above still passed.
    /// </summary>
    [WpfTheory]
    [InlineData(ModifierKeys.None, KeyModifiers.None)]
    [InlineData(ModifierKeys.Control, KeyModifiers.Control)]
    [InlineData(ModifierKeys.Alt, KeyModifiers.Alt)]
    [InlineData(ModifierKeys.Shift, KeyModifiers.Shift)]
    [InlineData(ModifierKeys.Windows, KeyModifiers.Win)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(
        ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows,
        KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win)]
    public void Held_MapsEveryWpfModifierOntoItsCoreFlag(ModifierKeys modifiers, KeyModifiers expected)
    {
        ShortcutRecorder.Held(modifiers).ShouldBe(expected);
    }

    /// <summary>
    /// WPF delivers an Alt-modified press as <see cref="Key.System"/> with the real key parked in
    /// SystemKey, and every Ctrl+Alt chord in the product arrives that way. Symptom if the branch
    /// goes: recording Alt+F5 reads the Alt key itself, which is not bindable, so the recorder
    /// silently ignores the press and no default chord can be re-recorded.
    /// </summary>
    [WpfFact]
    public void PreviewKeyDown_AKeyDeliveredAsKeySystem_RecordsTheKeyBehindIt()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder);
        try
        {
            recorder.IsRecording = true;
            var args = SystemKeyPress(window, Key.F5);
            args.Key.ShouldBe(Key.System);
            args.SystemKey.ShouldBe(Key.F5);

            recorder.RaiseEvent(args);

            args.Handled.ShouldBeTrue();
            // No modifier is physically held here, so F5 is the key that commits on its own; a chord
            // needing Ctrl+Alt cannot be staged without a real keyboard.
            recorder.Shortcut.ShouldBe(new Shortcut(KeyModifiers.None, VirtualKeys.F1 + 4));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Recording starts on keyboard focus, so a recorder that swallowed Tab would trap a keyboard-only
    /// user inside it with no way out but Escape (spec 8.3, "Shortcut recorder"; WCAG 2.1.2). Shift is
    /// read from the real keyboard, so only the bare half of the rule can be staged here; it is the
    /// half that carries the trap.
    /// </summary>
    [WpfFact]
    public void PreviewKeyDown_TabWithNothingHeld_IsLeftUnhandledSoFocusCanMoveOn()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder);
        try
        {
            recorder.IsRecording = true;
            var args = KeyPress(window, Key.Tab);

            recorder.RaiseEvent(args);

            args.Handled.ShouldBeFalse();
            recorder.Shortcut.ShouldBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Stopping a recording must leave focus alone. Every recorder arms itself on keyboard focus and
    /// in the Shortcuts tab the only focusable element per row is the recorder (the title is a
    /// TextBlock, the clear button is not focusable), so a StopRecording that moved focus on made
    /// Escape and every committed chord hand the recording — and the hotkey suspension that rides on
    /// it — to the row below instead of ending it.
    /// </summary>
    [WpfTheory]
    [InlineData(Key.Escape)]
    [InlineData(Key.F5)]
    public void StoppingARecording_LeavesFocusPut_SoNoOtherRecorderInTheListArms(Key stoppingKey)
    {
        var first = new ShortcutRecorder();
        var second = new ShortcutRecorder();
        var list = new ItemsControl();
        list.Items.Add(first);
        list.Items.Add(second);
        var window = HostFor(list);
        try
        {
            first.Focus();
            // Armed by hand rather than through GotKeyboardFocus, because the test host does not
            // always get real keyboard focus. The assertions below are against whatever focus WPF did
            // hand out, so they hold either way: even where only logical focus lands, a StopRecording
            // that moves focus moves it off this recorder.
            first.IsRecording = true;
            var focused = FocusManager.GetFocusedElement(window);

            first.RaiseEvent(KeyPress(window, stoppingKey));

            first.IsRecording.ShouldBeFalse();
            second.IsRecording.ShouldBeFalse("stopping armed the next recorder in the list");
            // Both recorders print the same way, so say which move went wrong rather than leaving the
            // reader two identical type names.
            FocusManager.GetFocusedElement(window).ShouldBe(focused, "stopping moved focus off the recorder");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Space and Enter are how a keyboard user starts a second recording without leaving the control.
    /// The route exists because Focus() on the element that already has focus raises no
    /// GotKeyboardFocus — the reason StopRecording used to shed focus, at the cost above.
    /// </summary>
    [WpfTheory]
    [InlineData(Key.Space)]
    [InlineData(Key.Enter)]
    public void SpaceOrEnter_OnAStoppedRecorder_StartsRecordingAgain(Key activationKey)
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder);
        try
        {
            recorder.IsRecording = true;
            recorder.RaiseEvent(KeyPress(window, Key.Escape));
            recorder.IsRecording.ShouldBeFalse();

            var args = KeyPress(window, activationKey);
            recorder.RaiseEvent(args);

            // Modifiers come from the real keyboard, so this is the bare press: with one held the key
            // is left unhandled, which is the point of the check in the control.
            args.Handled.ShouldBeTrue();
            recorder.IsRecording.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The same route for the mouse. A click used to start a recording only by way of the focus
    /// change it caused, so clicking the recorder that already had focus did nothing.
    /// </summary>
    [WpfFact]
    public void Click_OnARecorderThatAlreadyHasFocus_StartsRecordingAgain()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder);
        try
        {
            recorder.Focus();
            recorder.IsRecording = true;
            recorder.RaiseEvent(KeyPress(window, Key.Escape));
            recorder.IsRecording.ShouldBeFalse();

            recorder.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            });

            recorder.IsRecording.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A shown window, because <see cref="KeyEventArgs"/> insists on a real presentation source.</summary>
    private static Window HostFor(UIElement content)
    {
        var window = new Window
        {
            Width = 200,
            Height = 100,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = content,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static KeyEventArgs KeyPress(Window window, Key key)
    {
        var source = PresentationSource.FromVisual(window).ShouldNotBeNull();
        return new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
    }

    /// <summary>
    /// The same press as WPF itself hands over for an Alt-modified key. MarkSystem is how WPF moves the
    /// real key into SystemKey and puts <see cref="Key.System"/> in its place, and it is internal to
    /// PresentationCore: there is no public way to build the shape the recorder actually receives, so
    /// the caller asserts on Key and SystemKey before using the result rather than trusting this.
    /// </summary>
    private static KeyEventArgs SystemKeyPress(Window window, Key key)
    {
        var args = KeyPress(window, key);
        typeof(KeyEventArgs)
            .GetMethod("MarkSystem", BindingFlags.Instance | BindingFlags.NonPublic)
            .ShouldNotBeNull()
            .Invoke(args, null);
        return args;
    }
}
