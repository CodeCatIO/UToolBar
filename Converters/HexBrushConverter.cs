using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EdgeBar.Converters;

public class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text)) text = "#FFF3C4";
            var color = (Color)ColorConverter.ConvertFromString(text);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(255, 243, 196));
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
