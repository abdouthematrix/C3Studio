using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using C3Studio.Core.Services;
using C3Studio.MonoGame;
using C3Studio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace C3Studio.Views.Pages;

public partial class RoleViewerPage : UserControl
{
    private C3StudioGame? _game;

    public RoleViewerPage()
    {
        InitializeComponent();

        // We still hook into Loaded, but we won't block the thread here.
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;

        if (_game != null) return;

        var sp = (Application.Current as App)?.Services;
        _game = new C3StudioGame
        {
            AssetService = sp?.GetService<IAssetFileService>(),
        };

        // Hide the loading placeholder and inject the engine
        if (ViewportPlaceholder != null)
            ViewportPlaceholder.Visibility = Visibility.Collapsed;

        ViewportHost.Children.Add(_game);

        if (DataContext is RoleViewerViewModel vm)
            vm.SetGame(_game);

        // DispatcherPriority.ContextIdle ensures WPF finishes calculating layout 
        // and paints the initial screen (showing the "Initialising..." placeholder) 
        // before we lock up the thread to boot DirectX/MonoGame.
        Dispatcher.InvokeAsync(InitializeEngine, DispatcherPriority.ContextIdle);
    }

    private void InitializeEngine()
    {
        if (DataContext is RoleViewerViewModel vm)
            vm.LoadBaseBody();
    }
}