using Loadstone.Core.Geometry;

namespace Loadstone.Core.Commands;

public enum OutcomeKind
{
    Moved,
    /// <summary>The window's frame could not be read, so nothing was attempted.</summary>
    FrameUnreadable,
    /// <summary>No display is attached.</summary>
    NoDisplay,
    /// <summary>Next/Previous Display with only one display attached.</summary>
    NoOtherDisplay,
    /// <summary>Restore was asked for a window Loadstone has not moved (or has already restored).</summary>
    NothingToRestore,
    /// <summary>The write was refused (elevated process, destroyed window); Win32Error says why.</summary>
    Rejected,
}

/// <summary>
/// Left or Right Half pressed on a window already in that half, which went on toward the display on
/// <paramref name="Toward"/>: <paramref name="From"/> is the frame it was in the half at.
/// </summary>
public readonly record struct CarryOn(Side Toward, Rect From);

/// <summary>What became of a command, so callers can log it and give the user a cue.</summary>
/// <param name="CarryOn">
/// Set when the command carried the window on to the display beside, whether that write was accepted
/// (<see cref="OutcomeKind.Moved"/>) or refused (<see cref="OutcomeKind.Rejected"/>), so the log can
/// say which frame was involved; null for every other command.
/// </param>
public readonly record struct CommandOutcome(OutcomeKind Kind, int Win32Error = 0, CarryOn? CarryOn = null)
{
    public static CommandOutcome Moved => new(OutcomeKind.Moved);
    public static CommandOutcome FrameUnreadable => new(OutcomeKind.FrameUnreadable);
    public static CommandOutcome NoDisplay => new(OutcomeKind.NoDisplay);
    public static CommandOutcome NoOtherDisplay => new(OutcomeKind.NoOtherDisplay);
    public static CommandOutcome NothingToRestore => new(OutcomeKind.NothingToRestore);
    public static CommandOutcome Rejected(int win32Error) => new(OutcomeKind.Rejected, win32Error);
    public static CommandOutcome CarriedOn(Side toward, Rect from) => new(OutcomeKind.Moved, CarryOn: new CarryOn(toward, from));

    public bool IsMoved => Kind == OutcomeKind.Moved;

    public override string ToString()
    {
        var kind = Kind == OutcomeKind.Rejected ? $"Rejected({Win32Error})" : Kind.ToString();
        return CarryOn is { } carry ? $"{kind}, carrying on {carry.Toward} from {carry.From}" : kind;
    }
}
