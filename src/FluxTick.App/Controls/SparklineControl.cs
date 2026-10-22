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
    /// Initializes static members of the <see cref="SparklineControl"/> class.
    /// </summary>
    static SparklineControl()
    {
        AffectsRender<SparklineControl>(PointsProperty, StrokeProperty);
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

        var min = (double)points.Min();
        var max = (double)points.Max();
        var range = max - min;
        var coordinates = new Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var x = pad + width * i / (points.Count - 1);
            var normalized = range <= 0 ? 0.5 : ((double)points[i] - min) / range;
            coordinates[i] = new Point(x, pad + height * (1 - normalized));
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
