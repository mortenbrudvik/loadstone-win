using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Loadstone.Core.Commands;

namespace Loadstone.UI.Controls;

/// <summary>
/// Records a global shortcut. Keyboard focus starts recording, and so does an explicit activation —
/// a click, or Space or Enter — so a second recording can be started on the control that already has
/// focus. A key press with a modifier commits, a function key commits alone, Escape cancels,
/// Backspace or Delete alone clears, and Tab with nothing, Shift, or Ctrl held moves focus or switches
/// tabs instead of being recorded so the window can be worked with the keyboard. Stopping leaves focus
/// where it is, so no other recorder is armed by it. While recording, the owning view model suspends
/// the real hotkeys through <see cref="IsRecording"/>.
/// </summary>
public partial class ShortcutRecorder : UserControl
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(Shortcut), typeof(ShortcutRecorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ShortcutRecorder)d).Refresh()));

    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(
        nameof(IsRecording), typeof(bool), typeof(ShortcutRecorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ShortcutRecorder)d).Refresh()));

    public ShortcutRecorder()
    {
        InitializeComponent();
        GotKeyboardFocus += (_, _) => StartRecording();
        LostKeyboardFocus += (_, _) => StopRecording();
        // Bubbling rather than preview on purpose: the clear button handles its own press, so clicking
        // it neither arms the recorder nor takes the focus off whatever had it.
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            StartRecording();
            e.Handled = true;
        };
        Refresh();
    }

    public Shortcut? Shortcut
    {
        get => (Shortcut?)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        set => SetValue(IsRecordingProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsRecording)
        {
            // Space and Enter are the keyboard's half of the click: recording no longer rides on a
            // focus transition, so a keyboard user who has just stopped one recording (Escape, or a
            // chord that committed) can start another without tabbing away and back. Alt+Space
            // arrives as Key.System and so still reaches the window menu.
            if ((e.Key == Key.Space || e.Key == Key.Enter) && Keyboard.Modifiers == ModifierKeys.None)
            {
                StartRecording();
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
            return;
        }

        // WPF hands an Alt-modified key over as Key.System with the key the user actually pressed
        // parked in SystemKey. Every Ctrl+Alt chord in the product arrives that way, so this is the
        // main path, not an edge case: without the translation Alt+Left records as the System key.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (LeavesTabToFocusTraversal(key, Keyboard.Modifiers))
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var action = RecorderKeyHandler.Handle(Held(Keyboard.Modifiers), virtualKey);
        switch (action.Kind)
        {
            case RecorderActionKind.Commit:
                Shortcut = action.Shortcut;
                StopRecording();
                break;
            case RecorderActionKind.Clear:
                Shortcut = null;
                StopRecording();
                break;
            case RecorderActionKind.Cancel:
                StopRecording();
                break;
            case RecorderActionKind.Ignore:
                break;
        }
    }

    /// <summary>
    /// Whether a press is left unhandled so the window's own keyboard navigation gets it. Tab and
    /// Shift+Tab move focus, and recording starts on keyboard focus, so a recorder that swallowed them
    /// would trap a keyboard-only user inside it with no way out but Escape (spec 8.3, "Shortcut
    /// recorder", says the control is focusable by Tab; WCAG 2.1.2). Ctrl+Tab and Ctrl+Shift+Tab are
    /// how WPF's TabControl switches tabs from inside its content, and a recorder that committed them
    /// bound Ctrl+Tab system-wide the first time a keyboard user reached for the General tab. The cost
    /// is that none of the four can be recorded as a chord; Alt and Win combinations with Tab still
    /// reach the handler.
    /// </summary>
    internal static bool LeavesTabToFocusTraversal(Key key, ModifierKeys modifiers) =>
        key == Key.Tab && (modifiers & ~(ModifierKeys.Shift | ModifierKeys.Control)) == ModifierKeys.None;

    /// <summary>
    /// Translates WPF's modifier set into the Core one. Internal rather than private so a test can pin
    /// the mapping directly: it sits under every chord the recorder produces, and a swapped pair here
    /// would rewrite every default shortcut in the product while the pure handler's tests all passed.
    /// </summary>
    internal static KeyModifiers Held(ModifierKeys modifiers)
    {
        var held = KeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) held |= KeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) held |= KeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) held |= KeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) held |= KeyModifiers.Win;
        return held;
    }

    /// <summary>
    /// Arms the recorder. Called from keyboard focus and from an explicit activation — a click, Space,
    /// or Enter — because Focus() on the element that already has focus raises no GotKeyboardFocus:
    /// without the second route, a recorder could only be restarted by leaving it and coming back.
    /// </summary>
    private void StartRecording() => IsRecording = true;

    /// <summary>
    /// Stops recording and deliberately leaves focus alone. Shedding it used to be how the next click
    /// on this control could start a fresh recording, and that was worse than the problem it solved:
    /// every recorder arms itself on keyboard focus, and in the Shortcuts tab the only focusable
    /// element per row is the recorder, so moving focus on handed the recording — and the hotkey
    /// suspension that comes with it — to the row below on every Escape and every committed chord.
    /// <see cref="StartRecording"/> covers restarting instead.
    /// </summary>
    private void StopRecording() => IsRecording = false;

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Shortcut = null;
        StopRecording();
    }

    private void Refresh()
    {
        Text.Text = IsRecording ? "Press a shortcut" : Shortcut?.DisplayText ?? "Not set";
        Text.FontStyle = IsRecording || Shortcut is null ? FontStyles.Italic : FontStyles.Normal;

        // A resource reference, never a brush looked up here and assigned: WPF-UI swaps the theme
        // dictionary on a light/dark change, and a captured brush kept the old theme's stroke until
        // the recorder was next touched - a light stroke on a dark ground, which is invisible. The
        // reference resolves lazily, so it is safe from the constructor, before the control is in a tree.
        Outline.SetResourceReference(Border.BorderBrushProperty, IsRecording ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush");

        ClearButton.Visibility = Shortcut is null || IsRecording ? Visibility.Collapsed : Visibility.Visible;
    }
}
