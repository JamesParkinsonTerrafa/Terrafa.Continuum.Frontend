// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.TableGrid;

public class TableGridControl : Control
{
    private const double ScrollBarThickness = 10;
    private const double CellPadX = 8;
    private const double MinColumnWidth = 70;
    private const double MaxColumnWidth = 280;
    private const double WheelRowsPerNotch = 3;

    private static readonly Typeface GridTypeface = new(Palette.Font);

    private readonly double cellFontSize = TypographySettings.Size(10);
    private readonly double headerFontSize = TypographySettings.Size(9);
    private readonly double rowHeight;
    private readonly double headerHeight;
    private readonly Pen rowSeparatorPen = new(Palette.RowSeparator, 1);
    private readonly Pen columnRulePen = new(Palette.GridFaint, 1);
    private readonly Pen frozenEdgePen = new(Palette.Border, 1);
    private readonly Pen headerRulePen = new(Palette.Border, 1);
    private readonly Pen selectionPen = new(Palette.Amber, 1);

    private ITableDocument? document;
    private TableRowCache? cache;
    private double gutterWidth;
    private double topRow;
    private double leftPx;
    private int hoverRow = -1;
    private (int Row, int Column)? selected;
    private double[] columnWidths = [];
    private double[] columnOffsets = [];
    private bool widthsSampled;
    private bool draggingVertical;
    private bool draggingHorizontal;
    private double dragGrabOffset;

    public TableGridControl()
    {
        Focusable = true;
        ClipToBounds = true;
        rowHeight = Math.Ceiling(cellFontSize * 1.9);
        headerHeight = rowHeight + 6;
    }

    public event Action? ViewportChanged;

    public int FirstVisibleRow => (int)Math.Floor(topRow);

    public int VisibleRows =>
        Math.Max(1, (int)Math.Ceiling(BodyHeight / rowHeight));

    private double BodyHeight => Math.Max(Bounds.Height - headerHeight - HorizontalBarSpace, 1);

    private double BodyWidth => Math.Max(Bounds.Width - ScrollBarThickness, 1);

    private double HorizontalBarSpace => NeedsHorizontalBar ? ScrollBarThickness : 0;

    private bool NeedsHorizontalBar =>
        columnOffsets.Length > 0 && gutterWidth + columnOffsets[^1] > BodyWidth;

    private double FrozenWidth => gutterWidth + (columnWidths.Length > 0 ? columnWidths[0] : 0);

    private int TotalRows => document?.TotalRows ?? 0;

    private double MaxTopRow => Math.Max(0, TotalRows - Math.Floor(BodyHeight / rowHeight));

    private double MaxLeftPx =>
        columnOffsets.Length == 0 ? 0 : Math.Max(0, gutterWidth + columnOffsets[^1] - BodyWidth);

    public bool IsAttachedTo(ITableDocument tableDocument, TableRowCache rowCache) =>
        ReferenceEquals(document, tableDocument) && ReferenceEquals(cache, rowCache);

    public void Attach(ITableDocument tableDocument, TableRowCache rowCache)
    {
        document = tableDocument;
        cache = rowCache;
        widthsSampled = false;
        selected = null;
        hoverRow = -1;
        leftPx = 0;
        gutterWidth = Math.Ceiling(Format(
            Math.Max(tableDocument.TotalRows, 1).ToString(CultureInfo.InvariantCulture),
            Palette.TextFaint, cellFontSize).Width) + 2 * CellPadX;
        EstimateColumnWidths();
        topRow = Math.Clamp(rowCache.LastFirstRow, 0, MaxTopRow);
        NotifyViewport();
        InvalidateVisual();
    }

    public void Refresh()
    {
        if (!widthsSampled) TrySampleColumnWidths();
        InvalidateVisual();
        ViewportChanged?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Palette.BgField, new Rect(Bounds.Size));
        if (document is null || cache is null || columnWidths.Length == 0)
        {
            DrawCenteredNote(context, "NO TABLE — BUILD ONE FROM THE EXPORT PIPELINE");
            return;
        }

