using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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
    /// Initializes static members of the <see cref="SparklineControl"/> class.
    /// </summary>
    static SparklineControl()
    {
        AffectsRender<SparklineControl>(PointsProperty, StrokeProperty, AboveLevelProperty, BelowLevelProperty, AboveStrokeProperty, BelowStrokeProperty);
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
        var points = Points;

        if (points is null || points.Count < 2)
        {
            var y = bounds.Height / 2;
            context.DrawLine(new Pen(stroke, 1, new DashStyle([2, 3], 0)) { LineCap = PenLineCap.Round }, new Point(pad, y), new Point(bounds.Width - pad, y));
            return;
        }

        var levels = new List<(double Price, IBrush? Brush)>();
        if (AboveLevel is { } above)
        {
            levels.Add(((double)above, AboveStroke));
        }

        if (BelowLevel is { } below)
        {
            levels.Add(((double)below, BelowStroke));
        }

        var (min, max) = SparklineScale.Range((double)points.Min(), (double)points.Max(), levels.Select(l => l.Price));
        var coordinates = new Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var x = pad + width * i / (points.Count - 1);
            var (normalized, _) = SparklineScale.Place((double)points[i], min, max);
            coordinates[i] = new Point(x, pad + height * (1 - normalized));
        }

        // The alert levels go under the prices. A level too far away for the chart to grow to it sits at the edge, fainter.
        foreach (var (price, brush) in levels)
        {
            if (brush is null)
            {
                continue;
            }

            var (position, clamped) = SparklineScale.Place(price, min, max);
            var y = pad + height * (1 - position);
            using (context.PushOpacity(clamped ? 0.35 : 0.9))
            {
                context.DrawLine(new Pen(brush, 1, new DashStyle([3, 3], 0)), new Point(pad, y), new Point(bounds.Width - pad, y));
            }
        }

        if (stroke is ISolidColorBrush solid)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(coordinates[0].X, bounds.Height), true);
                foreach (var point in coordinates)
                {
                    ctx.LineTo(point);
                }

                ctx.LineTo(new Point(coordinates[^1].X, bounds.Height));
                ctx.EndFigure(true);
            }

            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(70, solid.Color.R, solid.Color.G, solid.Color.B), 0),
                    new GradientStop(Color.FromArgb(0, solid.Color.R, solid.Color.G, solid.Color.B), 1),
                ],
            };
            context.DrawGeometry(fill, null, area);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(coordinates[0], false);
            for (var i = 1; i < coordinates.Length; i++)
            {
                ctx.LineTo(coordinates[i]);
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(stroke, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        context.DrawEllipse(stroke, null, coordinates[^1], 2.2, 2.2);
    }
}
