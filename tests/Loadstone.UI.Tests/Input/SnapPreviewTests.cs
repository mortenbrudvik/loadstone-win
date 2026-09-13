using System.Windows;
using System.Windows.Media;
using Loadstone.UI.Input;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class SnapPreviewTests
{
    private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>The preview is a tint of the live accent: 22 percent fill, 85 percent border, so the window beneath still reads.</summary>
    [Fact]
    public void Colours_Normally_TintTheAccent()
    {
        var (fill, border) = SnapPreview.Colours(Accent, highContrast: false);

        fill.ShouldBe(Color.FromArgb(56, 0x00, 0x78, 0xD4));
        border.ShouldBe(Color.FromArgb(217, 0x00, 0x78, 0xD4));
    }

    /// <summary>
    /// A 22 percent tint of the DWM colour can be near-invisible on a black High Contrast desktop.
    /// There the preview takes the theme's own highlight colour, a solid border and a heavier fill.
    /// </summary>
    [WpfFact]
    public void Colours_InHighContrast_UseTheHighlightColourWithASolidBorder()
    {
        var highlight = SystemColors.HighlightColor;

        var (fill, border) = SnapPreview.Colours(Accent, highContrast: true);

        border.ShouldBe(Color.FromArgb(255, highlight.R, highlight.G, highlight.B));
        fill.R.ShouldBe(highlight.R);
        fill.G.ShouldBe(highlight.G);
        fill.B.ShouldBe(highlight.B);
        fill.A.ShouldBeGreaterThan((byte)56);
    }
}
