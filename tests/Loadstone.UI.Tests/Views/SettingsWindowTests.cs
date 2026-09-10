using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Loadstone.Core.Commands;
using Loadstone.Core.Platform;
using Loadstone.UI.Controls;
using Loadstone.UI.Input;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Views;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
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
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.InUse(1409));
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        var settings = new InMemorySettings();
        using var hotkeys = new HotkeyCenter(registrar, settings, Substitute.For<ICommandDispatcher>(), probe, NullLogger<HotkeyCenter>.Instance);
        hotkeys.Start();
        var startup = Substitute.For<IStartupRegistration>();
        startup.Status.Returns(StartupStatus.NotRegistered);

        var window = new SettingsWindow(
            new SettingsViewModel(settings, startup, NullLogger<SettingsViewModel>.Instance),
            new ShortcutsViewModel(hotkeys));
        try
        {
            // App.xaml merges these at application level; the test host has no Application, so the
            // window carries them itself. Without them FluentWindow has no template and lays out nothing.
            window.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            window.Resources.MergedDictionaries.Add(new ControlsDictionary());
            window.Show();

            // A renamed or dropped .ico leaves the title bar without an icon and says nothing, since
            // AppIconFrame swallows the miss on purpose so the window still opens. This is what turns
            // that silence into a failing test.
            window.SettingsTitleBar.Icon.ShouldNotBeNull();
            window.UpdateLayout();
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
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Shortcuts"));
            window.UpdateLayout();

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
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        var settings = new InMemorySettings();
        using var hotkeys = new HotkeyCenter(registrar, settings, Substitute.For<ICommandDispatcher>(), probe, NullLogger<HotkeyCenter>.Instance);
        hotkeys.Start();
        var startup = Substitute.For<IStartupRegistration>();
        startup.Status.Returns(StartupStatus.NotRegistered);

        var window = new SettingsWindow(
            new SettingsViewModel(settings, startup, NullLogger<SettingsViewModel>.Instance),
            new ShortcutsViewModel(hotkeys));
        try
        {
            window.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            window.Resources.MergedDictionaries.Add(new ControlsDictionary());
            window.Show();
            window.UpdateLayout();

            var tabs = Descendants<TabControl>(window).ShouldHaveSingleItem();
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Shortcuts"));
            window.UpdateLayout();

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
