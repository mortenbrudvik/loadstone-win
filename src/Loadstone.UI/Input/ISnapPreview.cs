using Loadstone.Core.Geometry;

namespace Loadstone.UI.Input;

internal interface ISnapPreview
{
    /// <summary>Shows the preview over <paramref name="frame"/> (physical pixels).</summary>
    void Show(Rect frame);

    void Hide();
}
