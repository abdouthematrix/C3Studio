using System.Windows;
using System.Windows.Controls;
using C3Studio.Core.Services;
using C3Studio.MonoGame;
using C3Studio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace C3Studio.Views.Pages;

/// <summary>
/// Phase 2 workspace page.
/// Creates the <see cref="C3StudioGame"/> WpfInterop control at runtime,
/// adds it to <c>ViewportHost</c>, and wires it to the ViewModel.
/// </summary>
public partial class WorkspacePage : UserControl
{
    private C3StudioGame? _game;

    public WorkspacePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_game != null) return;

        // Resolve IAssetFileService from DI
        var sp = (Application.Current as App)?.Services;

        _game = new C3StudioGame
        {
            AssetService = sp?.GetService<IAssetFileService>(),
        };

        // Add to viewport grid and hide placeholder
        ViewportPlaceholder.Visibility = Visibility.Collapsed;
        ViewportHost.Children.Add(_game);

        // Wire to ViewModel
        if (DataContext is WorkspaceViewModel vm)
            vm.SetGame(_game);
    }

    // TreeView SelectedItemChanged → ViewModel
    // (TwoWay binding on TreeView.SelectedItem doesn't work with HierarchicalDataTemplate)
    private void AssetTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is WorkspaceViewModel vm &&
            e.NewValue is Core.Models.AssetNode node)
            vm.SelectedNode = node;
    }
}
