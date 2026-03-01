using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.UI.Converters;

public class FilterResultOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FilterResult.Excluded ? 0.4 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
