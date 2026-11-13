using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.UI.Controls;

/// <summary>
/// The desktop in miniature on the About tab: one display, three windows, and the two things Loadstone
/// does to a window. It holds no WPF, and it decides nothing itself: a press lays a tile out with
/// <see cref="TileExtensions.Frame"/> and a drag asks <see cref="SnapZones.TileAt"/>, the same two
/// calls the app makes, so the toy cannot drift from the product. Everything is in the pixels of an
/// imagined 1920 pixel wide display, which <see cref="MiniDesktop"/> draws scaled down.
/// </summary>
internal sealed class MiniDesktopModel
{
    /// <summary>
    /// The imagined display: 1920 by 780 with a 48 pixel taskbar along the bottom, at 100 percent. Wide
    /// and short so the whole tab fits the Settings window without scrolling; landscape, so the bottom
    /// band gives thirds. The zones are read against the whole frame and the tiles laid into the work area, as on
    /// a real one.
    /// </summary>
    public static readonly Display Screen = new(new Rect(0, 0, 1920, 780), new Rect(0, 0, 1920, 732), 96);

    public const int WindowCount = 3;

    /// <summary>The size a window takes when it is picked up, as a snapped window does on Windows.</summary>
    public static readonly (int Width, int Height) Floating = (620, 360);

    /// <summary>
    /// What each press of the stone does, in order, and to which window. It starts from
    /// <see cref="Opening"/> and its last step puts the first window back in the left half, so it
    /// reads as one loop: thirds, two thirds, a half, corners, maximize, and home.
    /// </summary>
    public static readonly IReadOnlyList<(int Window, WindowCommand Command)> Script =
    [
        (0, WindowCommand.LeftThird),
        (1, WindowCommand.CenterThird),
        (2, WindowCommand.RightThird),
        (0, WindowCommand.LeftTwoThirds),
        (2, WindowCommand.RightHalf),
        (1, WindowCommand.TopLeft),
        (0, WindowCommand.BottomLeft),
        (2, WindowCommand.Maximize),
        (2, WindowCommand.BottomRight),
        (1, WindowCommand.TopRight),
        (0, WindowCommand.LeftHalf),
    ];

    /// <summary>Where the three windows sit when the tab opens: a half and two quarters.</summary>
    public static readonly IReadOnlyList<Tile> Opening = [Tile.LeftHalf, Tile.TopRight, Tile.BottomRight];

    private readonly Rect[] _frames = new Rect[WindowCount];
    private readonly List<int> _order = [];
    private int _step;
    private (int Window, int Dx, int Dy)? _held;

    public MiniDesktopModel()
    {
        for (var window = 0; window < WindowCount; window++)
        {
            _frames[window] = Opening[window].Frame(Screen.WorkArea);
            _order.Add(window);
        }
    }

    public Rect FrameOf(int window) => _frames[window];

    /// <summary>The windows from the back to the front.</summary>
    public IReadOnlyList<int> BackToFront => _order;

    /// <summary>Presses the next shortcut in <see cref="Script"/>: that window comes to the front and takes the tile.</summary>
    public Press Press()
    {
        var (window, command) = Script[_step];
        _step = (_step + 1) % Script.Count;

        // Every scripted command is a tile command; ToTile is null only for Center, Restore and the
        // display moves, and MiniDesktopModelTests pins that none of those is in the script.
        var tile = command.ToTile() ?? throw new InvalidOperationException($"{command} lays out no tile");
        _frames[window] = tile.Frame(Screen.WorkArea);
        BringToFront(window);
        return new Press(window, command, _frames[window]);
    }

    /// <summary>
    /// Picks a window up by <paramref name="pointer"/>. It comes to the front and shrinks to
    /// <see cref="Floating"/> under the pointer, keeping the pointer the same share of the way along
    /// its width and the same distance below its top, which is what Windows does to a snapped window.
    /// </summary>
    public Rect PickUp(int window, Point pointer)
    {
        var frame = _frames[window];
        var along = frame.Width > 0 ? (pointer.X - frame.X) / (double)frame.Width : 0.5;
        var dx = (int)Math.Round(Math.Clamp(along, 0, 1) * Floating.Width);
        var dy = Math.Clamp(pointer.Y - frame.Y, 0, Floating.Height - 1);
        _held = (window, dx, dy);
        BringToFront(window);
        return MoveHeld(pointer);
    }

    /// <summary>Moves the held window with the pointer. A no-op answering the frame as it is when nothing is held.</summary>
    public Rect DragTo(int window, Point pointer) => _held?.Window == window ? MoveHeld(pointer) : _frames[window];

    /// <summary>The tile a drop at <paramref name="pointer"/> would snap to, by the app's own zones.</summary>
    public static Tile? TileAt(Point pointer) => SnapZones.TileAt(pointer, Screen);

    /// <summary>
    /// Lets the held window go. In a zone it takes that tile; anywhere else it stays where it was
    /// dropped, pulled back onto the work area if it hung over the edge.
    /// </summary>
    public Drop Drop(int window, Point pointer)
    {
        if (_held?.Window != window)
        {
            return new Drop(null, _frames[window]);
        }

        MoveHeld(pointer);
        _held = null;
        var tile = TileAt(pointer);
        var work = Screen.WorkArea;
        var frame = _frames[window];
        _frames[window] = tile is { } snapped
            ? snapped.Frame(work)
            : frame with
            {
                X = Math.Clamp(frame.X, work.Left, work.Right - frame.Width),
                Y = Math.Clamp(frame.Y, work.Top, work.Bottom - frame.Height),
            };
        return new Drop(tile, _frames[window]);
    }

    private Rect MoveHeld(Point pointer)
    {
        var (window, dx, dy) = _held!.Value;
        _frames[window] = new Rect(pointer.X - dx, pointer.Y - dy, Floating.Width, Floating.Height);
        return _frames[window];
    }

    private void BringToFront(int window)
    {
        _order.Remove(window);
        _order.Add(window);
    }
}

/// <summary>One press of the stone: the window that moved, the command that moved it, and where it went.</summary>
internal readonly record struct Press(int Window, WindowCommand Command, Rect Frame);

/// <summary>The end of a drag: the tile the window snapped to, or null when it was dropped away from every zone, and where it is.</summary>
internal readonly record struct Drop(Tile? Tile, Rect Frame);
