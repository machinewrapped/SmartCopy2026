using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SmartCopy.UI.Converters;

public class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            if (bytes == 0) return string.Empty;
            
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; 
            if (bytes == 0)
                return "0 " + suf[0];
            long bytesTemp = Math.Abs(bytes);
            int place = System.Convert.ToInt32(Math.Floor(Math.Log(bytesTemp, 1024)));
            double num = Math.Round(bytesTemp / Math.Pow(1024, place), 1);
            return (Math.Sign(bytes) * num).ToString() + " " + suf[place];
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
