using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Loadstone.UI.Controls;

/// <summary>The colour of a drawn window: the Mac icon's blue and sand, and a slate for a third.</summary>
internal enum WindowTint
{
    Blue,
    Sand,
    Slate,
}

/// <summary>
/// The two things the splash and the About tab are both drawn from: the stone, and the little windows
/// it pulls into place. The splash runs on a thread of its own, so nothing here may belong to the
/// thread that first asked for it: every brush and the stone's bitmap are frozen, and no element is
/// shared. Nothing here reads a resource dictionary either, for the reason <c>SplashWindow</c> gives.
/// </summary>
internal static class LoadstoneArt
{
    private static readonly Lazy<ImageSource?> StoneImage = new(LoadStone);

    private static readonly Brush TitleStrip = Frozen(new SolidColorBrush(Color.FromArgb(0x47, 0xFF, 0xFF, 0xFF)));

    private static readonly Brush Blue = Vertical(Color.FromRgb(0x7E, 0xA3, 0xC8), Color.FromRgb(0x48, 0x69, 0x8D));
    private static readonly Brush Sand = Vertical(Color.FromRgb(0xDF, 0xCD, 0xA6), Color.FromRgb(0xB4, 0x9B, 0x6E));
    private static readonly Brush Slate = Vertical(Color.FromRgb(0x9A, 0xA3, 0xB2), Color.FromRgb(0x5D, 0x66, 0x76));

    /// <summary>
    /// The app icon's largest frame, or null when the resource is missing, so a renamed or dropped
    /// asset costs the picture and not the window it was for. Read through the assembly's .g resources
    /// rather than a pack URI: that needs no <see cref="Application"/>, which the test host lacks and
    /// the splash thread does not own.
    /// </summary>
    public static ImageSource? Stone => StoneImage.Value;

    /// <summary>The stone at <paramref name="size"/>, or an empty element of that size when the icon could not be read.</summary>
    public static FrameworkElement NewStone(double size)
    {
        var image = new Image { Style = null, Source = Stone, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>
    /// A window as the icon draws one: a rounded block of colour with a lighter strip for its title
    /// bar. The caller sizes and places it.
    /// </summary>
    public static Border NewWindow(WindowTint tint, double cornerRadius, double titleHeight) => new()
    {
        Style = null,
        CornerRadius = new CornerRadius(cornerRadius),
        Background = tint switch
        {
            WindowTint.Blue => Blue,
            WindowTint.Sand => Sand,
            WindowTint.Slate => Slate,
            _ => throw new ArgumentOutOfRangeException(nameof(tint), tint, null),
        },
        Child = new Border
        {
            Style = null,
            Height = titleHeight,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(cornerRadius, cornerRadius, 0, 0),
            Background = TitleStrip,
        },
    };

    public static Brush Vertical(Color top, Color bottom) =>
        Frozen(new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1)));

    public static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static ImageSource? LoadStone()
    {
        try
        {
            var resources = new ResourceManager("Loadstone.g", typeof(LoadstoneArt).Assembly);
            using var stream = resources.GetStream("assets/loadstone.ico");
            if (stream is null)
            {
                return null;
            }

            // OnLoad, so the frame owns its pixels and the stream can go; frozen, so both threads may draw it.
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var largest = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).FirstOrDefault();
            return largest is null ? null : Frozen(largest);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException
            or MissingManifestResourceException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