        if (TotalRows == 0)
        {
            DrawHeader(context);
            DrawCenteredNote(context, "0 ROWS");
            return;
        }

        var firstRow = FirstVisibleRow;
        var yOffset = headerHeight - (topRow - firstRow) * rowHeight;
        var rowsToDraw = Math.Min(VisibleRows + 1, TotalRows - firstRow);

        DrawHoverAndSelectionBackdrop(context, firstRow, yOffset, rowsToDraw);

        using (context.PushClip(new Rect(
                   FrozenWidth, headerHeight, Math.Max(BodyWidth - FrozenWidth, 0), BodyHeight)))
        {
            DrawCells(context, firstRow, rowsToDraw, yOffset, scrolledColumns: true);
        }

        using (context.PushClip(new Rect(0, headerHeight, FrozenWidth, BodyHeight)))
        {
            DrawCells(context, firstRow, rowsToDraw, yOffset, scrolledColumns: false);
        }

        DrawGutter(context, firstRow, rowsToDraw, yOffset);
        DrawRowSeparators(context, yOffset, rowsToDraw);
        DrawColumnRules(context);
        DrawHeader(context);
        DrawSelectionOutline(context, firstRow, yOffset, rowsToDraw);
        DrawVerticalScrollBar(context);
        DrawHorizontalScrollBar(context);
    }

    private void DrawHoverAndSelectionBackdrop(
        DrawingContext context, int firstRow, double yOffset, int rowsToDraw)
    {
        if (hoverRow < firstRow || hoverRow >= firstRow + rowsToDraw) return;
        var y = yOffset + (hoverRow - firstRow) * rowHeight;
        context.FillRectangle(Palette.BgPanel, new Rect(0, y, BodyWidth, rowHeight));
    }

    private void DrawCells(
        DrawingContext context, int firstRow, int rowsToDraw, double yOffset, bool scrolledColumns)
    {
        if (document is null || cache is null) return;

        var firstColumn = scrolledColumns ? 1 : 0;
        var lastColumn = scrolledColumns ? columnWidths.Length - 1 : 0;
        if (scrolledColumns)
        {
            var viewLeft = leftPx + columnWidths[0];
            var viewRight = leftPx + BodyWidth - gutterWidth;
            while (firstColumn < columnWidths.Length - 1
                   && columnOffsets[firstColumn] + columnWidths[firstColumn] < viewLeft)
            {
                firstColumn++;
            }

            lastColumn = firstColumn;
            while (lastColumn < columnWidths.Length - 1 && columnOffsets[lastColumn] < viewRight)
            {
                lastColumn++;
            }
        }

        for (var index = 0; index < rowsToDraw; index++)
        {
            var row = firstRow + index;
            var y = yOffset + index * rowHeight;
            var group = row / document.RowGroupSize;
            if (cache.TryGetRowGroup(group, out var rowGroup))
            {
                var groupRow = row - rowGroup.FirstRow;
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    DrawCellText(context, rowGroup, groupRow, row, column, y);
                }
            }
            else
            {
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    DrawMissPlaceholder(context, column, y);
                }
            }
        }
    }

    private void DrawCellText(
        DrawingContext context, TableRowGroup rowGroup, int groupRow, int row, int column, double y)
    {
        var value = rowGroup.Columns[column].Format(groupRow);
        if (value.Length == 0) return;

        var isSelected = selected is { } cell && cell.Row == row && cell.Column == column;
        var brush = column == 0
            ? Palette.TextMuted
            : isSelected ? Palette.TextBright : Palette.Text;
        var text = Format(value, brush, cellFontSize);
        var left = CellLeft(column);
        var width = columnWidths[column];
        var alignRight = document!.Columns[column].Kind == TableColumnKind.Number;
        var x = alignRight
            ? left + width - CellPadX - text.Width
            : left + CellPadX;
        context.DrawText(text, new Point(x, y + (rowHeight - text.Height) / 2));
    }

    private void DrawMissPlaceholder(DrawingContext context, int column, double y)
    {
        var left = CellLeft(column);
        var width = Math.Max(columnWidths[column] * 0.55, 8);
        context.FillRectangle(
            Palette.GridFaint,
            new Rect(left + CellPadX, y + rowHeight / 2 - 2, width, 3));
    }

    private void DrawGutter(DrawingContext context, int firstRow, int rowsToDraw, double yOffset)
    {
        using (context.PushClip(new Rect(0, headerHeight, gutterWidth, BodyHeight)))
        {
            for (var index = 0; index < rowsToDraw; index++)
            {
                var row = firstRow + index;
                var brush = selected is { } cell && cell.Row == row
                    ? Palette.TextMuted
                    : Palette.TextFaint;
                var text = Format((row + 1).ToString(CultureInfo.InvariantCulture), brush, cellFontSize);
                var y = yOffset + index * rowHeight;
                context.DrawText(
                    text,
                    new Point(gutterWidth - CellPadX - text.Width, y + (rowHeight - text.Height) / 2));
            }
        }
    }

    private void DrawRowSeparators(DrawingContext context, double yOffset, int rowsToDraw)
    {
        for (var index = 0; index <= rowsToDraw; index++)
        {
            var y = yOffset + index * rowHeight;
            if (y < headerHeight - 0.5 || y > headerHeight + BodyHeight) continue;
            context.DrawLine(rowSeparatorPen, new Point(0, y), new Point(BodyWidth, y));
        }
    }

    private void DrawColumnRules(DrawingContext context)
    {
        var bottom = headerHeight + BodyHeight;
        using (context.PushClip(new Rect(
                   FrozenWidth, 0, Math.Max(BodyWidth - FrozenWidth, 0), bottom)))
        {
            for (var column = 1; column < columnWidths.Length; column++)
            {
                var x = CellLeft(column) + columnWidths[column];
                context.DrawLine(columnRulePen, new Point(x, headerHeight), new Point(x, bottom));
            }
        }

        context.DrawLine(
            columnRulePen, new Point(gutterWidth, headerHeight), new Point(gutterWidth, bottom));
        context.DrawLine(frozenEdgePen, new Point(FrozenWidth, 0), new Point(FrozenWidth, bottom));
    }

    private void DrawHeader(DrawingContext context)
    {
        if (document is null) return;
        context.FillRectangle(Palette.BgPanel, new Rect(0, 0, Bounds.Width, headerHeight));

        using (context.PushClip(new Rect(
                   FrozenWidth, 0, Math.Max(BodyWidth - FrozenWidth, 0), headerHeight)))
        {
            for (var column = 1; column < columnWidths.Length; column++)
            {
                DrawHeaderLabel(context, column);
            }
        }

        DrawHeaderLabel(context, 0);
        context.DrawLine(
            headerRulePen, new Point(0, headerHeight), new Point(Bounds.Width, headerHeight));
    }

    private void DrawHeaderLabel(DrawingContext context, int column)
    {
        var text = Format(
            document!.Columns[column].Name.ToUpperInvariant(), Palette.TextMuted, headerFontSize);
        var left = CellLeft(column);
        var alignRight = document.Columns[column].Kind == TableColumnKind.Number;
        var x = alignRight
            ? left + columnWidths[column] - CellPadX - text.Width
            : left + CellPadX;
        context.DrawText(text, new Point(x, (headerHeight - text.Height) / 2));
    }

    private void DrawSelectionOutline(
        DrawingContext context, int firstRow, double yOffset, int rowsToDraw)
    {
        if (selected is not { } cell) return;
        if (cell.Row < firstRow || cell.Row >= firstRow + rowsToDraw) return;
        if (cell.Column >= columnWidths.Length) return;

        var y = yOffset + (cell.Row - firstRow) * rowHeight;
        var rect = new Rect(CellLeft(cell.Column) + 0.5, y + 0.5, columnWidths[cell.Column] - 1, rowHeight - 1);
        if (cell.Column > 0 && rect.Left < FrozenWidth) return;
        context.DrawRectangle(null, selectionPen, rect);
    }

    private void DrawVerticalScrollBar(DrawingContext context)
    {
        if (TotalRows == 0 || MaxTopRow <= 0) return;
        var track = VerticalTrack;
        context.FillRectangle(Palette.BgPanel, track);
        context.FillRectangle(Palette.BorderMid, VerticalThumb);
    }

    private void DrawHorizontalScrollBar(DrawingContext context)
    {
        if (!NeedsHorizontalBar) return;
        var track = HorizontalTrack;
        context.FillRectangle(Palette.BgPanel, track);
        context.FillRectangle(Palette.BorderMid, HorizontalThumb);
    }

    private Rect VerticalTrack =>
        new(Bounds.Width - ScrollBarThickness, headerHeight, ScrollBarThickness, BodyHeight);

    private Rect VerticalThumb
    {
        get
        {
            var track = VerticalTrack;
            var thumbHeight = Math.Max(24, track.Height * (VisibleRows / (double)Math.Max(TotalRows, 1)));
            thumbHeight = Math.Min(thumbHeight, track.Height);
            var travel = track.Height - thumbHeight;
            var position = MaxTopRow <= 0 ? 0 : topRow / MaxTopRow;
            return new Rect(track.X + 2, track.Y + travel * position, ScrollBarThickness - 4, thumbHeight);
        }
    }

    private Rect HorizontalTrack =>
        new(FrozenWidth, Bounds.Height - ScrollBarThickness,
            Math.Max(BodyWidth - FrozenWidth, 0), ScrollBarThickness);

    private Rect HorizontalThumb
    {
        get
        {
            var track = HorizontalTrack;
            var viewport = Math.Max(BodyWidth - FrozenWidth, 1);
            var content = Math.Max(
                columnOffsets.Length == 0 ? 1 : columnOffsets[^1] - columnWidths[0], viewport);
            var thumbWidth = Math.Max(24, track.Width * (viewport / content));
            thumbWidth = Math.Min(thumbWidth, track.Width);
            var travel = track.Width - thumbWidth;
            var position = MaxLeftPx <= 0 ? 0 : leftPx / MaxLeftPx;
            return new Rect(track.X + travel * position, track.Y + 2, thumbWidth, ScrollBarThickness - 4);
        }
    }

    private void DrawCenteredNote(DrawingContext context, string note)
    {
        var text = Format(note, Palette.TextMuted, cellFontSize);
        context.DrawText(text, new Point(
            (Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
    }

    private double CellLeft(int column) =>
        gutterWidth + (column == 0 ? 0 : columnOffsets[column] - leftPx);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SetLeftPx(leftPx - e.Delta.Y * 40);
        }
        else
        {
            SetTopRow(topRow - e.Delta.Y * WheelRowsPerNotch);
            if (e.Delta.X != 0) SetLeftPx(leftPx - e.Delta.X * 40);
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var position = e.GetPosition(this);
        Focus();

        if (VerticalThumb.Contains(position))
        {
            draggingVertical = true;
            dragGrabOffset = position.Y - VerticalThumb.Y;
            e.Pointer.Capture(this);
        }
        else if (MaxTopRow > 0 && VerticalTrack.Contains(position))
        {
            var page = position.Y < VerticalThumb.Y ? -VisibleRows : VisibleRows;
            SetTopRow(topRow + page);
        }
        else if (HorizontalThumb.Contains(position) && NeedsHorizontalBar)
        {
            draggingHorizontal = true;
            dragGrabOffset = position.X - HorizontalThumb.X;
            e.Pointer.Capture(this);
        }
        else if (NeedsHorizontalBar && HorizontalTrack.Contains(position))
        {
            var page = position.X < HorizontalThumb.X ? -(BodyWidth - FrozenWidth) : BodyWidth - FrozenWidth;
            SetLeftPx(leftPx + page);
        }
        else if (position.Y > headerHeight && position.Y < headerHeight + BodyHeight
                 && position.X < BodyWidth)
        {
            SelectCellAt(position);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);

        if (draggingVertical)
        {
            var track = VerticalTrack;
            var travel = Math.Max(track.Height - VerticalThumb.Height, 1);
            var fraction = Math.Clamp((position.Y - dragGrabOffset - track.Y) / travel, 0, 1);
            SetTopRow(fraction * MaxTopRow);
            e.Handled = true;
            return;
        }

        if (draggingHorizontal)
        {
            var track = HorizontalTrack;
            var travel = Math.Max(track.Width - HorizontalThumb.Width, 1);
            var fraction = Math.Clamp((position.X - dragGrabOffset - track.X) / travel, 0, 1);
            SetLeftPx(fraction * MaxLeftPx);
            e.Handled = true;
            return;
        }

        var row = RowAt(position);
        if (row != hoverRow)
        {
            hoverRow = row;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (draggingVertical || draggingHorizontal)
        {
            draggingVertical = false;
            draggingHorizontal = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (hoverRow != -1)
        {
            hoverRow = -1;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (document is null || TotalRows == 0) return;

        var cell = selected ?? (FirstVisibleRow, 0);
        var handled = true;
        switch (e.Key)
        {
            case Key.Down:
                cell = (Math.Min(cell.Item1 + 1, TotalRows - 1), cell.Item2);
                break;
            case Key.Up:
                cell = (Math.Max(cell.Item1 - 1, 0), cell.Item2);
                break;
            case Key.Right:
                cell = (cell.Item1, Math.Min(cell.Item2 + 1, columnWidths.Length - 1));
                break;
            case Key.Left:
                cell = (cell.Item1, Math.Max(cell.Item2 - 1, 0));
                break;
            case Key.PageDown:
                cell = (Math.Min(cell.Item1 + VisibleRows, TotalRows - 1), cell.Item2);
                break;
            case Key.PageUp:
                cell = (Math.Max(cell.Item1 - VisibleRows, 0), cell.Item2);
                break;
            case Key.Home when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                cell = (0, cell.Item2);
                break;
            case Key.End when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                cell = (TotalRows - 1, cell.Item2);
                break;
            case Key.Home:
                cell = (cell.Item1, 0);
                break;
            case Key.End:
                cell = (cell.Item1, columnWidths.Length - 1);
                break;
            default:
                handled = false;
                break;
        }

        if (!handled) return;
        selected = cell;
        EnsureRowVisible(cell.Item1);
        EnsureColumnVisible(cell.Item2);
        InvalidateVisual();
        e.Handled = true;
    }

    private void SelectCellAt(Point position)
    {
        var row = RowAt(position);
        if (row < 0) return;
        var column = ColumnAt(position.X);
        if (column < 0) return;
        selected = (row, column);
        InvalidateVisual();
    }

    private int RowAt(Point position)
    {
        if (position.Y <= headerHeight || position.Y >= headerHeight + BodyHeight) return -1;
        if (position.X >= BodyWidth) return -1;
        var row = (int)Math.Floor(topRow + (position.Y - headerHeight) / rowHeight);
        return row >= 0 && row < TotalRows ? row : -1;
    }

    private int ColumnAt(double x)
    {
        if (columnWidths.Length == 0) return -1;
        if (x < gutterWidth) return -1;
        if (x < FrozenWidth) return 0;
        var worldX = x - gutterWidth + leftPx;
        for (var column = 1; column < columnWidths.Length; column++)
        {
            if (worldX >= columnOffsets[column] && worldX < columnOffsets[column] + columnWidths[column])
            {
                return column;
            }
        }

        return -1;
    }

    private void EnsureRowVisible(int row)
    {
        var fullyVisible = Math.Max(1, (int)Math.Floor(BodyHeight / rowHeight));
        if (row < topRow) SetTopRow(row);
        else if (row >= topRow + fullyVisible) SetTopRow(row - fullyVisible + 1);
        else NotifyViewport();
    }

    private void EnsureColumnVisible(int column)
    {
        if (column == 0 || columnWidths.Length == 0) return;
        var left = columnOffsets[column];
        var right = left + columnWidths[column];
        if (left - leftPx < columnWidths[0]) SetLeftPx(left - columnWidths[0]);
        else if (right - leftPx > BodyWidth - gutterWidth) SetLeftPx(right - (BodyWidth - gutterWidth));
    }

    private void SetTopRow(double value)
    {
        var clamped = Math.Clamp(value, 0, MaxTopRow);
        topRow = clamped;
        NotifyViewport();
        InvalidateVisual();
    }

    private void SetLeftPx(double value)
    {
        leftPx = Math.Clamp(value, 0, MaxLeftPx);
        InvalidateVisual();
    }

    private void NotifyViewport()
    {
        cache?.OnViewport(FirstVisibleRow, VisibleRows);
        ViewportChanged?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (document is null) return;
        SetTopRow(topRow);
    }

    private void EstimateColumnWidths()
    {
        if (document is null)
        {
            columnWidths = [];
            columnOffsets = [];
            return;
        }

        var widths = new double[document.Columns.Count];
        for (var column = 0; column < widths.Length; column++)
        {
            var header = Format(
                document.Columns[column].Name.ToUpperInvariant(), Palette.TextMuted, headerFontSize);
            var body = document.Columns[column].Kind switch
            {
                TableColumnKind.Timestamp => Format("2020-01-01 00:00", Palette.Text, cellFontSize).Width,
                TableColumnKind.Number => Format("-12345.678", Palette.Text, cellFontSize).Width,
                _ => Format("MMMMMMMM", Palette.Text, cellFontSize).Width
            };
            widths[column] = Math.Clamp(
                Math.Max(header.Width, body) + 2 * CellPadX, MinColumnWidth, MaxColumnWidth);
        }

        ApplyColumnWidths(widths);
    }

    private void TrySampleColumnWidths()
    {
        if (document is null || cache is null) return;
        var group = FirstVisibleRow / Math.Max(document.RowGroupSize, 1);
        if (!cache.TryGetRowGroup(group, out var rowGroup)) return;

        var widths = new double[document.Columns.Count];
        var sampleRows = new[] { 0, rowGroup.RowCount / 2, rowGroup.RowCount - 1 };
        for (var column = 0; column < widths.Length; column++)
        {
            var header = Format(
                document.Columns[column].Name.ToUpperInvariant(), Palette.TextMuted, headerFontSize);
            var widest = header.Width;
            foreach (var row in sampleRows)
            {
                var text = rowGroup.Columns[column].Format(row);
                if (text.Length == 0) continue;
                widest = Math.Max(widest, Format(text, Palette.Text, cellFontSize).Width);
            }

            widths[column] = Math.Clamp(widest + 2 * CellPadX, MinColumnWidth, MaxColumnWidth);
        }

        ApplyColumnWidths(widths);
        widthsSampled = true;
    }

    private void ApplyColumnWidths(double[] widths)
    {
        columnWidths = widths;
        columnOffsets = new double[widths.Length];
        var offset = 0.0;
        for (var column = 0; column < widths.Length; column++)
        {
            columnOffsets[column] = offset;
            offset += widths[column];
        }
    }

    private static FormattedText Format(string value, IBrush brush, double fontSize) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GridTypeface, fontSize, brush);
}
