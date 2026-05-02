// Views/WiFiDirectPanel.xaml.cs

using System.Windows;
using System.Windows.Controls;

namespace LanDrop.Views
{
    public partial class WiFiDirectPanel : UserControl
    {
        public WiFiDirectPanel()
        {
            InitializeComponent();

            // Set DataContext after App services are initialized
            Loaded += (_, __) =>
            {
                DataContext = App.WifiDirectVM;
            };
        }
    }
}
