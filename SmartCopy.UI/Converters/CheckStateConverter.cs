using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Converters;

public class CheckStateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CheckState checkState)
        {
            return checkState switch
            {
                CheckState.Checked => true,
                CheckState.Unchecked => false,
                CheckState.Indeterminate => null,
                _ => false
            };
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? CheckState.Checked : CheckState.Unchecked;
        if (value is null)
            return CheckState.Indeterminate;
            
        return CheckState.Unchecked;
    }
}
