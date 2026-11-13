using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Loadstone.UI.Controls;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
using Loadstone.UI.Windows;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Views;

/// <summary>
/// The startup splash: four loose windows are pulled to the stone and lock into a grid around it, and
/// the name lands beneath. A click on the stone throws the windows out and pulls them back; a click
/// anywhere else dismisses it; and it waits while the pointer plays over it.
/// </summary>
/// <remarks>
/// <para>
/// It lives on a thread of its own (<see cref="StartupSplash"/>), because the app's dispatcher does not
/// pump until <c>OnStartup</c> returns and nothing on it could move until then. That is also what keeps
/// it out of <see cref="Application.MainWindow"/>, the slot the Settings window holds: WPF only gives
/// that slot, and a place in <see cref="Application.Windows"/>, to a window built on the app's thread.
/// </para>
/// <para>
/// Being on another thread is why this is code and not XAML, and why every element is given a null
/// <see cref="FrameworkElement.Style"/>. An element without a local style looks one up by its type in
/// <see cref="Application.Resources"/>, and those dictionaries belong to the app's thread, which is
/// rewriting them (the theme is applied during startup) while this window is being built. A null
/// style is a local value, so no lookup happens. The parts of the window's own template still look,
/// which is safe only while WPF-UI ships no implicit style for them;
/// <c>SplashWindowTests.WpfUi_HasNoImplicitStyle_ForAnythingTheSplashIsBuiltFrom</c> pins that.
/// </para>
/// <para>
/// It must never take the foreground. A hotkey pressed while it is up acts on the window in front,
/// and Loadstone often starts at sign-in, in the middle of whatever the user began doing.
/// </para>
/// </remarks>
internal sealed class SplashWindow : Window
{
    public const double PlateWidth = 360;
    public const double PlateHeight = 220;

    /// <summary>Room round the plate for its shadow.</summary>
    private const double Surround = 28;

    private const double PlateRadius = 14;
    private const double TileWidth = 52;
    private const double TileHeight = 40;
    private const double StoneSize = 72;

    /// <summary>Where each window rests, where it is thrown from, and how far it is turned on the way.</summary>
    private static readonly (double X, double Y, double Dx, double Dy, double Angle, WindowTint Tint)[] Tiles =
    [
        (126, 38, -86, -32, -15, WindowTint.Blue),
        (182, 38, 94, -25, 11, WindowTint.Sand),
        (126, 82, -97, 29, 9, WindowTint.Sand),
        (182, 82, 86, 36, -13, WindowTint.Blue),
    ];

    private readonly bool _animate;
    private readonly TimeSpan _minimumShown;
    private readonly Stopwatch _shown = new();
    private readonly List<(Border Tile, TranslateTransform Move, RotateTransform Turn, ScaleTransform Size)> _tiles = [];
    private readonly ScaleTransform _stoneSize = new(1, 1);
    private readonly Ellipse _ring;
    private readonly ScaleTransform _ringSize = new(1, 1);
    private Point? _pointer;
    private TimeSpan? _interacted;
    private DispatcherTimer? _closeTimer;
    private bool _closing;

    /// <param name="light">The app theme, read by the caller: this window reads no resources.</param>
    /// <param name="animate">False with animation effects turned off in Windows: everything is drawn in place and nothing moves.</param>
    /// <param name="minimumShown">How long it stays up however quickly startup finished.</param>
    public SplashWindow(bool light, bool animate, TimeSpan minimumShown)
    {
        _animate = animate;
        _minimumShown = minimumShown;

        Style = null;
        Title = "Loadstone";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Topmost = true;
        Width = PlateWidth + 2 * Surround;
        Height = PlateHeight + 2 * Surround;

        // Centred on the primary display, where WPF's own splash put itself. Both figures are in the
        // primary display's own units, so this holds at any scale.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;

        var ink = light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White;
        var plateTop = light ? Color.FromRgb(0xF7, 0xF7, 0xF8) : Color.FromRgb(0x26, 0x28, 0x2D);
        var plateBottom = light ? Color.FromRgb(0xE8, 0xE9, 0xEC) : Color.FromRgb(0x1A, 0x1B, 0x1F);
        var edge = light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);

        var canvas = new Canvas
        {
            Style = null,
            Width = PlateWidth,
            Height = PlateHeight,
            Clip = LoadstoneArt.Frozen(new RectangleGeometry(new Rect(0, 0, PlateWidth, PlateHeight), PlateRadius, PlateRadius)),
        };

        foreach (var (x, y, dx, dy, angle, tint) in Tiles)
        {
            var move = new TranslateTransform(animate ? dx : 0, animate ? dy : 0);
            var turn = new RotateTransform(animate ? angle : 0);
            var size = new ScaleTransform(animate ? 0.86 : 1, animate ? 0.86 : 1);
            var tile = LoadstoneArt.NewWindow(tint, cornerRadius: 5, titleHeight: 7);
            tile.Width = TileWidth;
            tile.Height = TileHeight;
            tile.Opacity = animate ? 0 : 1;
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.RenderTransform = new TransformGroup { Children = { size, turn, move } };
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            canvas.Children.Add(tile);
            _tiles.Add((tile, move, turn, size));
        }

