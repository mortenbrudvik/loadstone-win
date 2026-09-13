using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Loadstone.UI.Controls;
using Shouldly;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace Loadstone.UI.Tests.Controls;

/// <summary>
/// The recorder's visuals and accessibility surface, checked on a shown control with the WPF-UI
/// dictionaries the app merges at application level.
/// </summary>
public class ShortcutRecorderTests
{
    /// <summary>
    /// The outline brush must stay a resource reference after the state changes, not become a brush
    /// instance captured at that moment: WPF-UI swaps the theme dictionary on a light/dark change,
    /// and a captured brush keeps the previous theme's stroke until the recorder is next touched -
    /// a light stroke on a dark ground, which is invisible. Both states are checked because each
    /// has its own key, and the constructor's refresh runs before the control is in a tree.
    /// </summary>
    [WpfTheory]
    [InlineData(false, "ControlStrokeColorDefaultBrush")]
    [InlineData(true, "AccentFillColorDefaultBrush")]
    public void Outline_KeepsAResourceReference_AfterTheStateChanges(bool recording, string brushKey)
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder, ApplicationTheme.Dark);
        try
        {
            // Through the opposite state first, so the refresh runs in the tree for both values.
            recorder.IsRecording = !recording;
            recorder.IsRecording = recording;

            recorder.Outline.ReadLocalValue(Border.BorderBrushProperty).ShouldBeAssignableTo<Expression>();
            recorder.Outline.BorderBrush.ShouldBeSameAs(window.FindResource(brushKey));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The stroke brush is a different instance in each theme dictionary (the accent brush is shared
    /// between them, so it cannot show this), which makes it the one that proves the outline follows
    /// a theme swap rather than the brush it was last given.
    /// </summary>
    [WpfFact]
    public void Outline_FollowsTheStrokeBrush_AfterTheThemeDictionaryIsSwapped()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder, ApplicationTheme.Dark);
        try
        {
            recorder.IsRecording = true;
            recorder.IsRecording = false;
            var dark = window.FindResource("ControlStrokeColorDefaultBrush");
            recorder.Outline.BorderBrush.ShouldBeSameAs(dark);

            window.Resources.MergedDictionaries[0] = new ThemesDictionary { Theme = ApplicationTheme.Light };
            window.UpdateLayout();

            var light = window.FindResource("ControlStrokeColorDefaultBrush");
            light.ShouldNotBeSameAs(dark);
            recorder.Outline.BorderBrush.ShouldBeSameAs(light);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>WPF derives no automation name from a tooltip, so an icon-only button is nameless to a screen reader without one.</summary>
    [WpfFact]
    public void ClearButton_HasAnAccessibleName()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder, ApplicationTheme.Dark);
        try
        {
            AutomationProperties.GetName(recorder.ClearButton).ShouldBe("Clear shortcut");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The guide's minimum pointer target is 28 px; the button used to be 26 px tall inside the 32 px control.</summary>
    [WpfFact]
    public void ClearButton_IsAtLeastTwentyEightPixelsEachWay()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder, ApplicationTheme.Dark);
        try
        {
            recorder.Shortcut = Loadstone.Core.Commands.Shortcut.Parse("Ctrl+Alt+Left");
            window.UpdateLayout();

            recorder.ClearButton.ActualWidth.ShouldBeGreaterThanOrEqualTo(28);
            recorder.ClearButton.ActualHeight.ShouldBeGreaterThanOrEqualTo(28);
            recorder.ActualHeight.ShouldBe(32);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// "Press a shortcut" and "Not set" are the recorder's state, and the only hint a screen-reader
    /// user gets of how to record; a live region is what makes a change to them announced.
    /// </summary>
    [WpfFact]
    public void StateText_IsAPoliteLiveRegion_AndTheControlExplainsHowToRecord()
    {
        var recorder = new ShortcutRecorder();
        var window = HostFor(recorder, ApplicationTheme.Dark);
        try
        {
            AutomationProperties.GetLiveSetting(recorder.Text).ShouldBe(AutomationLiveSetting.Polite);
            AutomationProperties.GetHelpText(recorder).ShouldContain("Escape");
        }
        finally
        {
            window.Close();
        }
    }

    private static Window HostFor(UIElement content, ApplicationTheme theme)
    {
        var window = new Window
        {
            Width = 300,
            Height = 100,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = content,
        };
        window.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
        window.Resources.MergedDictionaries.Add(new ControlsDictionary());
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
