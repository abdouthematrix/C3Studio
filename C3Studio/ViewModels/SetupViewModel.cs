using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using C3Studio.Core.Models;
using C3Studio.Core.Services;


namespace C3Studio.ViewModels;

public class SetupViewModel : ViewModelBase
{
    private readonly ISettingsService  _settings;
    private readonly IGameDataService  _gameData;
    private readonly IAssetFileService _assets;

    private string _conquerPath  = string.Empty;
    private string _statusText   = "Select your Conquer Online game folder.";
    private bool   _isValid;
    private bool   _isLoading;

    public event Action? NavigateToWorkspace;

    // ── Required items ────────────────────────────────────────────────────
    private static readonly string[] RequiredFolders = ["ani", "c3", "data", "ini"];
    private static readonly string[] RequiredWdf     = ["c3.wdf", "data.wdf"];
    private static readonly string[] RequiredIni     =
    [
        "npc.ini", "3DSimpleObj.ini", "3DEffect.ini",
        "3dobj.ini", "3dtexture.ini", "3dmotion.ini", "3DEffectObj.ini",
        "3DsimpleRole.ini", "MountMotion.ini", "WeaponMotion.ini",
        "armor.ini", "armet.ini", "weapon.ini", "Mount.ini",
        "AdditiveSize.ini","TransForm.ini",
    ];

    public string ConquerPath
    {
        get => _conquerPath;
        set { if (Set(ref _conquerPath, value)) Validate(); }
    }

    public string StatusText  { get => _statusText;  private set => Set(ref _statusText,  value); }
    public bool   IsValid     { get => _isValid;      private set => Set(ref _isValid,     value); }
    public bool   IsLoading   { get => _isLoading;    private set => Set(ref _isLoading,   value); }

    public ObservableCollection<ValidationItem> ValidationItems { get; } = new();

    public ICommand BrowseCommand { get; }
    public ICommand LoadCommand   { get; }

    public SetupViewModel(ISettingsService settings, IGameDataService gameData, IAssetFileService assets)
    {
        _settings = settings;
        _gameData  = gameData;
        _assets    = assets;

        BrowseCommand = Cmd(Browse);
        LoadCommand   = Cmd(async () => await LoadAsync(), () => IsValid && !IsLoading);

        ConquerPath = settings.ConquerPath;
    }

    // ── Browse ────────────────────────────────────────────────────────────
    private void Browse()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description            = "Select Conquer Online game folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = false,
        };
        if (!string.IsNullOrEmpty(ConquerPath))
            dlg.SelectedPath = ConquerPath;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ConquerPath = dlg.SelectedPath;
    }

    // ── Validate ──────────────────────────────────────────────────────────
    private void Validate()
    {
        ValidationItems.Clear();

        if (string.IsNullOrWhiteSpace(ConquerPath) || !Directory.Exists(ConquerPath))
        {
            IsValid    = false;
            StatusText = "Folder does not exist.";
            return;
        }

        foreach (var f in RequiredFolders)
            AddItem("Folder", f, Directory.Exists(Path.Combine(ConquerPath, f)));

        foreach (var w in RequiredWdf)
            AddItem("Archive", w, File.Exists(Path.Combine(ConquerPath, w)));

        foreach (var i in RequiredIni)
            AddItem("INI", i, File.Exists(Path.Combine(ConquerPath, "ini", i)));

        bool ok = ValidationItems.All(v => v.Status == ValidationStatus.Ok);
        IsValid    = ok;
        StatusText = ok ? "✓  All checks passed — ready to load."
                        : $"{ValidationItems.Count(v => v.Status == ValidationStatus.Fail)} item(s) missing.";
    }

    private void AddItem(string category, string name, bool exists)
        => ValidationItems.Add(new ValidationItem
        {
            Category = category,
            Name     = name,
            Status   = exists ? ValidationStatus.Ok : ValidationStatus.Fail,
        });

    // ── Load ──────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        IsLoading  = true;
        StatusText = "Loading game data…";
        try
        {
            _settings.ConquerPath = ConquerPath;
            _assets.Initialize(ConquerPath);
            await _gameData.LoadAsync(ConquerPath);
            NavigateToWorkspace?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
            IsValid    = false;
        }
        finally { IsLoading = false; }
    }
}