        // The accent colour, as the snap preview uses, so the pulse is the same colour as the thing it stands for.
        var accent = SystemParameters.WindowGlassColor;
        _ring = new Ellipse
        {
            Style = null,
            Width = 62,
            Height = 48,
            Stroke = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B))),
            StrokeThickness = 1.4,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _ringSize,
        };
        Canvas.SetLeft(_ring, (PlateWidth - _ring.Width) / 2);
        Canvas.SetTop(_ring, 58);
        canvas.Children.Add(_ring);

        var stone = LoadstoneArt.NewStone(StoneSize);
        stone.Opacity = animate ? 0 : 1;
        stone.Cursor = Cursors.Hand;
        stone.RenderTransformOrigin = new Point(0.5, 0.5);
        stone.RenderTransform = _stoneSize;
        stone.MouseLeftButtonDown += OnStonePressed;
        Canvas.SetLeft(stone, (PlateWidth - StoneSize) / 2);
        Canvas.SetTop(stone, 44);
        canvas.Children.Add(stone);

        var word = Line("Loadstone", 26, FontWeights.SemiBold, "Segoe UI Variable Display, Segoe UI", ink, top: 131);
        var tag = Line("Snap any window into place.", 13, FontWeights.Normal, "Segoe UI Variable Text, Segoe UI",
            Color.FromArgb(0xA3, ink.R, ink.G, ink.B), top: 171);
        canvas.Children.Add(word);
        canvas.Children.Add(tag);

        var plate = new Grid { Style = null, Margin = new Thickness(Surround) };

        // The shadow is a shape of its own behind the plate, not an effect on the plate: an effect is
        // redrawn whenever anything inside its element changes, and everything inside the plate moves.
        plate.Children.Add(new Border
        {
            Style = null,
            CornerRadius = new CornerRadius(PlateRadius),
            Background = LoadstoneArt.Frozen(new SolidColorBrush(plateBottom)),
            Effect = LoadstoneArt.Frozen(new DropShadowEffect { BlurRadius = 30, ShadowDepth = 9, Direction = 270, Opacity = 0.45, Color = Colors.Black }),
            IsHitTestVisible = false,
        });
        plate.Children.Add(new Border
        {
            Style = null,
            CornerRadius = new CornerRadius(PlateRadius),
            Background = LoadstoneArt.Vertical(plateTop, plateBottom),
        });
        plate.Children.Add(canvas);
        plate.Children.Add(new Border
        {
            Style = null,
            CornerRadius = new CornerRadius(PlateRadius),
            BorderBrush = LoadstoneArt.Frozen(new SolidColorBrush(edge)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        });
        Content = plate;

        MouseMove += OnPointerMoved;
        MouseLeftButtonDown += (_, _) => FadeAndClose(TimeSpan.FromMilliseconds(150));
        SourceInitialized += (_, _) => NeverActivate();
        Loaded += (_, _) =>
        {
            _shown.Start();
            if (_animate)
            {
                PullIn(stone, word, tag);
            }
        };
    }

    /// <summary>Why the window could not be told never to take the foreground, or null when it was. There is no logger on this thread; <see cref="StartupSplash"/> writes it.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// Fades the window out once it has been up for its minimum and the pointer has stopped playing
    /// with it. Startup calls this when it is done; until then the window stays however long that takes.
    /// </summary>
    public void CloseWhenDue()
    {
        if (_closeTimer is not null || _closing)
        {
            return;
        }

        _closeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) =>
        {
            var sinceInteraction = _interacted is { } at ? _shown.Elapsed - at : (TimeSpan?)null;
            if (StartupSplash.RemainingDelay(_shown.Elapsed, sinceInteraction, _minimumShown) <= TimeSpan.Zero)
            {
                FadeAndClose(StartupSplash.FadeOut);
            }
        }, Dispatcher);
    }

    private void FadeAndClose(TimeSpan fade)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _closeTimer?.Stop();
        if (!_animate)
        {
            Close();
            return;
        }

        var fadeOut = new DoubleAnimation(0, fade);
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Counts the pointer as playing only when it has really moved. WPF also raises MouseMove when the
    /// scene under a still pointer changes, and this scene is all motion: without the comparison a
    /// pointer left resting mid-screen at sign-in would hold the splash up for good.
    /// </summary>
    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        var now = e.GetPosition(this);
        if (_pointer is { } before && (now - before).Length > 0.5)
        {
            _interacted = _shown.Elapsed;
        }

        _pointer = now;
    }

    private void OnStonePressed(object sender, MouseButtonEventArgs e)
    {
        // Handled, so the click does not go on to the window, where a click dismisses.
        e.Handled = true;
        _interacted = _shown.Elapsed;
        if (_animate && !_closing)
        {
            ThrowAndPullBack();
        }
    }

    private void PullIn(FrameworkElement stone, TextBlock word, TextBlock tag)
    {
        var settle = new CubicEase { EasingMode = EasingMode.EaseOut };
        stone.BeginAnimation(OpacityProperty, Tween(0, 1, 320, 0, settle));
        _stoneSize.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(0.8, 1, 320, 0, settle));
        _stoneSize.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(0.8, 1, 320, 0, settle));

        for (var i = 0; i < _tiles.Count; i++)
        {
            // Past its place and back: the overshoot is what reads as a magnet rather than a slide.
            var snap = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
            var (tile, move, turn, size) = _tiles[i];
            var (_, _, dx, dy, angle, _) = Tiles[i];
            var delay = 160 + 80 * i;
            tile.BeginAnimation(OpacityProperty, Tween(0, 1, 300, delay, null));
            move.BeginAnimation(TranslateTransform.XProperty, Tween(dx, 0, 560, delay, snap));
            move.BeginAnimation(TranslateTransform.YProperty, Tween(dy, 0, 560, delay, snap));
            turn.BeginAnimation(RotateTransform.AngleProperty, Tween(angle, 0, 560, delay, snap));
            size.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(0.86, 1, 560, delay, snap));
            size.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(0.86, 1, 560, delay, snap));
        }

        Rise(word, 880, settle);
        Rise(tag, 1020, settle);
    }

    private void ThrowAndPullBack()
    {
        var kick = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.2 };
        _stoneSize.BeginAnimation(ScaleTransform.ScaleXProperty, OutAndBack(1, 1.16, 138, 460, 0, kick, kick));
        _stoneSize.BeginAnimation(ScaleTransform.ScaleYProperty, OutAndBack(1, 1.16, 138, 460, 0, kick, kick));

        var spread = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _ring.BeginAnimation(OpacityProperty, Tween(0.6, 0, 800, 0, spread));
        _ringSize.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(0.9, 3.6, 800, 0, spread));
        _ringSize.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(0.9, 3.6, 800, 0, spread));

        var away = new CubicEase { EasingMode = EasingMode.EaseOut };
        var home = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
        for (var i = 0; i < _tiles.Count; i++)
        {
            var (tile, move, turn, size) = _tiles[i];
            var (_, _, dx, dy, angle, _) = Tiles[i];
            var delay = 40 * i;

            // A click during the pull-in lands on a window that is still transparent; make sure of it.
            tile.BeginAnimation(OpacityProperty, null);
            tile.Opacity = 1;
            move.BeginAnimation(TranslateTransform.XProperty, OutAndBack(0, dx, 240, 760, delay, away, home));
            move.BeginAnimation(TranslateTransform.YProperty, OutAndBack(0, dy, 240, 760, delay, away, home));
            turn.BeginAnimation(RotateTransform.AngleProperty, OutAndBack(0, angle, 240, 760, delay, away, home));
            size.BeginAnimation(ScaleTransform.ScaleXProperty, OutAndBack(1, 0.86, 240, 760, delay, away, home));
            size.BeginAnimation(ScaleTransform.ScaleYProperty, OutAndBack(1, 0.86, 240, 760, delay, away, home));
        }
    }

    private static void Rise(TextBlock line, int beginMs, IEasingFunction ease)
    {
        var lift = new TranslateTransform(0, 7);
        line.RenderTransform = lift;
        line.Opacity = 0;
        line.BeginAnimation(OpacityProperty, Tween(0, 1, 360, beginMs, ease));
        lift.BeginAnimation(TranslateTransform.YProperty, Tween(7, 0, 360, beginMs, ease));
    }

    private static DoubleAnimation Tween(double from, double to, int ms, int beginMs, IEasingFunction? ease) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = ease };

    private static DoubleAnimationUsingKeyFrames OutAndBack(double rest, double far, int farMs, int totalMs, int beginMs, IEasingFunction outEase, IEasingFunction backEase) =>
        new()
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(far, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(farMs)), outEase),
                new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs)), backEase),
            },
        };

    private static TextBlock Line(string text, double size, FontWeight weight, string family, Color colour, double top)
    {
        var line = new TextBlock
        {
            Style = null,
            Text = text,
            Width = PlateWidth,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily(family),
            FontSize = size,
            FontWeight = weight,
            Foreground = LoadstoneArt.Frozen(new SolidColorBrush(colour)),
            IsHitTestVisible = false,
        };
        Canvas.SetTop(line, top);
        return line;
    }

    /// <summary>
    /// WS_EX_NOACTIVATE keeps a click on the stone from taking the foreground, which ShowActivated
    /// alone does not; WS_EX_TOOLWINDOW keeps the window out of Alt+Tab. Like the read,
    /// SetWindowLongPtr returns 0 both for a previous value of 0 and for failure, so the thread's last
    /// error - cleared first - is what tells the two apart.
    /// </summary>
    private void NeverActivate()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (WindowStyles.StyleWord(handle, NativeConstants.GWL_EXSTYLE) is not { } exStyle)
        {
            Problem = "could not read the splash window's extended style; a click on it may take the foreground";
            return;
        }

        var wanted = (nint)exStyle | (nint)(NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE);
        Marshal.SetLastSystemError(0);
        var previous = User32.SetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE, wanted);
        var error = Marshal.GetLastWin32Error();
        if (previous == 0 && error != 0)
        {
            Problem = $"could not stop the splash window taking the foreground: error {error}";
        }
    }
}
