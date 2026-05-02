// Converters/InverseBoolConverter.cs
// True → False, False → True. Used for two-way binding of the Send/Receive toggle.

using System;
using System.Globalization;
using System.Windows.Data;

namespace LanDrop.Converters
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }
}
