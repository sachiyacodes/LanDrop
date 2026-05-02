// Converters/ProgressWidthConverter.cs

using System;
using System.Globalization;
using System.Windows.Data;

namespace LanDrop.Converters
{
    /// <summary>
    /// Multi-value converter: [0] = percent (0-100), [1] = container ActualWidth.
    /// Returns the pixel width for the progress fill bar.
    /// </summary>
    public class ProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            if (values.Length < 2) return 0.0;
            if (values[0] is not double pct)   return 0.0;
            if (values[1] is not double total) return 0.0;
            return Math.Max(0, Math.Min(total, total * pct / 100.0));
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// CanSend: returns true when state is Idle/Completed/Failed/Cancelled
    /// (i.e. NOT actively transferring) — enables the Send button.
    /// </summary>
    public class CanSendConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is Models.TransferState s &&
            (s == Models.TransferState.Idle      ||
             s == Models.TransferState.Completed ||
             s == Models.TransferState.Failed    ||
             s == Models.TransferState.Cancelled);

        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// Returns Visibility.Visible only when TransferState == Paused.
    /// </summary>
    public class IsPausedToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is Models.TransferState s && s == Models.TransferState.Paused
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// Returns Visibility.Visible when transfer is active (Connecting/Transferring/Paused).
    /// Combines IsActiveTransferConverter + BoolToVisibilityConverter in one step.
    /// </summary>
    public class IsActiveToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is Models.TransferState s &&
            (s == Models.TransferState.Connecting   ||
             s == Models.TransferState.Transferring ||
             s == Models.TransferState.Paused)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

}
