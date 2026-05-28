
using C3Studio.MonoGame;
using System.Collections.ObjectModel;

namespace C3Studio.ViewModels;

public class MeshSlotViewModel : ViewModelBase
{
    private bool _isVisible;
    public string Name { get; }
    private readonly Action<string, bool> _onToggled;

    public bool IsVisible
    {
        get => _isVisible;
        set { if (Set(ref _isVisible, value)) _onToggled(Name, value); }
    }

    public MeshSlotViewModel(string name, bool isVisible, Action<string, bool> onToggled)
    {
        Name = name;
        _isVisible = isVisible;
        _onToggled = onToggled;
    }
}