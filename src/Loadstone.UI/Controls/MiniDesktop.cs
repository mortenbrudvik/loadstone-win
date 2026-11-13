using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.UI.Input;
using Point = Loadstone.Core.Geometry.Point;
using Rect = Loadstone.Core.Geometry.Rect;

namespace Loadstone.UI.Controls;

/// <summary>
/// The About tab's desktop in miniature: a display with its taskbar, three windows, and the stone in
/// the middle. A click on the stone presses the next shortcut - the keys float up and a window takes
/// that tile - and a window dragged to an edge or a corner snaps there, with the snap preview showing
/// where. Left alone, the stone gives off a slow pulse.
/// </summary>
/// <remarks>
/// This draws and animates; <see cref="MiniDesktopModel"/> decides, in the pixels of the display it
/// imagines, and this scales those down to whatever width it is given. With animation effects turned
/// off in Windows nothing moves by itself: a press or a drop still lands, at once, and the pulse, the
/// floating keys and the opening pull are left out.
/// </remarks>
internal sealed class MiniDesktop : Canvas
{
    public const string Hint = "Click the stone, or drag a window to an edge.";

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(MiniDesktop), new PropertyMetadata(Hint));

    private const double Rounding = 9;

    /// <summary>The gap drawn round each window, so tiles that meet exactly still read as separate windows.</summary>
    private const double Gap = 1.5;

    private const int MaxFloaters = 8;
    private static readonly TimeSpan SnapTime = TimeSpan.FromMilliseconds(440);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(3.4);

    /// <summary>Where the windows lie loose before the stone pulls them in, in display pixels, and how far each is turned.</summary>
    private static readonly (int X, int Y, double Angle)[] Loose = [(110, 60, -14), (1150, 20, 10), (1080, 340, -9)];

    /// <summary>How far sideways each floating set of keys drifts as it rises, in turn, so successive ones do not stack.</summary>
    private static readonly double[] Drift = [-26, 18, -8, 28, -18, 10];

    private static readonly WindowTint[] Tints = [WindowTint.Blue, WindowTint.Sand, WindowTint.Slate];

    private readonly MiniDesktopModel _model = new();
    private readonly Border[] _windows = new Border[MiniDesktopModel.WindowCount];
    private readonly RotateTransform[] _turns = new RotateTransform[MiniDesktopModel.WindowCount];
    private readonly TextBlock _wallpaperMark;
    private readonly List<Run> _wallpaperMarkSubscripts = [];
    private readonly Border _taskbar;
    private readonly Border _preview;
    private readonly Border _edge;
    private readonly Button _stone;
    private readonly FrameworkElement _stoneImage;
    private readonly ScaleTransform _stoneSize = new(1, 1);
    private readonly List<FrameworkElement> _floaters = [];
    private readonly DispatcherTimer _heartbeat;
    private (int Window, System.Windows.Point Down, Point Last, bool Moved)? _drag;
    private double _scale;
    private int _presses;

    public MiniDesktop()
    {
        Background = LoadstoneArt.Frozen(new RadialGradientBrush(Color.FromRgb(0x34, 0x40, 0x4F), Color.FromRgb(0x1B, 0x20, 0x29))
        {
            GradientOrigin = new System.Windows.Point(0.3, 0),
            Center = new System.Windows.Point(0.3, 0),
            RadiusX = 1.1,
            RadiusY = 1.4,
        });
        ClipToBounds = true;
        AutomationProperties.SetName(this, "A desktop in miniature");

        // Magnetite, what a loadstone is made of, set into the wallpaper. A window covers it when the tab
        // opens; it shows once that window has been sent somewhere else.
        _wallpaperMark = new TextBlock
        {
            Style = null,
            FontFamily = new FontFamily("Cambria, Georgia, Times New Roman"),
            Foreground = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))),
            IsHitTestVisible = false,
        };
        foreach (var (text, lowered) in new[] { ("Fe", false), ("3", true), ("O", false), ("4", true) })
        {
            var run = new Run(text) { BaselineAlignment = lowered ? BaselineAlignment.Subscript : BaselineAlignment.Baseline };
            _wallpaperMark.Inlines.Add(run);
            if (lowered)
            {
                _wallpaperMarkSubscripts.Add(run);
            }
        }

        SetZIndex(_wallpaperMark, 1);
        Children.Add(_wallpaperMark);

        for (var i = 0; i < _windows.Length; i++)
        {
            var index = i;
            var window = LoadstoneArt.NewWindow(Tints[i], cornerRadius: 5, titleHeight: 9);
            window.BorderBrush = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)));
            window.BorderThickness = new Thickness(1);
            window.Cursor = Cursors.SizeAll;
            window.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            window.RenderTransform = _turns[i] = new RotateTransform(0);
            window.MouseLeftButtonDown += (_, e) => OnWindowPressed(index, e);
            window.MouseMove += (_, e) => OnWindowDragged(index, e);
            window.MouseLeftButtonUp += (_, e) => OnWindowReleased(index, e);
            window.LostMouseCapture += (_, _) => EndDrag(index);
            _windows[i] = window;
            Children.Add(window);
        }

        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dot = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF)));
        for (var i = 0; i < 5; i++)
        {
            dots.Children.Add(new Rectangle { Width = 5, Height = 5, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(2, 0, 2, 0), Fill = dot });
        }

        _taskbar = new Border
        {
            Background = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x0C, 0x0E, 0x12))),
            Child = dots,
            IsHitTestVisible = false,
        };
        SetZIndex(_taskbar, 30);
        Children.Add(_taskbar);

        _preview = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        SetZIndex(_preview, 40);
        Children.Add(_preview);

        _stoneImage = LoadstoneArt.NewStone(70);
        _stone = new Button
        {
            // No style, so the Fluent button chrome stays off it; the template is the picture alone,
            // and the theme's own focus rectangle still shows for the keyboard.
            Style = null,
            Template = ContentOnly(),
            Content = _stoneImage,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = _stoneSize,
            ToolTip = "Press the next shortcut",
        };
        AutomationProperties.SetName(_stone, "Press the next shortcut");
        _stone.Click += (_, _) => Press();
        SetZIndex(_stone, 50);
        Children.Add(_stone);

        _edge = new Border
        {
            CornerRadius = new CornerRadius(Rounding),
            BorderBrush = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        SetZIndex(_edge, 100);
        Children.Add(_edge);

        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = Heartbeat };
        _heartbeat.Tick += (_, _) =>
        {
            if (Animates && _drag is null)
            {
                Pulse(0.4, 1100);
            }
        };
        Loaded += (_, _) => OnShown();
        Unloaded += (_, _) => _heartbeat.Stop();
    }

    /// <summary>What the last press or drop did, for the caption under the desktop and for a screen reader.</summary>
    public string Status
    {
        get => (string)GetValue(StatusProperty);
        private set => SetCurrentValue(StatusProperty, value);
    }

    /// <summary>The chord each command is bound to, so the keys shown are the user's own. The defaults until the window says otherwise.</summary>
    public Func<WindowCommand, Shortcut?> ShortcutOf { get; set; } = command => command.DefaultShortcut();

    private static bool Animates => SystemParameters.ClientAreaAnimation;

    /// <summary>Presses the next shortcut in the script, as a click on the stone does.</summary>
    internal void Press()
    {
        var press = _model.Press();
        Restack();
        Place(_windows[press.Window], press.Frame, animate: true);

        var title = press.Command.Title();
        var shortcut = ShortcutOf(press.Command);
        Status = shortcut is null ? $"{title}  ·  not bound" : $"{title}  ·  {shortcut.DisplayText}";
        if (!Animates)
        {
            return;
        }

        var kick = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.2 };
        _stoneSize.BeginAnimation(ScaleTransform.ScaleXProperty, OutAndBack(1.16, kick));
        _stoneSize.BeginAnimation(ScaleTransform.ScaleYProperty, OutAndBack(1.16, kick));
        Pulse(0.6, 800);
        Float(shortcut is null ? [title] : Keys(shortcut));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        var screen = MiniDesktopModel.Screen.Frame;
        var width = double.IsInfinity(constraint.Width) ? screen.Width / 4.0 : constraint.Width;
        return new Size(width, width * screen.Height / screen.Width);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _scale = ActualWidth / MiniDesktopModel.Screen.Frame.Width;
        Clip = new RectangleGeometry(new System.Windows.Rect(0, 0, ActualWidth, ActualHeight), Rounding, Rounding);

        var screen = MiniDesktopModel.Screen;
        _edge.Width = ActualWidth;
        _edge.Height = ActualHeight;
        _taskbar.Width = ActualWidth;
        _taskbar.Height = (screen.Frame.Height - screen.WorkArea.Height) * _scale;
        SetTop(_taskbar, screen.WorkArea.Height * _scale);

        // Centred in the upper right quarter of the work area.
        // The alignment only lowers a subscript; making it smaller is the run's own font size.
        _wallpaperMark.FontSize = Math.Max(12, 150 * _scale);
        foreach (var subscript in _wallpaperMarkSubscripts)
        {
            subscript.FontSize = _wallpaperMark.FontSize * 0.6;
        }

        _wallpaperMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_wallpaperMark, screen.WorkArea.Width * 0.75 * _scale - _wallpaperMark.DesiredSize.Width / 2);
        SetTop(_wallpaperMark, screen.WorkArea.Height * 0.25 * _scale - _wallpaperMark.DesiredSize.Height / 2);

        var stone = Math.Max(40, 300 * _scale);
        _stoneImage.Width = _stoneImage.Height = stone;
        var centre = screen.WorkArea.Center;
        SetLeft(_stone, centre.X * _scale - stone / 2);
        SetTop(_stone, centre.Y * _scale - stone / 2);

        Restack();
        for (var i = 0; i < _windows.Length; i++)
        {
            Place(_windows[i], _model.FrameOf(i), animate: false);
        }
    }

    /// <summary>
    /// Every time the tab comes into view the windows start loose and are pulled into place, wherever
    /// the last visit left them: the splash in miniature. A tab control unloads the tab it leaves, so
    /// this runs on each return, and the pulse stops while nobody is looking.
    /// </summary>
    private void OnShown()
    {
        _drag = null;
        _preview.Visibility = Visibility.Collapsed;
        _heartbeat.Start();
        if (!Animates || _scale <= 0)
        {
            return;
        }

        var (width, height) = MiniDesktopModel.Floating;
        for (var i = 0; i < _windows.Length; i++)
        {
            var delay = 350 + 120 * i;
            var (x, y, angle) = Loose[i];
            Place(_windows[i], new Rect(x, y, width, height), animate: false);
            Place(_windows[i], _model.FrameOf(i), animate: true, delay);
            _turns[i].BeginAnimation(RotateTransform.AngleProperty, Glide(angle, 0, delay));
        }
    }

    private void OnWindowPressed(int window, MouseButtonEventArgs e)
    {
        var down = e.GetPosition(this);
        _drag = (window, down, OnScreen(down), false);
        _windows[window].CaptureMouse();
        e.Handled = true;
    }

    private void OnWindowDragged(int window, MouseEventArgs e)
    {
        if (_drag is not { } drag || drag.Window != window || !_windows[window].IsMouseCaptured)
        {
            return;
        }

        var now = e.GetPosition(this);
        if (!drag.Moved)
        {
            // A click is not a drag: the window stays snapped until the pointer has really set off.
            if ((now - drag.Down).Length < 4)
            {
                return;
            }

            _model.PickUp(window, OnScreen(drag.Down));
            Restack();
        }

        var pointer = OnScreen(now);
        _drag = (window, drag.Down, pointer, true);
        Place(_windows[window], _model.DragTo(window, pointer), animate: false);
        ShowPreview(MiniDesktopModel.TileAt(pointer));
    }

    private void OnWindowReleased(int window, MouseButtonEventArgs e)
    {
        if (_drag is { } drag && drag.Window == window)
        {
            _drag = (window, drag.Down, drag.Moved ? OnScreen(e.GetPosition(this)) : drag.Last, drag.Moved);
        }

        // Releasing the capture is what ends the drag, so a capture lost any other way ends it too.
        _windows[window].ReleaseMouseCapture();
        e.Handled = true;
    }

    private void EndDrag(int window)
    {
        if (_drag is not { } drag || drag.Window != window)
        {
            return;
        }

        _drag = null;
        ShowPreview(null);
        if (!drag.Moved)
        {
            return;
        }

        var drop = _model.Drop(window, drag.Last);
        Place(_windows[window], drop.Frame, animate: true);
        Status = drop.Tile is { } tile
            ? $"{tile.Title()}  ·  dragged to {Where(tile)}"
            : "Dropped away from an edge, so it stays where it is.";
    }

    private void ShowPreview(Tile? tile)
    {
        if (tile is not { } target)
        {
            _preview.Visibility = Visibility.Collapsed;
            return;
        }

        // The snap preview's own colours, read as it reads them, so the miniature matches the real one.
        var (fill, border) = SnapPreview.Colours(SystemParameters.WindowGlassColor, SystemParameters.HighContrast);
        _preview.Background = new SolidColorBrush(fill);
        _preview.BorderBrush = new SolidColorBrush(border);
        Place(_preview, target.Frame(MiniDesktopModel.Screen.WorkArea), animate: false);
        _preview.Visibility = Visibility.Visible;
    }

    /// <summary>A pointer position as a pixel of the imagined display, held onto it the way a real display holds the cursor.</summary>
    private Point OnScreen(System.Windows.Point position)
    {
        var frame = MiniDesktopModel.Screen.Frame;
        var scale = _scale > 0 ? _scale : 1;
        return new Point(
            Math.Clamp((int)Math.Round(position.X / scale), frame.Left, frame.Right - 1),
            Math.Clamp((int)Math.Round(position.Y / scale), frame.Top, frame.Bottom - 1));
    }

    private void Restack()
    {
        for (var depth = 0; depth < _model.BackToFront.Count; depth++)
        {
            SetZIndex(_windows[_model.BackToFront[depth]], 10 + depth);
        }
    }

    private void Place(FrameworkElement element, Rect frame, bool animate, int delayMs = 0)
    {
        Move(element, LeftProperty, frame.X * _scale + Gap, animate, delayMs);
        Move(element, TopProperty, frame.Y * _scale + Gap, animate, delayMs);
        Move(element, WidthProperty, Math.Max(0, frame.Width * _scale - 2 * Gap), animate, delayMs);
        Move(element, HeightProperty, Math.Max(0, frame.Height * _scale - 2 * Gap), animate, delayMs);
    }

    /// <summary>
    /// Sets the property to <paramref name="to"/> and, when animating, glides there from wherever it
    /// is showing now. The value itself is set first and the animation stops rather than holds, so
    /// once it has run out the property simply has its value and the next drag can set it directly.
    /// </summary>
    private static void Move(FrameworkElement element, DependencyProperty property, double to, bool animate, int delayMs)
    {
        var from = (double)element.GetValue(property);
        element.BeginAnimation(property, null);
        element.SetValue(property, to);
        if (animate && Animates && !double.IsNaN(from))
        {
            element.BeginAnimation(property, Glide(from, to, delayMs));
        }
    }

    /// <summary>
    /// From one value to another, past it and back, after an optional wait. The wait is a held first
    /// key frame and not a begin time: before its begin time an animation shows nothing, and the
    /// property would sit at the value it is going to.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Glide(double from, double to, int delayMs)
    {
        var delay = TimeSpan.FromMilliseconds(delayMs);
        return new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop,
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)),
                new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + SnapTime), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }),
            },
        };
    }

    private static DoubleAnimationUsingKeyFrames OutAndBack(double far, IEasingFunction ease) => new()
    {
        FillBehavior = FillBehavior.Stop,
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new EasingDoubleKeyFrame(far, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(138)), ease),
            new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)), ease),
        },
    };

    /// <summary>A ring that spreads from the stone and fades, in the accent colour.</summary>
    private void Pulse(double strength, int ms)
    {
        var accent = SystemParameters.WindowGlassColor;
        var size = new ScaleTransform(0.9, 0.9);
        var width = _stoneImage.Width * 0.88;
        var height = _stoneImage.Width * 0.7;
        var ring = new Ellipse
        {
            Width = width,
            Height = height,
            Stroke = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B)),
            StrokeThickness = 1.5,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = size,
        };
        var centre = MiniDesktopModel.Screen.WorkArea.Center;
        SetLeft(ring, centre.X * _scale - width / 2);
        SetTop(ring, centre.Y * _scale - height / 2);
        SetZIndex(ring, 45);
        Children.Add(ring);

        var duration = TimeSpan.FromMilliseconds(ms);
        var spread = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(strength, 0, duration) { EasingFunction = spread, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, fade);
        size.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 3.4, duration) { EasingFunction = spread });
        size.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 3.4, duration) { EasingFunction = spread });
    }

    /// <summary>The keys of the chord just pressed, rising from the stone and fading.</summary>
    private void Float(IEnumerable<string> keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false, Opacity = 0 };
        foreach (var key in keys)
        {
            row.Children.Add(new Border
            {
                Background = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A))),
                BorderBrush = LoadstoneArt.Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A))),
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 3),
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Child = new TextBlock { Style = null, Text = key, FontSize = 11, Foreground = Brushes.White },
            });
        }

        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var centre = MiniDesktopModel.Screen.WorkArea.Center;
        SetLeft(row, centre.X * _scale - row.DesiredSize.Width / 2);
        SetTop(row, centre.Y * _scale - _stoneImage.Height / 2 - row.DesiredSize.Height + 6);
        SetZIndex(row, 60);

        var rise = new TranslateTransform();
        row.RenderTransform = rise;
        Children.Add(row);
        _floaters.Add(row);
        while (_floaters.Count > MaxFloaters)
        {
            Children.Remove(_floaters[0]);
            _floaters.RemoveAt(0);
        }

        var duration = TimeSpan.FromMilliseconds(1250);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop,
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)),
                new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)),
                new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)),
            },
            Duration = duration,
        };
        fade.Completed += (_, _) =>
        {
            Children.Remove(row);
            _floaters.Remove(row);
        };
        row.BeginAnimation(OpacityProperty, fade);
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -46, duration) { EasingFunction = ease });
        rise.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Drift[_presses++ % Drift.Length], duration) { EasingFunction = ease });
    }

    private static IEnumerable<string> Keys(Shortcut shortcut)
    {
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Control))
        {
            yield return "Ctrl";
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            yield return "Alt";
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            yield return "Shift";
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Win))
        {
            yield return "Win";
        }

        yield return VirtualKeys.DisplayName(shortcut.VirtualKey);
    }

    private static string Where(Tile tile) => tile switch
    {
        Tile.LeftHalf => "the left edge",
        Tile.RightHalf => "the right edge",
        Tile.Maximize => "the top edge",
        Tile.TopLeft or Tile.TopRight or Tile.BottomLeft or Tile.BottomRight => "a corner",
        Tile.LeftThird or Tile.CenterThird or Tile.RightThird => "the bottom edge",
        _ => "an edge",
    };

    private static ControlTemplate ContentOnly()
    {
        var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        template.Seal();
        return template;
    }
}
