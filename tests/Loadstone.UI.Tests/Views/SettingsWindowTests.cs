using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Loadstone.Core.Commands;
using Loadstone.Core.Platform;
using Loadstone.UI.Controls;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Tests.Windows;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Views;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace Loadstone.UI.Tests.Views;

/// <summary>
/// Shows the real window and realizes every tab. A XAML error inside a data template only surfaces
/// when the template is applied, which the view-model tests never do.
/// </summary>
public class SettingsWindowTests
{
    [WpfFact]
    public void EveryTab_RendersItsTemplates()
    {
        var window = ShownWindow(HotkeyStatus.InUse(1409));
        try
        {
            // A renamed or dropped .ico leaves the title bar without an icon and says nothing, since
            // AppIconFrame swallows the miss on purpose so the window still opens. This is what turns
            // that silence into a failing test.
            window.SettingsTitleBar.Icon.ShouldNotBeNull();
            var tabs = Descendants<TabControl>(window).ShouldHaveSingleItem();

            for (var index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                window.UpdateLayout();
            }

            // Recorders only exist while the Shortcuts tab is the realized one, so select it by name
            // rather than trusting the loop to have left it selected: that held only because Shortcuts
            // is the last TabItem, and moving a tab would have failed this count for a reason nobody
            // would find from the message.
            SelectTab(window, "Shortcuts");

            Descendants<ShortcutRecorder>(window).Count().ShouldBe(WindowCommands.All.Count);
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// WPF-UI's ScrollViewer paints the bar over the content. The Shortcuts tab's recorders sit on
    /// the right edge, so without a gutter they land under the thumb and the clear button cannot be
    /// clicked. Removing that gutter is the production change this catches.
    /// </summary>
    [WpfFact]
    public void ShortcutsTab_ScrollBarDoesNotCoverRecorders()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            SelectTab(window, "Shortcuts");

            var recorder = Descendants<ShortcutRecorder>(window).First();
            var scroller = Ancestor<ScrollViewer>(recorder).ShouldNotBeNull();
            scroller.ScrollableHeight.ShouldBeGreaterThan(0);

            var bar = Descendants<ScrollBar>(scroller).Single(b => b.Orientation == Orientation.Vertical);
            bar.Visibility.ShouldBe(Visibility.Visible);
            bar.ActualWidth.ShouldBeGreaterThan(0);

            var barBounds = BoundsIn(window, bar);
            foreach (var row in Descendants<ShortcutRecorder>(window))
            {
                BoundsIn(window, row).Right.ShouldBeLessThanOrEqualTo(barBounds.Left);
            }
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// "Still held by Loadstone; recording it may not work (error N)" is wider than the star column at
    /// the default width, and a caption that cannot wrap is clipped mid-word with no ellipsis. The
    /// live region is what makes a status change reach a screen reader at all; without it "In use by
    /// another app" is never announced.
    /// </summary>
    [WpfFact]
    public void ShortcutsTab_StatusCaptions_WrapAndAreLiveRegions()
    {
        var window = ShownWindow(HotkeyStatus.InUse(1409));
        try
        {
            SelectTab(window, "Shortcuts");

            var captions = Descendants<TextBlock>(window).Where(caption => BindsTo(caption, "Status")).ToList();
            captions.Count.ShouldBe(WindowCommands.All.Count);
            foreach (var caption in captions)
            {
                caption.TextWrapping.ShouldBe(TextWrapping.Wrap);
                AutomationProperties.GetLiveSetting(caption).ShouldBe(AutomationLiveSetting.Polite);
            }
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>The General tab is where the user chooses one-shot Restore or stepping back one move.</summary>
    [WpfFact]
    public void GeneralTab_OffersRestoreOneMoveAtATime()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            SelectTab(window, "General");

            var toggle = Descendants<Wpf.Ui.Controls.ToggleSwitch>(window)
                .Single(candidate => candidate.Content as string == "Restore one move at a time");
            toggle.IsChecked.ShouldBe(false);
            var caption = Descendants<TextBlock>(window)
                .Single(block => block.Text.Contains("steps back one move", StringComparison.Ordinal));
            caption.Text.ShouldContain("by default");
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>The launch-at-login captions change under the user's hands: a refresh or a refused write rewrites them.</summary>
    [WpfFact]
    public void GeneralTab_LaunchAtLoginCaptions_AreLiveRegions()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            SelectTab(window, "General");

            foreach (var path in new[] { "LaunchAtLoginMessage", "LaunchAtLoginActionMessage", "ShellActionMessage" })
            {
                var caption = Descendants<TextBlock>(window).Single(candidate => BindsTo(candidate, path));
                AutomationProperties.GetLiveSetting(caption).ShouldBe(AutomationLiveSetting.Polite, path);
            }
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// Keyboard order follows the tree. The reset button was declared before the scroll viewer so it
    /// could dock to the bottom, which made it the first Tab stop on the tab: Tab then Space wiped
    /// every recorded chord before a recorder had been reached.
    /// </summary>
    [WpfFact]
    public void ShortcutsTab_TabOrder_ReachesTheRecordersBeforeRestoreDefaults()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            SelectTab(window, "Shortcuts");

            var first = Descendants<FrameworkElement>(window)
                .First(element => element is ShortcutRecorder || element is Button { Content: "Restore defaults" });
            first.ShouldBeOfType<ShortcutRecorder>();
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// Both buttons docked right, and the one declared first lands rightmost, so the tree order and
    /// with it the Tab order ran right to left; and with both visible nothing kept them apart.
    /// </summary>
    [WpfFact]
    public void GeneralTab_ShellButtons_ReadLeftToRight_WithAGapBetween()
    {
        var (window, _) = ShownWindowWith(HotkeyStatus.Registered, StartupStatus.DisabledInTaskManager);
        try
        {
            SelectTab(window, "General");

            var buttons = Descendants<Button>(window)
                .Where(button => button.Content is "Open log folder" or "Open Startup Apps")
                .ToList();
            buttons.Select(button => (string)button.Content).ShouldBe(["Open log folder", "Open Startup Apps"]);
            buttons.ShouldAllBe(button => button.IsVisible);

            // A tolerance because the transform to window space carries floating-point error: the
            // same 8 px margin measured 7.99999999999994 on one run and 8 on another.
            var logFolder = BoundsIn(window, buttons[0]);
            var startupApps = BoundsIn(window, buttons[1]);
            (startupApps.Left - logFolder.Right).ShouldBe(8, tolerance: 0.01);
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// The caption under each switch is what explains it, and nothing told assistive technology the
    /// two belong together: a screen reader on the switch announced its content and nothing more.
    /// The two fixed captions are shared resources, so the help text cannot drift from the text on
    /// screen; the launch-at-login one follows the live message.
    /// </summary>
    [WpfFact]
    public void GeneralTab_Toggles_CarryTheirCaptionsAsHelpText()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            SelectTab(window, "General");

            foreach (var content in new[] { "Snap windows by dragging to screen edges", "Restore one move at a time" })
            {
                var help = AutomationProperties.GetHelpText(Toggle(window, content));
                help.ShouldNotBeNullOrWhiteSpace(content);
                Descendants<TextBlock>(window).ShouldContain(block => block.Text == help, content);
            }

            BindingOperations.GetBinding(Toggle(window, "Launch at login"), AutomationProperties.HelpTextProperty)
                .ShouldNotBeNull().Path.Path.ShouldBe("LaunchAtLoginMessage");
        }
        finally
        {
            window.Hide();
        }
    }

    [WpfFact]
    public void GeneralTab_LaunchAtLogin_IsDisabledWhenAPolicyDecidesIt()
    {
        var (window, _) = ShownWindowWith(HotkeyStatus.Registered, StartupStatus.DisabledByPolicy);
        try
        {
            SelectTab(window, "General");

            Toggle(window, "Launch at login").IsEnabled.ShouldBeFalse();
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// The real wiring, end to end: the view model asks through WPF-UI's dialog service and the
    /// window hosts the dialog. The click opens the question and clears nothing until it is
    /// answered, and dismissing the question clears nothing at all.
    /// </summary>
    [WpfFact]
    public async Task ShortcutsTab_RestoreDefaults_AsksBeforeClearing()
    {
        var (window, settings) = ShownWindowWith(HotkeyStatus.Registered, StartupStatus.NotRegistered);
        try
        {
            settings.Current.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
            SelectTab(window, "Shortcuts");

            var asking = window.Shortcuts.RestoreDefaultsCommand.ExecuteAsync(null);
            TestWindow.Pump();

            var dialog = Descendants<Wpf.Ui.Controls.ContentDialog>(window).ShouldHaveSingleItem();
            settings.Current.Shortcuts.ShouldContainKey("leftHalf");

            dialog.Hide(Wpf.Ui.Controls.ContentDialogResult.None);
            await asking;

            settings.Current.Shortcuts.ShouldContainKey("leftHalf");
        }
        finally
        {
            window.Hide();
        }
    }

    [WpfFact]
    public void Close_HidesAndCanBeShownAgain()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            window.Close();
            window.IsVisible.ShouldBeFalse();

            window.Show();
            window.IsVisible.ShouldBeTrue();

            window.Close();
            window.IsVisible.ShouldBeFalse();
            Should.NotThrow(window.Show);
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// ShowMaximize="False" only hides the button. With the maximize box still in the window style, a
    /// title-bar double-click or Win+Up maximized the 520 px window anyway. Resizing must survive:
    /// the window has a minimum size, not a fixed one.
    /// </summary>
    [WpfFact]
    public void Window_HasNoMaximizeBox_AndStaysResizable()
    {
        var window = ShownWindow(HotkeyStatus.Registered);
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = WindowStyles.StyleWord(hwnd, NativeConstants.GWL_STYLE).ShouldNotBeNull();

            (style & NativeConstants.WS_MAXIMIZEBOX).ShouldBe(0);
            (style & NativeConstants.WS_THICKFRAME).ShouldNotBe(0);
        }
        finally
        {
            window.Hide();
        }
    }

    /// <summary>
    /// The window as the app builds it. App.xaml merges the dictionaries at application level; the
    /// test host has no Application, so the window carries them itself. Without them FluentWindow has
    /// no template and lays out nothing.
    /// </summary>
    private static SettingsWindow ShownWindow(HotkeyStatus registration) =>
        ShownWindowWith(registration, StartupStatus.NotRegistered).Window;

    /// <summary>The window as the app builds it, through the public constructors, with the settings it reads and writes.</summary>
    private static (SettingsWindow Window, InMemorySettings Settings) ShownWindowWith(HotkeyStatus registration, StartupStatus status)
    {
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(registration);
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        var settings = new InMemorySettings();
        var hotkeys = new HotkeyCenter(registrar, settings, Substitute.For<ICommandDispatcher>(), probe, NullLogger<HotkeyCenter>.Instance);
        hotkeys.Start();
        var startup = Substitute.For<IStartupRegistration>();
        startup.Status.Returns(status);
        var dialogs = new ContentDialogService();

        var window = new SettingsWindow(
            new SettingsViewModel(settings, startup, NullLogger<SettingsViewModel>.Instance),
            new ShortcutsViewModel(hotkeys, dialogs),
            dialogs,
            NullLogger<SettingsWindow>.Instance);
        window.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        window.Resources.MergedDictionaries.Add(new ControlsDictionary());
        window.Show();
        window.UpdateLayout();
        return (window, settings);
    }

    private static Wpf.Ui.Controls.ToggleSwitch Toggle(SettingsWindow window, string content) =>
        Descendants<Wpf.Ui.Controls.ToggleSwitch>(window).Single(candidate => candidate.Content as string == content);

    private static void SelectTab(SettingsWindow window, string header)
    {
        var tabs = Descendants<TabControl>(window).ShouldHaveSingleItem();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, header));
        window.UpdateLayout();
    }

    private static bool BindsTo(TextBlock caption, string path) =>
        BindingOperations.GetBinding(caption, TextBlock.TextProperty)?.Path?.Path == path;

    private static T? Ancestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(start); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static Rect BoundsIn(Visual root, UIElement element) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
