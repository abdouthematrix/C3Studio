using System.Windows;
using System.Windows.Controls;

namespace C3Studio;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Replace the visible content.</summary>
    public void Navigate(UIElement view) => ContentHost.Content = view;
}
