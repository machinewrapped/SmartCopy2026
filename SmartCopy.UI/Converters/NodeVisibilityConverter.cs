using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SmartCopy.UI.Converters;

public class NodeVisibilityConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 2 && values[0] is bool isFilterIncluded && values[1] is bool showFilteredNodes)
        {
            return isFilterIncluded || showFilteredNodes;
        }
        return true;
    }
}
