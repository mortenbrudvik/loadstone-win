using Loadstone.Core.Commands;
using Loadstone.UI.Controls;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Controls;

public class RecorderKeyHandlerTests
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;

    [Fact]
    public void Handle_ModifierWithKey_Commits()
    {
        var action = RecorderKeyHandler.Handle(CtrlAlt, 'E');
        action.Kind.ShouldBe(RecorderActionKind.Commit);
        action.Shortcut.ShouldBe(new Shortcut(CtrlAlt, 'E'));
    }

    [Fact]
    public void Handle_FunctionKeyAlone_Commits()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.F1 + 4).ShouldBe(new RecorderAction(RecorderActionKind.Commit, new Shortcut(KeyModifiers.None, VirtualKeys.F1 + 4)));
    }

    [Fact]
    public void Handle_PlainKeyAlone_IsIgnored()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, 'E').Kind.ShouldBe(RecorderActionKind.Ignore);
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Left).Kind.ShouldBe(RecorderActionKind.Ignore);
    }

    [Theory]
    [InlineData(VirtualKeys.ShiftKey)]
    [InlineData(VirtualKeys.ControlKey)]
    [InlineData(VirtualKeys.MenuKey)]
    [InlineData(VirtualKeys.LWin)]
    [InlineData(VirtualKeys.RMenu)]
    public void Handle_LoneModifier_IsIgnored_EvenWithOthersHeld(int modifierKey)
    {
        RecorderKeyHandler.Handle(CtrlAlt, modifierKey).Kind.ShouldBe(RecorderActionKind.Ignore);
        RecorderKeyHandler.Handle(KeyModifiers.None, modifierKey).Kind.ShouldBe(RecorderActionKind.Ignore);
    }

    [Fact]
    public void Handle_Escape_Cancels()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Escape).Kind.ShouldBe(RecorderActionKind.Cancel);
        RecorderKeyHandler.Handle(CtrlAlt, VirtualKeys.Escape).Kind.ShouldBe(RecorderActionKind.Cancel);
    }

    [Fact]
    public void Handle_BackspaceOrDeleteAlone_Clears_ButWithModifiersCommits()
    {
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Backspace).Kind.ShouldBe(RecorderActionKind.Clear);
        RecorderKeyHandler.Handle(KeyModifiers.None, VirtualKeys.Delete).Kind.ShouldBe(RecorderActionKind.Clear);
        RecorderKeyHandler.Handle(CtrlAlt, VirtualKeys.Backspace).ShouldBe(new RecorderAction(RecorderActionKind.Commit, new Shortcut(CtrlAlt, VirtualKeys.Backspace)));
    }
}
