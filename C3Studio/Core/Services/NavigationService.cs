using System.Windows;
using C3Studio.ViewModels;
using C3Studio.Views;
using C3Studio.Views.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace C3Studio.Core.Services;

public interface INavigationService
{
    void GoToRoleViewer();
    void GoToSetup();
    void GoToWorkspace();
}

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _sp;

    public NavigationService(IServiceProvider sp) => _sp = sp;

    private static MainWindow Win =>
        (MainWindow)Application.Current.MainWindow;


    public void GoToRoleViewer()
    {
        var vm = _sp.GetRequiredService<RoleViewerViewModel>();
        var view = new RoleViewerPage { DataContext = vm };
        Win.Navigate(view);        
    }
    public void GoToSetup()
    {
        var vm   = _sp.GetRequiredService<SetupViewModel>();
        vm.NavigateToWorkspace += GoToWorkspace;
        var view = new SetupView { DataContext = vm };
        Win.Navigate(view);
    }

    public void GoToWorkspace()
    {
        var vm   = _sp.GetRequiredService<WorkspaceViewModel>();
        var view = new WorkspacePage { DataContext = vm };
        Win.Navigate(view);
        _ = vm.LoadAsync();   // fire-and-forget; status bar shows progress
    }
}
