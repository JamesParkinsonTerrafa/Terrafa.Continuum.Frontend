using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Diagram;

public enum PortSide
{
    Left,
    Right
}

public class PortMarker : Control
{
    public const double MarkerWidth = 10;
    public const double MarkerHeight = 20;
    public const double Bulge = 7;

    private readonly PortSide side;
    private readonly NodeCardVariant variant;
    private bool isHot;

    public PortMarker(PortSide side, NodeCardVariant variant)
    {
        this.side = side;
        this.variant = variant;
        Width = MarkerWidth;
        Height = MarkerHeight;
        ClipToBounds = true;
        PointerEntered += (_, _) => SetHot(true);
        PointerExited += (_, _) => SetHot(false);
    }

    private void SetHot(bool hot)
    {
        isHot = hot;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var accent = NodeCard.AccentFor(variant);
        var center = new Point(side == PortSide.Right ? 0 : MarkerWidth, MarkerHeight / 2);
        context.DrawEllipse(isHot ? accent : Palette.BgField, new Pen(accent, 1), center, Bulge, Bulge);
    }
}
