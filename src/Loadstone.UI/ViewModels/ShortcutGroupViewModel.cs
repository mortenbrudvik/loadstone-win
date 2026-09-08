namespace Loadstone.UI.ViewModels;

internal sealed record ShortcutGroupViewModel(string Header, IReadOnlyList<ShortcutRowViewModel> Rows);
