using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Loadstone.UI.Controls;
using Loadstone.UI.Interop;
using Loadstone.UI.Tests.Windows;
using Loadstone.UI.Views;
using Loadstone.UI.Windows;
using Shouldly;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace Loadstone.UI.Tests.Views;

public class SplashWindowTests
{
    /// <summary>
    /// A hotkey pressed while the splash is up acts on the window in front, and Loadstone often starts
    /// at sign-in, in the middle of whatever the user began doing. So the splash must not take the
    /// foreground when it shows, and a click on it must not either: ShowActivated covers the first,
    /// and only WS_EX_NOACTIVATE covers the second. WS_EX_TOOLWINDOW keeps it out of Alt+Tab.
    /// </summary>
    [WpfTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Shows_WithoutTakingTheForeground_AndCannotBeActivated(bool light, bool animate)
    {
        var before = NativeMethods.User32.GetForegroundWindow();
        var window = new SplashWindow(light, animate, TimeSpan.Zero);
        try
        {
            window.Show();
            TestWindow.Pump();

            var handle = new WindowInteropHelper(window).Handle;
            NativeMethods.User32.GetForegroundWindow().ShouldNotBe(handle);
            NativeMethods.User32.GetForegroundWindow().ShouldBe(before);

            var exStyle = WindowStyles.StyleWord(handle, NativeConstants.GWL_EXSTYLE).ShouldNotBeNull();
            (exStyle & NativeConstants.WS_EX_NOACTIVATE).ShouldNotBe(0);
            (exStyle & NativeConstants.WS_EX_TOOLWINDOW).ShouldNotBe(0);
            window.Problem.ShouldBeNull();
            window.ShowInTaskbar.ShouldBeFalse();
            window.Topmost.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The stone is the app icon, read out of the assembly. A renamed or dropped .ico would leave the splash and the About tab without it and say nothing.</summary>
    [Fact]
    public void TheStone_IsPacked_AndFrozenSoBothThreadsMayDrawIt()
    {
        var stone = LoadstoneArt.Stone.ShouldNotBeNull();

        stone.IsFrozen.ShouldBeTrue();
        stone.Width.ShouldBeGreaterThanOrEqualTo(128);
    }

    /// <summary>
    /// The splash is built on a thread of its own, while the app's thread is rewriting the application
    /// resources. An element with no style of its own looks one up there by its type, so every element
    /// in the splash either carries a local style - null, which is still a local value and stops the
    /// lookup - or is of a type WPF-UI defines no implicit style for. The parts of the window's own
    /// template cannot be given a local style, so they have to be the second kind. WPF-UI does style
    /// TextBlock, which is why a splash TextBlock that lost its null style fails here, as does a WPF-UI
    /// upgrade that starts styling Border or ContentPresenter.
    /// </summary>
    [WpfFact]
    public void NothingInTheSplash_LooksUpAnImplicitStyle()
    {
        // WPF-UI's dictionaries load themselves from pack URIs, and it is Application's static
        // constructor that registers the scheme and its "application" authority. Run alone, this test
        // is the first thing in the process to need them.
        _ = Application.Current;

        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        resources.MergedDictionaries.Add(new ControlsDictionary());
        resources.Contains(typeof(TextBlock)).ShouldBeTrue("the premise: WPF-UI styles TextBlock, so the lookup is not hypothetical");

        var window = new SplashWindow(light: false, animate: false, TimeSpan.Zero);
        try
        {
            window.Show();
            TestWindow.Pump();

            var elements = Descendants(window).Prepend(window).OfType<FrameworkElement>().ToList();
            elements.Count.ShouldBeGreaterThan(10);
            elements
                .Where(element => element.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
                .Where(element => resources.Contains(element.GetType()))
                .Select(element => element.GetType().Name)
                .ShouldBeEmpty();
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
