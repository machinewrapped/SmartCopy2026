using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Converters;

public class FilterResultExcludedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FilterResult.Excluded;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
