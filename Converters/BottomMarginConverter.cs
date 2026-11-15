using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdgeBar.Converters;

/// <summary>把行间距数值转换成只保留下边距的 Thickness</summary>
public class BottomMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double bottom = 0;
        if (value is double d) bottom = d;
        else if (value is int i) bottom = i;
        return new Thickness(0, 0, 0, bottom);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
