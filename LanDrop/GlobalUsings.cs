// GlobalUsings.cs
// Resolves all WPF vs WinForms type ambiguities project-wide.
// WinForms is only included for FolderBrowserDialog — everything else is WPF.

global using Application        = System.Windows.Application;
global using MessageBox         = System.Windows.MessageBox;
global using MessageBoxResult   = System.Windows.MessageBoxResult;
global using MessageBoxButton   = System.Windows.MessageBoxButton;
global using MessageBoxImage    = System.Windows.MessageBoxImage;
global using SystemColors       = System.Windows.SystemColors;
global using DataFormats        = System.Windows.DataFormats;
global using DragEventArgs      = System.Windows.DragEventArgs;
global using DragDropEffects    = System.Windows.DragDropEffects;

// WPF Controls
global using TextBox            = System.Windows.Controls.TextBox;
global using CheckBox           = System.Windows.Controls.CheckBox;
global using Button             = System.Windows.Controls.Button;
global using Label              = System.Windows.Controls.Label;
global using ListBox            = System.Windows.Controls.ListBox;
global using ComboBox           = System.Windows.Controls.ComboBox;
global using ProgressBar        = System.Windows.Controls.ProgressBar;
global using Orientation        = System.Windows.Controls.Orientation;
global using Brushes            = System.Windows.Media.Brushes;
global using Color              = System.Windows.Media.Color;
global using Colors             = System.Windows.Media.Colors;
global using SolidColorBrush   = System.Windows.Media.SolidColorBrush;
global using UserControl      = System.Windows.Controls.UserControl;
global using ColorConverter    = System.Windows.Media.ColorConverter;
