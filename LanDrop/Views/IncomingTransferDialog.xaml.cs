using System.Windows;
using LanDrop.Helpers;
using LanDrop.Networking;

namespace LanDrop.Views
{
    public partial class IncomingTransferDialog : Window
    {
        public bool Accepted { get; private set; } = false;

        public IncomingTransferDialog(IncomingTransferEventArgs args)
        {
            InitializeComponent();

            SenderText.Text        = $"{args.Hello.SenderName} wants to send you files";
            FileCountText.Text     = args.Hello.FileCount.ToString();
            TotalSizeText.Text     = FormatHelper.FormatBytes(args.Hello.TotalBytes);
            RemoteAddressText.Text = args.RemoteAddress;

            Loaded += (_, _) =>
            {
                WindowHelper.ApplyTitleBarColor(this, App.Settings?.DarkMode ?? false);
            };
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            DialogResult = true;
            Close();
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            DialogResult = false;
            Close();
        }
    }
}
