using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using C3Studio.Core.Models;
using C3Studio.Core.Services;

namespace C3Studio.ViewModels;

public class SetupViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IGameDataService _gameData;
    private readonly IAssetFileService _assets;

    private string _conquerPath = string.Empty;
    private string _statusText = "Select your Conquer Online game folder.";
    private bool _isValid;
    private bool _isLoading;

    public event Action? NavigateToWorkspace;

    // ── Required items ────────────────────────────────────────────────────
    private static readonly string[] RequiredFolders = ["ani", "c3", "data", "ini"];
    private static readonly string[] RequiredPackages = ["c3", "data"];
    private static readonly string[] PackagesExtensions = { ".wdf", ".tpd", ".tpi", ".dnp" };

    // INI files that must strictly exist on disk inside the 'ini' directory
    private static readonly string[] RequiredPlainIni =
    [
        "npc.ini", "3DsimpleRole.ini", "AdditiveSize.ini", "TransForm.ini"
    ];

    // Files that are preferentially loaded as a .dbc table inside c3.wdb, with a local .ini fallback
    private static readonly string[] RequiredDbcOrIni =
    [
        "3DSimpleObj.ini", "3DEffect.ini", "3dobj.ini", "3dtexture.ini",
        "3dmotion.ini", "3DEffectObj.ini", "MountMotion.ini", "WeaponMotion.ini",
        "armor.ini", "armet.ini", "weapon.ini", "Mount.ini"
    ];

    public string ConquerPath
    {
        get => _conquerPath;
        set { if (Set(ref _conquerPath, value)) Validate(); }
    }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public bool IsValid { get => _isValid; private set => Set(ref _isValid, value); }
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public ObservableCollection<ValidationItem> ValidationItems { get; } = new();

    public ICommand BrowseCommand { get; }
    public ICommand LoadCommand { get; }

    public SetupViewModel(ISettingsService settings, IGameDataService gameData, IAssetFileService assets)
    {
        _settings = settings;
        _gameData = gameData;
        _assets = assets;

        BrowseCommand = Cmd(Browse);
        LoadCommand = Cmd(async () => await LoadAsync(), () => IsValid && !IsLoading);

        ConquerPath = settings.ConquerPath;
    }

    // ── Browse ────────────────────────────────────────────────────────────
    private void Browse()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Conquer Online game folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
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
            IsValid = false;
            StatusText = "Folder does not exist.";
            return;
        }

        foreach (var f in RequiredFolders)
            AddItem("Folder", f, Directory.Exists(Path.Combine(ConquerPath, f)));

        foreach (var package in RequiredPackages)
        {
            bool exists = PackagesExtensions.Any(ext =>
                File.Exists(Path.Combine(ConquerPath, package + ext)));

            string foundExtension = PackagesExtensions.FirstOrDefault(ext =>
                File.Exists(Path.Combine(ConquerPath, package + ext))) ?? string.Empty;

            AddItem(
                "Archive",
                exists ? package + foundExtension : package,
                exists
            );
        }

        // 1. Validate files that must strictly be plain INIs on disk
        foreach (var i in RequiredPlainIni)
        {
            AddItem("INI", i, File.Exists(Path.Combine(ConquerPath, "ini", i)));
        }

        // 2. Validate flexible files (Prioritizing the .dbc archive version)
        bool wdbExists = File.Exists(Path.Combine(ConquerPath, "ini", "c3.wdb"));
        foreach (var i in RequiredDbcOrIni)
        {
            bool hasPlainIni = File.Exists(Path.Combine(ConquerPath, "ini", i));
            bool exists = wdbExists || hasPlainIni;

            // Since the loader grabs the DBC from c3.wdb first, reflect that priority in the display name
            string displayName = wdbExists
                ? $"{Path.GetFileNameWithoutExtension(i)}.dbc (Archive)"
                : i;

            AddItem("DBC/INI", displayName, exists);
        }

        bool ok = ValidationItems.All(v => v.Status == ValidationStatus.Ok);
        IsValid = ok;
        StatusText = ok ? "✓  All checks passed — ready to load."
                        : $"{ValidationItems.Count(v => v.Status == ValidationStatus.Fail)} item(s) missing.";
    }

    private void AddItem(string category, string name, bool exists)
        => ValidationItems.Add(new ValidationItem
        {
            Category = category,
            Name = name,
            Status = exists ? ValidationStatus.Ok : ValidationStatus.Fail,
        });

    // ── Load ──────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        IsLoading = true;
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
            IsValid = false;
        }
        finally { IsLoading = false; }
    }
}