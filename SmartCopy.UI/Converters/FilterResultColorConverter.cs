using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Converters;

public class FilterResultColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FilterResult result)
        {
            if (result == FilterResult.Excluded)
            {
                return new SolidColorBrush(Colors.SlateBlue);
            }
        }
        return Avalonia.AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
