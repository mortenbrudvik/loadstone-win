using Loadstone.Core.Geometry;

namespace Loadstone.UI.Input;

internal interface ISnapPreview
{
    /// <summary>
    /// Shows the preview over <paramref name="frame"/> (physical pixels). A frame too small to hold
    /// the inset shows nothing at all rather than an inverted rectangle.
    /// </summary>
    void Show(Rect frame);

    void Hide();
}
