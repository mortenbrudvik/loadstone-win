using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Loadstone.Core.Commands;

namespace Loadstone.UI.Controls;

/// <summary>
/// Records a global shortcut. Click or focus starts recording; a key press with a modifier commits,
/// a function key commits alone, Escape cancels, Backspace or Delete alone clears. While recording,
/// the owning view model suspends the real hotkeys through <see cref="IsRecording"/>.
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
        GotKeyboardFocus += (_, _) => IsRecording = true;
        LostKeyboardFocus += (_, _) => IsRecording = false;
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
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
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
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

    private static KeyModifiers Held(ModifierKeys modifiers)
    {
        var held = KeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) held |= KeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) held |= KeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) held |= KeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) held |= KeyModifiers.Win;
        return held;
    }

    private void StopRecording()
    {
        IsRecording = false;
        // Move focus away so the next click starts a fresh recording.
        Keyboard.ClearFocus();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Shortcut = null;
        StopRecording();
    }

    private void Refresh()
    {
        Text.Text = IsRecording ? "Press a shortcut" : Shortcut?.DisplayText ?? "Not set";
        Text.FontStyle = IsRecording || Shortcut is null ? FontStyles.Italic : FontStyles.Normal;
        // TryFindResource: in the constructor the control is not in a tree yet and the theme brushes may be unreachable.
        if (TryFindResource(IsRecording ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush") is Brush brush)
        {
            Outline.BorderBrush = brush;
        }

        ClearButton.Visibility = Shortcut is null || IsRecording ? Visibility.Collapsed : Visibility.Visible;
    }
}
