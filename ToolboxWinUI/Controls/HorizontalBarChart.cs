using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace ToolboxWinUI.Controls;

public class HorizontalBarChart : Grid
{
    private readonly Windows.UI.Color _color;
    private readonly TextBlock _titleLabel;
    private readonly TextBlock _valueLabel;
    private readonly Rectangle _trackBar;
    private readonly Rectangle _valueBar;
    private float _currentValue;

    public HorizontalBarChart(string title, Windows.UI.Color color)
    {
        _color = color;
        Margin = new Thickness(8, 4, 8, 4);
        Height = 44;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleLabel = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 48,
            Margin = new Thickness(4, 0, 8, 0)
        };
        Grid.SetColumn(_titleLabel, 0);

        var barGrid = new Grid
        {
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        _trackBar = new Rectangle
        {
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(30, color.R, color.G, color.B)),
            RadiusX = 4,
            RadiusY = 4,
            Height = 18
        };
        _valueBar = new Rectangle
        {
            Fill = new SolidColorBrush(color),
            RadiusX = 4,
            RadiusY = 4,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0
        };
        barGrid.Children.Add(_trackBar);
        barGrid.Children.Add(_valueBar);
        Grid.SetColumn(barGrid, 1);

        _valueLabel = new TextBlock
        {
            Text = "0%",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 52,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(8, 0, 4, 0)
        };
        Grid.SetColumn(_valueLabel, 2);

        Children.Add(_titleLabel);
        Children.Add(barGrid);
        Children.Add(_valueLabel);

        SizeChanged += (_, _) => UpdateBar();
    }

    public void AddValue(float value)
    {
        _currentValue = Math.Clamp(value, 0, 100);
        _valueLabel.Text = $"{_currentValue:F0}%";
        UpdateBar();
    }

    private void UpdateBar()
    {
        double trackWidth = ActualWidth - 120;
        if (trackWidth < 1) return;
        _valueBar.Width = trackWidth * _currentValue / 100.0;
    }
}
