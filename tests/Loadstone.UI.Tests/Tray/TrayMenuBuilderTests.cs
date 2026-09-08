using Loadstone.Core.Commands;
using Loadstone.UI.Tray;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Tray;

public class TrayMenuBuilderTests
{
    [Fact]
    public void Build_ListsEveryCommandInSectionOrder_WithSeparatorsAndFixedItems()
    {
        var entries = TrayMenuBuilder.Build(c => c.DefaultShortcut());

        var commands = entries.Where(e => e.Kind == TrayMenuEntryKind.Command).Select(e => e.Command!.Value).ToList();
        commands.ShouldBe(WindowCommands.All);

        // One separator after each of the five sections, one before Quit.
        entries.Count(e => e.Kind == TrayMenuEntryKind.Separator).ShouldBe(6);

        var tail = entries.Skip(entries.Count - 4).Select(e => e.Kind).ToList();
        tail.ShouldBe([TrayMenuEntryKind.Settings, TrayMenuEntryKind.Help, TrayMenuEntryKind.Separator, TrayMenuEntryKind.Quit]);
        entries.Single(e => e.Kind == TrayMenuEntryKind.Settings).Title.ShouldBe("Settings…");
        entries.Single(e => e.Kind == TrayMenuEntryKind.Help).Title.ShouldBe("Loadstone Help");
        entries.Single(e => e.Kind == TrayMenuEntryKind.Quit).Title.ShouldBe("Quit Loadstone");
    }

    [Fact]
    public void Build_SectionsAreSeparated()
    {
        var entries = TrayMenuBuilder.Build(c => c.DefaultShortcut());
        var firstSeparator = entries.ToList().FindIndex(e => e.Kind == TrayMenuEntryKind.Separator);
        firstSeparator.ShouldBe(4);
        entries[firstSeparator + 1].Command.ShouldBe(WindowCommand.TopLeft);
    }

    [Fact]
    public void Build_ShowsTheEffectiveChordAsGestureText()
    {
        var entries = TrayMenuBuilder.Build(c => c == WindowCommand.Center ? null : c.DefaultShortcut());
        entries.Single(e => e.Command == WindowCommand.LeftHalf).Gesture.ShouldBe("Ctrl+Alt+←");
        entries.Single(e => e.Command == WindowCommand.Center).Gesture.ShouldBeNull();
        entries.Single(e => e.Command == WindowCommand.TopLeft).Title.ShouldBe("Top Left Corner");
    }
}
