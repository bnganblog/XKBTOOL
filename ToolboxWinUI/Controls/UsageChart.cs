using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ToolboxWinUI.Controls;

public class UsageChart : Grid
{
    private readonly Windows.UI.Color _color;
    private readonly TextBlock _label;
    private readonly TextBlock _valueLabel;
    private readonly Microsoft.UI.Xaml.Shapes.Path _trackPath;
    private readonly Microsoft.UI.Xaml.Shapes.Path _valuePath;
    private float _currentValue;

    public UsageChart(string title, Windows.UI.Color color)
    {
        _color = color;
        Margin = new Thickness(12);
        Width = 140;
        Height = 140;

        _trackPath = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(30, color.R, color.G, color.B)),
            StrokeThickness = 10,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        _valuePath = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 10,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        _label = new TextBlock
        {
            Text = title,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 35, 0, 0)
        };

        _valueLabel = new TextBlock
        {
            Text = "0%",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        Children.Add(_trackPath);
        Children.Add(_valuePath);
        Children.Add(_label);
        Children.Add(_valueLabel);

        SizeChanged += (s, e) => Redraw();
    }

    public void AddValue(float value)
    {
        _currentValue = Math.Clamp(value, 0, 100);
        _valueLabel.Text = $"{_currentValue:F0}%";
        Redraw();
    }

    private void Redraw()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1) return;

        double cx = w / 2, cy = h / 2 + 8;
        double r = Math.Min(w, h) / 2 - 15;

        _trackPath.Data = CreateArcGeometry(cx, cy, r, 0, 359.999);
        double sweep = _currentValue / 100.0 * 359.999;
        _valuePath.Data = CreateArcGeometry(cx, cy, r, -90, sweep);
    }

    private Geometry CreateArcGeometry(double cx, double cy, double r, double startAngle, double sweepAngle)
    {
        var fig = new PathFigure();
        double startRad = startAngle * Math.PI / 180;
        double endRad = (startAngle + sweepAngle) * Math.PI / 180;

        var startPt = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        var endPt = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

        fig.StartPoint = startPt;
        fig.Segments.Add(new ArcSegment
        {
            Point = endPt,
            Size = new Size(r, r),
            IsLargeArc = sweepAngle > 180,
            SweepDirection = SweepDirection.Clockwise
        });

        var geom = new PathGeometry();
        geom.Figures.Add(fig);
        return geom;
    }
}
