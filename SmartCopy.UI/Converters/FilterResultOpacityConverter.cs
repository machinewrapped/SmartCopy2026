using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Converters;

public class FilterResultOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FilterResult result && result == FilterResult.Excluded)
            return 0.4;
        return 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
