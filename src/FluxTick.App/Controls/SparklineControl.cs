using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluxTick.Core.Application;

namespace FluxTick.App.Controls;

/// <summary>
/// A minimal line chart without axes: the recent prices as a smooth-ended polyline with a soft area fill and an end dot.
/// </summary>
public sealed class SparklineControl : Control
{
    /// <summary>
    /// Identifies the <see cref="Points"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<decimal>?> PointsProperty =
        AvaloniaProperty.Register<SparklineControl, IReadOnlyList<decimal>?>(nameof(Points));

    /// <summary>
    /// Identifies the <see cref="Stroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(Stroke), Brushes.Gray);

    /// <summary>
    /// Identifies the <see cref="Timeline"/> property.
    /// </summary>
    public static readonly StyledProperty<SparklineTimeline?> TimelineProperty =
        AvaloniaProperty.Register<SparklineControl, SparklineTimeline?>(nameof(Timeline));

    /// <summary>
    /// Identifies the <see cref="Price"/> property.
    /// </summary>
    public static readonly StyledProperty<decimal?> PriceProperty =
        AvaloniaProperty.Register<SparklineControl, decimal?>(nameof(Price));

    /// <summary>
    /// Identifies the <see cref="IsStopped"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsStoppedProperty =
        AvaloniaProperty.Register<SparklineControl, bool>(nameof(IsStopped));

    /// <summary>
    /// Identifies the <see cref="StoppedStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> StoppedStrokeProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(StoppedStroke), Brushes.Gray);

    /// <summary>
    /// Identifies the <see cref="AboveLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<decimal?> AboveLevelProperty =
        AvaloniaProperty.Register<SparklineControl, decimal?>(nameof(AboveLevel));

    /// <summary>
    /// Identifies the <see cref="BelowLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<decimal?> BelowLevelProperty =
        AvaloniaProperty.Register<SparklineControl, decimal?>(nameof(BelowLevel));

    /// <summary>
    /// Identifies the <see cref="AboveStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> AboveStrokeProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(AboveStroke), Brushes.Gray);

    /// <summary>
    /// Identifies the <see cref="BelowStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BelowStrokeProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(BelowStroke), Brushes.Gray);

    /// <summary>
    /// The dash pattern of the stretches without prices.
    /// </summary>
    private static readonly DashStyle GapDash = new([2, 2], 0);

    /// <summary>
    /// The dash pattern of the flat line drawn when there are too few prices.
    /// </summary>
    private static readonly DashStyle FlatDash = new([2, 3], 0);

    /// <summary>
    /// The dash pattern of the alert levels.
    /// </summary>
    private static readonly DashStyle LevelDash = new([3, 3], 0);

    /// <summary>
    /// The gradient under the line, kept while the color of <see cref="_fillColor"/> stays the same.
    /// </summary>
    private LinearGradientBrush? _fill;

    /// <summary>
    /// The color the gradient was made for.
    /// </summary>
    private Color _fillColor;

    /// <summary>
    /// The pen of the line, kept while the brush stays the same.
    /// </summary>
    private Pen? _linePen;

    /// <summary>
    /// The brush the pen was made for.
    /// </summary>
    private IBrush? _linePenBrush;

    /// <summary>
    /// Initializes static members of the <see cref="SparklineControl"/> class.
    /// </summary>
    static SparklineControl()
    {
        AffectsRender<SparklineControl>(PointsProperty, TimelineProperty, IsStoppedProperty, StoppedStrokeProperty, StrokeProperty, AboveLevelProperty, BelowLevelProperty, AboveStrokeProperty, BelowStrokeProperty);
    }

