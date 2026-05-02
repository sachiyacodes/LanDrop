// Views/IncomingTransferDialog.cs
// Modal accept/reject dialog for incoming transfers — built in code, no XAML.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LanDrop.Helpers;
using LanDrop.Networking;

namespace LanDrop.Views
{
    public partial class IncomingTransferDialog : Window
    {
        public bool Accepted { get; private set; } = false;

        public IncomingTransferDialog(IncomingTransferEventArgs args)
        {
            Title                 = "Incoming Transfer";
            Width                 = 440;
            Height                = 230;
            ResizeMode            = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background            = new SolidColorBrush(Colors.White);

            // Root stack — use Margin on children instead of Spacing
            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            root.Children.Add(new TextBlock
            {
                Text         = $"📥  Incoming transfer from {args.Hello.SenderName}",
                FontSize     = 15,
                FontWeight   = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 10)
            });

            root.Children.Add(new TextBlock
            {
                Text         = $"Files: {args.Hello.FileCount}   ·   Size: {FormatHelper.FormatBytes(args.Hello.TotalBytes)}\nFrom: {args.RemoteAddress}",
                FontSize     = 13,
                Foreground   = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 20)
            });

            // Button row — right-aligned using DockPanel
            var btnRow = new DockPanel { LastChildFill = false };

            var rejectBtn = new Button
            {
                Content  = "✕  Reject",
                Padding  = new Thickness(20, 8, 20, 8),
                IsCancel = true,
                Margin   = new Thickness(0, 0, 8, 0)
            };
            rejectBtn.Click += (_, __) => { Accepted = false; DialogResult = false; };

            var acceptBtn = new Button
            {
                Content   = "✔  Accept",
                Padding   = new Thickness(20, 8, 20, 8),
                IsDefault = true
            };
            acceptBtn.Click += (_, __) => { Accepted = true; DialogResult = true; };

            DockPanel.SetDock(rejectBtn, Dock.Right);
            DockPanel.SetDock(acceptBtn, Dock.Right);

            // Add in reverse so Accept appears on the right
            btnRow.Children.Add(acceptBtn);
            btnRow.Children.Add(rejectBtn);

            root.Children.Add(btnRow);
            Content = root;
        }
    }
}
