// Converters/Converters.cs
// IValueConverter implementations used in XAML bindings

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LanDrop.Helpers;
using LanDrop.Models;

namespace LanDrop.Converters
{
    /// <summary>Converts a long (bytes) to a human-readable string like "1.2 GB".</summary>
    public class BytesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is long l ? FormatHelper.FormatBytes(l) : "0 B";
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>Converts a double (bytes/s) to a speed string like "45.2 MB/s".</summary>
    public class SpeedConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is double d ? FormatHelper.FormatSpeed(d) : "— MB/s";
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>Converts a TimeSpan to an ETA string like "1m 23s".</summary>
    public class TimeSpanToEtaConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is TimeSpan ts ? FormatHelper.FormatEta(ts) : "—";
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>Converts a double percent to "42%".</summary>
    public class PercentConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is double d ? $"{d:F0}%" : "0%";
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>Converts a bool to Visibility (true → Visible, false → Collapsed).</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            v is Visibility vis && vis == Visibility.Visible;
    }

    /// <summary>True → False, False → True (two-way). Used for Send/Receive toggle.</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is bool b && !b;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            value is bool b && !b;
    }

    /// <summary>Inverted BoolToVisibility (false → Visible).</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            v is Visibility vis && vis != Visibility.Visible;
    }

    /// <summary>Converts TransferState to a colour-coded status string.</summary>
    public class TransferStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is TransferState state)
                return state switch
                {
                    TransferState.Transferring => "#22C55E",  // green
                    TransferState.Paused       => "#F59E0B",  // amber
                    TransferState.Completed    => "#3B82F6",  // blue
                    TransferState.Failed       => "#EF4444",  // red
                    TransferState.Cancelled    => "#6B7280",  // gray
                    _                          => "#6B7280"
                };
            return "#6B7280";
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>Converts TransferState to a display label.</summary>
    public class TransferStateToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is TransferState s
                ? s switch
                {
                    TransferState.Idle         => "Idle",
                    TransferState.Connecting   => "Connecting…",
                    TransferState.Transferring => "Transferring",
                    TransferState.Paused       => "Paused",
                    TransferState.Completed    => "Completed",
                    TransferState.Failed       => "Failed",
                    TransferState.Cancelled    => "Cancelled",
                    _                          => s.ToString()
                }
                : string.Empty;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>Returns true when a transfer is actively in progress (Connecting/Transferring/Paused).</summary>
    public class IsActiveTransferConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is Models.TransferState s &&
            (s == Models.TransferState.Connecting   ||
             s == Models.TransferState.Transferring ||
             s == Models.TransferState.Paused);
        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }
    /// <summary>
    /// Compares an int value to a parameter. Returns true if equal.
    /// Used for tab RadioButton IsChecked binding.
    /// </summary>
    public class IntEqualConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is int i && p is string s && int.TryParse(s, out int target) && i == target;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            if (value is bool b && b && p is string s && int.TryParse(s, out int target))
                return target;
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
