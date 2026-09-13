using System.IO;
using System.Resources;
using System.Windows.Media.Imaging;
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class StartupSplashTests
{
    /// <summary>
    /// SplashScreen draws its bitmap one image pixel to one screen pixel and never scales it, so the
    /// pick is the only thing that sizes the splash for the display. The wrong one shows it at two
    /// thirds or one and a half times its size. 110 and 120 are the pair that tell rounding apart:
    /// always rounding up takes 110 to the 150 image, always rounding down takes 120 to the 100 one.
    /// </summary>
    [Theory]
    [InlineData(96, false, "assets/splash/splash-dark.scale-100.png")]
    [InlineData(110, false, "assets/splash/splash-dark.scale-100.png")]
    [InlineData(120, false, "assets/splash/splash-dark.scale-150.png")]
    [InlineData(144, false, "assets/splash/splash-dark.scale-150.png")]
    [InlineData(168, false, "assets/splash/splash-dark.scale-200.png")]
    [InlineData(192, false, "assets/splash/splash-dark.scale-200.png")]
    [InlineData(288, false, "assets/splash/splash-dark.scale-200.png")]
    [InlineData(96, true, "assets/splash/splash-light.scale-100.png")]
    [InlineData(144, true, "assets/splash/splash-light.scale-150.png")]
    [InlineData(192, true, "assets/splash/splash-light.scale-200.png")]
    public void ResourceName_PicksTheNearestScale_AndTheTheme(int dpi, bool lightTheme, string expected)
    {
        StartupSplash.ResourceName(dpi, lightTheme).ShouldBe(expected);
    }

    /// <summary>
    /// The splash stays up for at least a second and a half however quickly startup finished. The
    /// clamp is the part that breaks something: a startup slower than the minimum leaves a negative
    /// remainder, and Task.Delay throws on one, so the splash would never close.
    /// </summary>
    [Theory]
    [InlineData(0, 1500)]
    [InlineData(400, 1100)]
    [InlineData(1500, 0)]
    [InlineData(4000, 0)]
    public void RemainingDelay_TopsUpToTheMinimum_AndNeverGoesNegative(int elapsedMs, int expectedMs)
    {
        StartupSplash.RemainingDelay(TimeSpan.FromMilliseconds(elapsedMs)).ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    /// <summary>
    /// Every name <see cref="StartupSplash.ResourceName"/> can hand out has to be packed where
    /// SplashScreen looks for it - the assembly's .g resources - and drawn at its scale. A missing one
    /// throws out of Show and the user starts with no splash and only a warning in the log; one drawn
    /// at the wrong scale undoes the pick above without anything failing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryScale_IsPacked_AtItsScale(bool lightTheme)
    {
        var at100 = PixelSize(StartupSplash.ResourceName(96, lightTheme));
        var at150 = PixelSize(StartupSplash.ResourceName(144, lightTheme));
        var at200 = PixelSize(StartupSplash.ResourceName(192, lightTheme));

        at150.ShouldBe((at100.Width * 3 / 2, at100.Height * 3 / 2));
        at200.ShouldBe((at100.Width * 2, at100.Height * 2));
    }

    private static (int Width, int Height) PixelSize(string resourceName)
    {
        var resources = new ResourceManager("Loadstone.g", typeof(StartupSplash).Assembly);
        using Stream stream = resources.GetStream(resourceName).ShouldNotBeNull($"{resourceName} is not packed");
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }
}