    /// <summary>
    /// Gets or sets when each of the <see cref="Points"/> was traded and what time span the chart shows. It lays the
    /// prices out along the time: stretches without prices are drawn as gray dashes. Without it, or when it has not as
    /// many times as there are prices, the prices are spread evenly over the width.
    /// </summary>
    public SparklineTimeline? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    /// <summary>
    /// Gets or sets the latest price, which places the alert lines when there are fewer than two prices to draw, or
    /// <see langword="null"/> when it is not known. A change draws the chart again only while there are fewer than two
    /// prices; with more, the prices change with it.
    /// </summary>
    public decimal? Price
    {
        get => GetValue(PriceProperty);
        set => SetValue(PriceProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the prices are not coming in, whatever the reason (stale, reconnecting,
    /// failed, switched off), which carries the last price on to the right edge as a dashed line in
    /// <see cref="StoppedStroke"/>.
    /// </summary>
    public bool IsStopped
    {
        get => GetValue(IsStoppedProperty);
        set => SetValue(IsStoppedProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the dashed lines drawn where there are no prices.
    /// </summary>
    public IBrush? StoppedStroke
    {
        get => GetValue(StoppedStrokeProperty);
        set => SetValue(StoppedStrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the price of the alert that fires when the price rises to it, drawn as a dashed line, or
    /// <see langword="null"/> for none.
    /// </summary>
    public decimal? AboveLevel
    {
        get => GetValue(AboveLevelProperty);
        set => SetValue(AboveLevelProperty, value);
    }

    /// <summary>
    /// Gets or sets the price of the alert that fires when the price falls to it, drawn as a dashed line, or
    /// <see langword="null"/> for none.
    /// </summary>
    public decimal? BelowLevel
    {
        get => GetValue(BelowLevelProperty);
        set => SetValue(BelowLevelProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the line of <see cref="AboveLevel"/>.
    /// </summary>
    public IBrush? AboveStroke
    {
        get => GetValue(AboveStrokeProperty);
        set => SetValue(AboveStrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the line of <see cref="BelowLevel"/>.
    /// </summary>
    public IBrush? BelowStroke
    {
        get => GetValue(BelowStrokeProperty);
        set => SetValue(BelowStrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the prices to draw, oldest first.
    /// </summary>
    public IReadOnlyList<decimal>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the line.
    /// </summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The price places the line only while there are too few prices to draw one; with more, a new price comes with
        // new prices, which draw the chart again.
        if (change.Property == PriceProperty && (Points?.Count ?? 0) < 2)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        var stroke = Stroke;
        if (stroke is null || bounds.Width < 8 || bounds.Height < 8)
        {
            return;
        }

        const double pad = 3;
        var width = bounds.Width - pad * 2;
        var height = bounds.Height - pad * 2;
        var points = Points ?? [];
        var timeline = Timeline;
        var timed = points.Count > 0 && timeline is not null && timeline.Times.Count == points.Count;
        var times = timeline?.Times;
        var gapBrush = StoppedStroke ?? stroke;
        Pen? gapPen = null;
        Pen GapPen() => gapPen ??= new Pen(gapBrush, 1.5, GapDash, PenLineCap.Flat, PenLineJoin.Round);

        // Most rows have no alert; they then allocate nothing for the levels.
        var levelPrices = LevelPrices(AboveLevel, BelowLevel);
        var hasAbove = AboveLevel is not null;

        if (!timed && points.Count < 2)
        {
            // Too little to draw a line: a flat dashed one at the price, or in the middle when even that is unknown, with
            // the alert lines around it.
            var known = points.Count == 1 ? points[0] : Price;
            if (known is not { } flat)
            {
                var middle = bounds.Height / 2;
                context.DrawLine(new Pen(gapBrush, 1, FlatDash) { LineCap = PenLineCap.Round }, new Point(pad, middle), new Point(bounds.Width - pad, middle));
                return;
            }

            var (flatBottom, flatTop) = SparklineScale.Range((double)flat, (double)flat, levelPrices);
            DrawLevels(context, levelPrices, hasAbove, AboveStroke, BelowStroke, flatBottom, flatTop, bounds.Width, pad, height);
            var (flatPosition, _) = SparklineScale.Place((double)flat, flatBottom, flatTop);
            var flatY = pad + height * (1 - flatPosition);
            context.DrawLine(new Pen(gapBrush, 1, FlatDash) { LineCap = PenLineCap.Round }, new Point(pad, flatY), new Point(bounds.Width - pad, flatY));
            return;
        }

        var lowest = points[0];
        var highest = points[0];
        foreach (var price in points)
        {
            lowest = Math.Min(lowest, price);
            highest = Math.Max(highest, price);
        }

        var (min, max) = SparklineScale.Range((double)lowest, (double)highest, levelPrices);
        var coordinates = new Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var x = timed
                ? pad + width * SparklineScale.TimeX(times![i], timeline!.AsOf, timeline.Window)
                : pad + width * i / (points.Count - 1);
            var (normalized, _) = SparklineScale.Place((double)points[i], min, max);
            coordinates[i] = new Point(x, pad + height * (1 - normalized));
        }

        // The alert levels go under the prices.
        DrawLevels(context, levelPrices, hasAbove, AboveStroke, BelowStroke, min, max, bounds.Width, pad, height);

        // After the last price of a source that stopped, however it shows (stale, reconnecting, failed), the last price is
        // carried on to the edge; the same when the last price is simply long ago.
        var trailing = timed && (IsStopped || timeline!.IsGap(times![^1], timeline.AsOf));
        if (timed)
        {
            // Before the first price nothing is known, so the first price is carried back to the edge.
            if (coordinates[0].X > pad + 0.5)
            {
                context.DrawLine(GapPen(), new Point(pad, coordinates[0].Y), coordinates[0]);
            }

            if (trailing && coordinates[^1].X < bounds.Width - pad - 0.5)
            {
                context.DrawLine(GapPen(), coordinates[^1], new Point(bounds.Width - pad, coordinates[^1].Y));
            }
        }

        // The prices are drawn in runs; a stretch between two prices that are too far apart is a dashed gap instead.
        var runStart = 0;
        for (var i = 1; i <= coordinates.Length; i++)
        {
            var endsRun = i == coordinates.Length || (timed && timeline!.IsGap(times![i - 1], times[i]));
            if (!endsRun)
            {
                continue;
            }

            DrawRun(context, coordinates, runStart, i - 1, stroke, bounds.Height);
            if (i < coordinates.Length)
            {
                context.DrawLine(GapPen(), coordinates[i - 1], coordinates[i]);
            }

            runStart = i;
        }

        context.DrawEllipse(trailing ? gapBrush : stroke, null, coordinates[^1], 2.2, 2.2);
    }

    /// <summary>
    /// Draws a run of prices that follow each other closely: a soft area under the line, and the line.
    /// </summary>
    /// <param name="context">
    /// The drawing context.
    /// </param>
    /// <param name="coordinates">
    /// The places of all the prices.
    /// </param>
    /// <param name="first">
    /// The index of the first price of the run.
    /// </param>
    /// <param name="last">
    /// The index of the last price of the run.
    /// </param>
    /// <param name="stroke">
    /// The brush of the line.
    /// </param>
    /// <param name="bottom">
    /// The y of the bottom edge, which the area reaches down to.
    /// </param>
    private void DrawRun(DrawingContext context, Point[] coordinates, int first, int last, IBrush stroke, double bottom)
    {
        if (first == last)
        {
            // A single price has no line; a small dot shows it.
            context.DrawEllipse(stroke, null, coordinates[first], 1.5, 1.5);
            return;
        }

        if (stroke is ISolidColorBrush solid)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(coordinates[first].X, bottom), true);
                for (var i = first; i <= last; i++)
                {
                    ctx.LineTo(coordinates[i]);
                }

                ctx.LineTo(new Point(coordinates[last].X, bottom));
                ctx.EndFigure(true);
            }

            // The gradient only depends on the color, which hardly ever changes between two renders.
            if (_fill is null || _fillColor != solid.Color)
            {
                _fillColor = solid.Color;
                _fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new GradientStop(Color.FromArgb(70, solid.Color.R, solid.Color.G, solid.Color.B), 0),
                        new GradientStop(Color.FromArgb(0, solid.Color.R, solid.Color.G, solid.Color.B), 1),
                    ],
                };
            }

            context.DrawGeometry(_fill, null, area);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(coordinates[first], false);
            for (var i = first + 1; i <= last; i++)
            {
                ctx.LineTo(coordinates[i]);
            }

            ctx.EndFigure(false);
        }

        if (_linePen is null || !ReferenceEquals(_linePenBrush, stroke))
        {
            _linePenBrush = stroke;
            _linePen = new Pen(stroke, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        }

        context.DrawGeometry(null, _linePen, line);
    }

    /// <summary>
    /// Collects the alert levels to draw.
    /// </summary>
    /// <param name="above">
    /// The level of the alert above, or <see langword="null"/>.
    /// </param>
    /// <param name="below">
    /// The level of the alert below, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The prices of the levels, the one above first; empty when there is none.
    /// </returns>
    private static double[] LevelPrices(decimal? above, decimal? below)
    {
        var count = (above is null ? 0 : 1) + (below is null ? 0 : 1);
        if (count == 0)
        {
            return [];
        }

        var prices = new double[count];
        var next = 0;
        if (above is { } a)
        {
            prices[next++] = (double)a;
        }

        if (below is { } b)
        {
            prices[next] = (double)b;
        }

        return prices;
    }

    /// <summary>
    /// Draws the dashed lines of the alert levels. A level too far away for the chart to grow to it sits at the edge,
    /// fainter.
    /// </summary>
    /// <param name="context">
    /// The drawing context.
    /// </param>
    /// <param name="prices">
    /// The prices of the levels, as <see cref="LevelPrices"/> returns them.
    /// </param>
    /// <param name="hasAbove">
    /// Whether the first price is the level above.
    /// </param>
    /// <param name="aboveStroke">
    /// The brush of the level above.
    /// </param>
    /// <param name="belowStroke">
    /// The brush of the level below.
    /// </param>
    /// <param name="bottom">
    /// The price at the bottom of the chart.
    /// </param>
    /// <param name="top">
    /// The price at the top of the chart.
    /// </param>
    /// <param name="totalWidth">
    /// The width of the control.
    /// </param>
    /// <param name="pad">
    /// The space kept free around the chart.
    /// </param>
    /// <param name="height">
    /// The height of the chart without the space around it.
    /// </param>
    private static void DrawLevels(DrawingContext context, double[] prices, bool hasAbove, IBrush? aboveStroke, IBrush? belowStroke, double bottom, double top, double totalWidth, double pad, double height)
    {
        for (var i = 0; i < prices.Length; i++)
        {
            var price = prices[i];
            var brush = hasAbove && i == 0 ? aboveStroke : belowStroke;
            if (brush is null)
            {
                continue;
            }

            var (position, clamped) = SparklineScale.Place(price, bottom, top);
            var y = pad + height * (1 - position);
            using (context.PushOpacity(clamped ? 0.35 : 0.9))
            {
                context.DrawLine(new Pen(brush, 1, LevelDash), new Point(pad, y), new Point(totalWidth - pad, y));
            }
        }
    }
}
