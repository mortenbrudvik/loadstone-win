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

/// <summary>What became of a command, so callers can log it and give the user a cue.</summary>
public readonly record struct CommandOutcome(OutcomeKind Kind, int Win32Error = 0)
{
    public static CommandOutcome Moved => new(OutcomeKind.Moved);
    public static CommandOutcome FrameUnreadable => new(OutcomeKind.FrameUnreadable);
    public static CommandOutcome NoDisplay => new(OutcomeKind.NoDisplay);
    public static CommandOutcome NoOtherDisplay => new(OutcomeKind.NoOtherDisplay);
    public static CommandOutcome NothingToRestore => new(OutcomeKind.NothingToRestore);
    public static CommandOutcome Rejected(int win32Error) => new(OutcomeKind.Rejected, win32Error);

    public bool IsMoved => Kind == OutcomeKind.Moved;

    public override string ToString() => Kind == OutcomeKind.Rejected ? $"Rejected({Win32Error})" : Kind.ToString();
}
