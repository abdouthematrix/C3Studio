using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.MonoGame;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;

namespace C3Studio.ViewModels;

public class WorkspaceViewModel : ViewModelBase
{
    private readonly IGameDataService _gameData;
    private readonly IAssetFileService _assets;
    private readonly ISettingsService _settings;
    private readonly INavigationService _nav;

    private C3StudioGame? _game;

    // ── Multi-file backing list ───────────────────────────────────────────
    private List<string> _modelPaths = new();

    private string _modelPath = string.Empty;
    private string _texturePath = string.Empty;
    private string _motionPath = string.Empty;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private string _frameLabel = "0 / 0";
    private string _playPauseLabel = "⏸";
    private float _fps = 30f;
    private AssetNode? _selectedNode;

    public ObservableCollection<AssetNode> AssetTree { get; } = new();
    public ObservableCollection<MotionData> AvailableMotions { get; } = new();

    // ── Bindable properties ───────────────────────────────────────────────
    private int _asb = 5;

    public int Asb
    {
        get => _asb;
        set => Set(ref _asb, value);
    }

    private int _adb = 6;

    public int Adb
    {
        get => _adb;
        set => Set(ref _adb, value);
    }


    public string ModelPath
    {
        get => _modelPath;
        set
        {
            if (!Set(ref _modelPath, value)) return;

            if (string.IsNullOrWhiteSpace(value))
            {
                _modelPaths = new List<string>();
            }
            else if (!value.EndsWith("files selected", StringComparison.Ordinal))
            {
                var path = value.Trim();
                _modelPaths = new List<string> { path };
                TexturePath = FindTexture(path);
            }
        }
    }

    public string TexturePath
    {
        get => _texturePath;
        set => Set(ref _texturePath, value);
    }

    public string MotionPath
    {
        get => _motionPath;
        set => Set(ref _motionPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public string FrameLabel
    {
        get => _frameLabel;
        private set => Set(ref _frameLabel, value);
    }

    public string PlayPauseLabel
    {
        get => _playPauseLabel;
        private set => Set(ref _playPauseLabel, value);
    }

    public float Fps
    {
        get => _fps;
        set { if (Set(ref _fps, value)) _game?.SetFps(value); }
    }

    public AssetNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            Set(ref _selectedNode, value);

            AvailableMotions.Clear();
            if (value?.AssetData?.Motions is { Length: > 0 } motions)
                foreach (var m in motions)
                    AvailableMotions.Add(m);

            if (value?.IsLoadable == true)
                LoadAssetNode(value);
        }
    }

    private MotionData? _selectedMotion;
    public MotionData? SelectedMotion
    {
        get => _selectedMotion;
        set
        {
            if (Set(ref _selectedMotion, value) && value != null)
                ApplyMotionSilent(value.Path);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────

    public ICommand BrowseFileCommand { get; }
    public ICommand LoadModelCommand { get; }
    public ICommand ResetCameraCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StepFwdCommand { get; }
    public ICommand StepBackCommand { get; }
    public ICommand SetFpsCommand { get; }
    public ICommand BrowseTextureCommand { get; }
    public ICommand BrowseMotionCommand { get; }
    public ICommand ApplyMotionCommand { get; }

    // ── Search / filter ───────────────────────────────────────────────────

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) RefreshFilter(); }
    }

    public ObservableCollection<AssetNode> FilteredAssetTree { get; } = new();
    public ICommand ClearSearchCommand { get; }

    // ── Export ───────────────────────────────────────────────────

    private readonly IAssetExportService _exportService;
    private CancellationTokenSource? _exportCts;
    private string _exportStatus = string.Empty;
    public string ExportStatus
    {
        get => _exportStatus;
        private set { _exportStatus = value; OnPropertyChanged(); }
    }

    private bool _isExporting;
    public bool IsExporting
    {
        get => _isExporting;
        private set { _isExporting = value; OnPropertyChanged(); }
    }

    private bool _exportMotions = true;
    public bool ExportMotions
    {
        get => _exportMotions;
        set { _exportMotions = value; OnPropertyChanged(); }
    }

    private ExportConflict _exportConflictMode = ExportConflict.Skip;
    public ExportConflict ExportConflictMode
    {
        get => _exportConflictMode;
        set { _exportConflictMode = value; OnPropertyChanged(); }
    }

    private ExportLayout _exportLayout = ExportLayout.NamedFolderFlat;
    public ExportLayout ExportLayout
    {
        get => _exportLayout;
        set { _exportLayout = value; OnPropertyChanged(); }
    }

    public ICommand ExportNodeCommand { get; }
    private bool CanExport() => SelectedNode?.IsLoadable == true && !IsExporting;

    public WorkspaceViewModel(IGameDataService gameData,
                               IAssetFileService assets,
                               IAssetExportService exportService,
                               ISettingsService settings,
                               INavigationService nav)
    {
        _gameData = gameData;
        _assets = assets;
        _settings = settings;
        _exportService = exportService;
        _nav = nav;
        BrowseFileCommand = Cmd(BrowseFile);
        LoadModelCommand = Cmd(LoadModel, () => _modelPaths.Count > 0);
        ResetCameraCommand = Cmd(() => _game?.ResetCamera());
        PlayPauseCommand = Cmd(TogglePlay);
        StepFwdCommand = Cmd(() => _game?.StepFrame(1));
        StepBackCommand = Cmd(() => _game?.StepFrame(-1));
        SetFpsCommand = Cmd<string>(s => { if (float.TryParse(s, out float v)) Fps = v; });
        BrowseTextureCommand = Cmd(BrowseTexture);
        BrowseMotionCommand = Cmd(BrowseMotion);
        ApplyMotionCommand = Cmd(ApplyMotion, () => !string.IsNullOrEmpty(MotionPath));
        ClearSearchCommand = Cmd(() => SearchText = string.Empty);
        ExportNodeCommand = Cmd(ExportNode, () => CanExport());
        GoToSetupCommand = Cmd(() => _nav.GoToSetup());
    }

    public ICommand GoToSetupCommand { get; }

    private async void ExportNode()
    {
        if (SelectedNode?.AssetData == null) return;

        using var dlg = new FolderBrowserDialog
        {
            Description = $"Export '{SelectedNode.Label}' to…",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        _exportCts = new CancellationTokenSource();
        IsExporting = true;
        ExportStatus = "Exporting…";

        try
        {
            var progress = new Progress<string>(msg =>
                System.Windows.Application.Current.Dispatcher.Invoke(() => ExportStatus = msg));

            var result = await _exportService.ExportNodeAsync(
                data: SelectedNode.AssetData,
                assetLabel: SelectedNode.Label,
                destFolder: dlg.SelectedPath,
                layout: ExportLayout,
                includeMotions: ExportMotions,
                conflictMode: ExportConflictMode,
                progress: progress,
                ct: _exportCts.Token);

            ExportStatus = result.ToString();

            if (result.HasErrors)
                ShowExportErrors(result);
        }
        catch (OperationCanceledException)
        {
            ExportStatus = "Export cancelled.";
        }
        finally
        {
            IsExporting = false;
            _exportCts.Dispose();
            _exportCts = null;
        }
    }

    public void CancelExport() => _exportCts?.Cancel();

    private static void ShowExportErrors(ExportResult result)
    {
        var msg = string.Join("\n", result.Failed.Select(f => $"• {f.Path}: {f.Reason}"));
        MessageBox.Show(msg, "Export — some files failed",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ── Initialisation ────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading game data…";
        try
        {
            _assets.Initialize(_settings.ConquerPath);
            await _gameData.LoadAsync(_settings.ConquerPath);
            BuildAssetTree();
            StatusMessage = $"Ready — {_gameData.Npcs.Count} NPCs, {_gameData.SimpleObjs.Count} objects, " +
                            $"{_gameData.Effects.Count} effects, {_gameData.Armors.Count} armors, " +
                            $"{_gameData.Armets.Count} armets, {_gameData.Weapons.Count} weapons, " +
                            $"{_gameData.Transforms.Count} transforms, {_gameData.Mounts.Count} mounts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    public void SetGame(C3StudioGame game)
    {
        _game = game;
        _game.FrameChanged += OnFrameChanged;
        _game.SetFps(_fps);
    }

    // ── Asset tree construction ───────────────────────────────────────────

    private void BuildAssetTree()
    {
        AssetTree.Clear();
        AssetTree.Add(BuildNpcRoot());
        AssetTree.Add(BuildSimpleObjRoot());
        AssetTree.Add(BuildEffectRoot());
        AssetTree.Add(BuildArmorRoot());
        AssetTree.Add(BuildArmetRoot());
        AssetTree.Add(BuildWeaponRoot());
        AssetTree.Add(BuildTransformRoot());
        AssetTree.Add(BuildMountRoot());
        RefreshFilter();
    }

    // ── NPC tree ──────────────────────────────────────────────────────────

    private AssetNode BuildNpcRoot()
    {
        var root = new AssetNode { Icon = "👤", Label = $"NPCs ({_gameData.Npcs.Count})" };
        foreach (var npc in _gameData.Npcs)
            root.Children.Add(BuildNpcNode(npc));
        return root;
    }

    private AssetNode BuildNpcNode(NpcTypeInfo npc)
    {
        var node = new AssetNode { Icon = "⚔", Label = $"[{npc.NpcType}] {npc.Name}" };

        var obj = _gameData.FindSimpleObj(npc.SimpleObjId);
        var motions = BuildMotionEntries(npc);

        // SimpleObj parts: use the NPC's scalar Asb/Adb for all slots.
        string[] objMeshes = [], objTextures = [];
        int[] objAsb = [], objAdb = [];
        if (obj != null)
        {
            (objMeshes, objTextures, objAsb, objAdb) = BuildMeshArrays(obj, npc.Asb, npc.Adb);
        }

        // Effect parts: each slot carries its own per-slot Asb/Adb.
        string[] efxMeshes = [], efxTextures = [];
        int[] efxAsb = [], efxAdb = [];
        if (!string.IsNullOrEmpty(npc.Effect))
        {
            (efxMeshes, efxTextures, efxAsb, efxAdb) = BuildEffectParts(npc.Effect);
        }

        var allMeshes = objMeshes.Concat(efxMeshes).ToArray();
        var allTextures = objTextures.Concat(efxTextures).ToArray();
        var allAsb = objAsb.Concat(efxAsb).ToArray();
        var allAdb = objAdb.Concat(efxAdb).ToArray();

        if (allMeshes.Length > 0 || motions.Length > 0)
        {
            node.AssetData = new AssetData
            {
                MeshPaths = allMeshes,
                TexturePaths = allTextures,
                Motions = motions,
                Asb = allAsb,
                Adb = allAdb,
            };
        }

        return node;
    }

    // ── SimpleObj tree ────────────────────────────────────────────────────

    private AssetNode BuildSimpleObjRoot()
    {
        var root = new AssetNode { Icon = "🗿", Label = $"Simple Objects ({_gameData.SimpleObjs.Count})" };
        foreach (var obj in _gameData.SimpleObjs)
            root.Children.Add(BuildSimpleObjNode(obj));
        return root;
    }

    private AssetNode BuildSimpleObjNode(C3DSimpleObjInfo obj)
    {
        // SimpleObj has no Asb/Adb — use defaults (5/6).
        var (meshPaths, texturePaths, asb, adb) = BuildMeshArrays(obj);

        var node = new AssetNode
        {
            Icon = "📦",
            Label = $"[{obj.IdType}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                Asb = asb,
                Adb = adb,
            }
        };

        for (int i = 0; i < obj.Parts; i++)
        {
            if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                continue;

            node.Children.Add(new AssetNode
            {
                Icon = "🔷",
                Label = $"Part {i} — {Path.GetFileName(meshPaths[i])}",
                AssetData = new AssetData
                {
                    MeshPaths = [meshPaths[i]],
                    TexturePaths = [texturePaths[i]],
                    Asb = [asb[i]],
                    Adb = [adb[i]],
                }
            });
        }

        return node;
    }

    // ── Effect tree ───────────────────────────────────────────────────────

    private AssetNode BuildEffectRoot()
    {
        var root = new AssetNode { Icon = "✨", Label = $"Effects ({_gameData.Effects.Count})" };
        foreach (var effect in _gameData.Effects)
            root.Children.Add(BuildEffectNode(effect));
        return root;
    }

    private AssetNode BuildEffectNode(C3DEffectInfo effect)
    {
        var label = effect.Lev > 0
            ? $"[{effect.Key}]  Lev {effect.Lev}"
            : $"[{effect.Key}]";

        var meshPaths = new string[effect.Amount];
        var texPaths = new string[effect.Amount];
        var asbArr = new int[effect.Amount];
        var adbArr = new int[effect.Amount];

        for (int i = 0; i < effect.Amount; i++)
        {
            meshPaths[i] = _gameData.ResolveEffectObj(effect.EffectIds[i])
                           ?? $"? ({effect.EffectIds[i]})";
            texPaths[i] = _gameData.ResolveTexture((ulong)effect.TextureIds[i])
                           ?? $"? ({effect.TextureIds[i]})";
            asbArr[i] = effect.Asb[i];
            adbArr[i] = effect.Adb[i];
        }

        var node = new AssetNode
        {
            Icon = "✨",
            Label = label,
            AssetData = effect.Amount > 0
                ? new AssetData
                {
                    MeshPaths = meshPaths,
                    TexturePaths = texPaths,
                    Asb = asbArr,
                    Adb = adbArr,
                }
                : null
        };

        for (int i = 0; i < effect.Amount; i++)
        {
            if (meshPaths[i].StartsWith('?')) continue;

            node.Children.Add(new AssetNode
            {
                Icon = "▫",
                Label = $"Slot {i} — EfxId {effect.EffectIds[i]}  ·  {Path.GetFileName(texPaths[i])}",
                AssetData = new AssetData
                {
                    MeshPaths = [meshPaths[i]],
                    TexturePaths = [texPaths[i]],
                    Asb = [asbArr[i]],
                    Adb = [adbArr[i]],
                }
            });
        }

        return node;
    }

    // ── Armor tree ────────────────────────────────────────────────────────

    private AssetNode BuildArmorRoot()
    {
        var root = new AssetNode { Icon = "🛡", Label = $"Armors ({_gameData.Armors.Count})" };
        foreach (var armor in _gameData.Armors)
            root.Children.Add(BuildArmorNode(armor));
        return root;
    }

    private AssetNode BuildArmorNode(ArmorTypeInfo armor)
    {
        var (meshPaths, texturePaths, asb, adb) = BuildMeshArraysForArmor(armor);

        var node = new AssetNode
        {
            Icon = "🛡",
            Label = $"[{armor.Id}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                Asb = asb,
                Adb = adb,
            }
        };

        for (int i = 0; i < armor.Parts; i++)
        {
            if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                continue;

            node.Children.Add(new AssetNode
            {
                Icon = "🔷",
                Label = $"Part {i} — {Path.GetFileName(meshPaths[i])}",
                AssetData = new AssetData
                {
                    MeshPaths = [meshPaths[i]],
                    TexturePaths = [texturePaths[i]],
                    Asb = [asb[i]],
                    Adb = [adb[i]],
                }
            });
        }

        return node;
    }

    // ── Armet tree ────────────────────────────────────────────────────────

    private AssetNode BuildArmetRoot()
    {
        var root = new AssetNode { Icon = "⛑", Label = $"Armets ({_gameData.Armets.Count})" };
        foreach (var armet in _gameData.Armets)
            root.Children.Add(BuildArmetNode(armet));
        return root;
    }

    private AssetNode BuildArmetNode(ArmetTypeInfo armet)
    {
        var (meshPaths, texturePaths, asb, adb) = BuildMeshArraysForArmet(armet);

        var node = new AssetNode
        {
            Icon = "⛑",
            Label = $"[{armet.Id}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                Asb = asb,
                Adb = adb,
            }
        };

        for (int i = 0; i < armet.Parts; i++)
        {
            if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                continue;

            node.Children.Add(new AssetNode
            {
                Icon = "🔷",
                Label = $"Part {i} — {Path.GetFileName(meshPaths[i])}",
                AssetData = new AssetData
                {
                    MeshPaths = [meshPaths[i]],
                    TexturePaths = [texturePaths[i]],
                    Asb = [asb[i]],
                    Adb = [adb[i]],
                }
            });
        }

        return node;
    }

    // ── Weapon tree ───────────────────────────────────────────────────────

    private AssetNode BuildWeaponRoot()
    {
        var root = new AssetNode { Icon = "⚔", Label = $"Weapons ({_gameData.Weapons.Count})" };
        foreach (var weapon in _gameData.Weapons)
            root.Children.Add(BuildWeaponNode(weapon));
        return root;
    }

    private AssetNode BuildWeaponNode(WeaponTypeInfo weapon)
    {
        var (meshPaths, texturePaths, asb, adb) = BuildMeshArraysForWeapon(weapon);

        var node = new AssetNode
        {
            Icon = "⚔",
            Label = $"[{weapon.Id}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                Asb = asb,
                Adb = adb,
            }
        };

        for (int i = 0; i < weapon.Parts; i++)
        {
            if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                continue;

            node.Children.Add(new AssetNode
            {
                Icon = "🔷",
                Label = $"Part {i} — {Path.GetFileName(meshPaths[i])}",
                AssetData = new AssetData
                {
                    MeshPaths = [meshPaths[i]],
                    TexturePaths = [texturePaths[i]],
                    Asb = [asb[i]],
                    Adb = [adb[i]],
                }
            });
        }

        return node;
    }

    // ── Transform tree ────────────────────────────────────────────────────

    private AssetNode BuildTransformRoot()
    {
        var root = new AssetNode { Icon = "🔀", Label = $"Transforms ({_gameData.Transforms.Count})" };
        foreach (var t in _gameData.Transforms)
            root.Children.Add(BuildTransformNode(t));
        return root;
    }

    private AssetNode BuildTransformNode(TransformInfo t)
    {
        AssetData? assetData = null;

        var armor = _gameData.FindArmor((uint)(t.Look * 1_000_000));
        if (armor != null)
        {
            var (meshPaths, texturePaths, asb, adb) = BuildMeshArraysForArmor(armor);
            if (meshPaths.Length > 0)
                assetData = new AssetData
                {
                    MeshPaths = meshPaths,
                    TexturePaths = texturePaths,
                    Asb = asb,
                    Adb = adb,
                };
        }

        return new AssetNode
        {
            Icon = "🔀",
            Label = $"[{t.Index}]",
            AssetData = assetData,
        };
    }

    // ── Mount tree ────────────────────────────────────────────────────────

    private AssetNode BuildMountRoot()
    {
        var root = new AssetNode { Icon = "🐴", Label = $"Mounts ({_gameData.Mounts.Count})" };
        foreach (var mount in _gameData.Mounts)
            root.Children.Add(BuildMountNode(mount));
        return root;
    }

    private AssetNode BuildMountNode(MountTypeInfo mount)
    {
        var (meshPaths, texturePaths, asb, adb) = BuildMeshArraysForMount(mount);

        var node = new AssetNode
        {
            Icon = "🐴",
            Label = $"[{mount.Id}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                Asb = asb,
                Adb = adb,
            }
        };

        for (int i = 0; i < mount.Parts; i++)
        {
            if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                continue;

            node.Children.Add(new AssetNode
            {
                Icon = "🔷",
                Label = $"Part {i} — {Path.GetFileName(meshPaths[i])}",
                AssetData = new AssetData
                {
                    MeshPaths = [meshPaths[i]],
                    TexturePaths = [texturePaths[i]],
                    Asb = [asb[i]],
                    Adb = [adb[i]],
                }
            });
        }

        return node;
    }

    // ── Mesh-array helpers ────────────────────────────────────────────────
    // All helpers return (Paths, Textures, Asb, Adb) so blend state flows
    // from every type's ini data through AssetData into the renderer.

    /// <summary>SimpleObj has no Asb/Adb — fills arrays with the supplied defaults (or 5/6).</summary>
    private (string[] Paths, string[] Textures, int[] Asb, int[] Adb) BuildMeshArrays(
        C3DSimpleObjInfo obj, int defaultAsb = 5, int defaultAdb = 6)
    {
        var meshes = new string[obj.Parts];
        var textures = new string[obj.Parts];
        var asb = new int[obj.Parts];
        var adb = new int[obj.Parts];
        for (int i = 0; i < obj.Parts; i++)
        {
            meshes[i] = _gameData.ResolveMesh(obj.MeshIds[i]) ?? $"? ({obj.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(obj.TextureIds[i]) ?? $"? ({obj.TextureIds[i]})";
            asb[i] = defaultAsb;
            adb[i] = defaultAdb;
        }
        return (meshes, textures, asb, adb);
    }

    private (string[] Paths, string[] Textures, int[] Asb, int[] Adb) BuildMeshArraysForArmor(
        ArmorTypeInfo armor)
    {
        var meshes = new string[armor.Parts];
        var textures = new string[armor.Parts];
        var asb = new int[armor.Parts];
        var adb = new int[armor.Parts];
        for (int i = 0; i < armor.Parts; i++)
        {
            meshes[i] = _gameData.ResolveMesh(armor.MeshIds[i]) ?? $"? ({armor.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(armor.TextureIds[i]) ?? $"? ({armor.TextureIds[i]})";
            asb[i] = armor.Asb[i];
            adb[i] = armor.Adb[i];
        }
        return (meshes, textures, asb, adb);
    }

    private (string[] Paths, string[] Textures, int[] Asb, int[] Adb) BuildMeshArraysForArmet(
        ArmetTypeInfo armet)
    {
        var meshes = new string[armet.Parts];
        var textures = new string[armet.Parts];
        var asb = new int[armet.Parts];
        var adb = new int[armet.Parts];
        for (int i = 0; i < armet.Parts; i++)
        {
            meshes[i] = _gameData.ResolveMesh(armet.MeshIds[i]) ?? $"? ({armet.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(armet.TextureIds[i]) ?? $"? ({armet.TextureIds[i]})";
            asb[i] = armet.Asb[i];
            adb[i] = armet.Adb[i];
        }
        return (meshes, textures, asb, adb);
    }

    private (string[] Paths, string[] Textures, int[] Asb, int[] Adb) BuildMeshArraysForWeapon(
        WeaponTypeInfo weapon)
    {
        var meshes = new string[weapon.Parts];
        var textures = new string[weapon.Parts];
        var asb = new int[weapon.Parts];
        var adb = new int[weapon.Parts];
        for (int i = 0; i < weapon.Parts; i++)
        {
            meshes[i] = _gameData.ResolveMesh(weapon.MeshIds[i]) ?? $"? ({weapon.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(weapon.TextureIds[i]) ?? $"? ({weapon.TextureIds[i]})";
            asb[i] = weapon.Asb[i];
            adb[i] = weapon.Adb[i];
        }
        return (meshes, textures, asb, adb);
    }

    private (string[] Paths, string[] Textures, int[] Asb, int[] Adb) BuildMeshArraysForMount(
        MountTypeInfo mount)
    {
        var meshes = new string[mount.Parts];
        var textures = new string[mount.Parts];
        var asb = new int[mount.Parts];
        var adb = new int[mount.Parts];
        for (int i = 0; i < mount.Parts; i++)
        {
            meshes[i] = _gameData.ResolveMesh(mount.MeshIds[i]) ?? $"? ({mount.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(mount.TextureIds[i]) ?? $"? ({mount.TextureIds[i]})";
            asb[i] = mount.Asb[i];
            adb[i] = mount.Adb[i];
        }
        return (meshes, textures, asb, adb);
    }

    /// <summary>
    /// Resolves all renderable slots for a named effect key (e.g. "1ghost", "10000"),
    /// including per-slot Asb/Adb from the effect's ini data.
    /// Returns empty arrays when the key is unknown or has no resolvable slots.
    /// </summary>
    private (string[] Meshes, string[] Textures, int[] Asb, int[] Adb) BuildEffectParts(string effectKey)
    {
        var effect = _gameData.FindEffect(effectKey);
        if (effect == null || effect.Amount == 0) return ([], [], [], []);

        var meshes = new List<string>(effect.Amount);
        var textures = new List<string>(effect.Amount);
        var asb = new List<int>(effect.Amount);
        var adb = new List<int>(effect.Amount);

        for (int i = 0; i < effect.Amount; i++)
        {
            var mesh = _gameData.ResolveEffectObj(effect.EffectIds[i]);
            if (string.IsNullOrEmpty(mesh)) continue;   // unresolvable slot — skip

            meshes.Add(mesh);
            textures.Add(_gameData.ResolveTexture((ulong)effect.TextureIds[i])
                         ?? $"? ({effect.TextureIds[i]})");
            asb.Add(effect.Asb[i]);
            adb.Add(effect.Adb[i]);
        }

        return (meshes.ToArray(), textures.ToArray(), asb.ToArray(), adb.ToArray());
    }

    private MotionData[] BuildMotionEntries(NpcTypeInfo npc)
    {
        var entries = new List<MotionData>();
        TryAddMotion(entries, "StandBy", npc.StandByMotionId);
        TryAddMotion(entries, "Blaze", npc.BlazeMotionId);
        TryAddMotion(entries, "Rest", npc.RestMotionId);
        return entries.ToArray();
    }

    private void TryAddMotion(List<MotionData> entries, string label, ulong id)
    {
        if (id == 0) return;
        var path = _gameData.ResolveMotion(id);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    // ── Search / filter ───────────────────────────────────────────────────

    private void RefreshFilter()
    {
        FilteredAssetTree.Clear();
        string q = _searchText?.Trim() ?? string.Empty;

        foreach (var root in AssetTree)
        {
            var node = FilterNode(root, q);
            if (node != null)
                FilteredAssetTree.Add(node);
        }
    }

    private static AssetNode? FilterNode(AssetNode node, string query)
    {
        if (string.IsNullOrEmpty(query))
            return node;

        bool selfMatch = node.Label.Contains(query, StringComparison.OrdinalIgnoreCase);

        if (node.Children.Count == 0)
            return selfMatch ? node : null;

        var matchedChildren = node.Children
            .Select(c => FilterNode(c, query))
            .OfType<AssetNode>()
            .ToList();

        if (!selfMatch && matchedChildren.Count == 0)
            return null;

        var proxy = new AssetNode
        {
            Icon = node.Icon,
            Label = node.Label,
            AssetData = node.AssetData
        };

        var childSource = selfMatch ? node.Children : matchedChildren;
        foreach (var child in childSource)
            proxy.Children.Add(child);

        return proxy;
    }

    // ── Asset loading ─────────────────────────────────────────────────────

    private void LoadAssetNode(AssetNode node)
    {
        if (node.AssetData is not { } data || _game == null) return;

        LoadParts(data);
        SelectFirstMotion(data);
    }

    /// <summary>
    /// Converts AssetData into the (MeshPath, TexturePath, Asb, Adb) tuples
    /// that C3StudioGame.LoadC3Parts expects, so blend state flows from the ini
    /// data all the way to the GPU.
    /// </summary>
    private void LoadParts(AssetData data)
    {
        if (data.MeshPaths.Length == 0) return;

        ModelPath = data.MeshPaths.Length == 1 ? data.MeshPaths[0] : $"{data.MeshPaths.Length} parts";
        TexturePath = data.TexturePaths.Length == 1 ? data.TexturePaths[0] : string.Empty;

        var parts = data.MeshPaths
            .Select((mesh, i) =>
            {
                string? tex = i < data.TexturePaths.Length ? data.TexturePaths[i] : null;
                // Manual texture override applies only to single-part loads
                if (data.MeshPaths.Length == 1 && !string.IsNullOrEmpty(TexturePath))
                    tex = TexturePath;
                return (MeshPath: mesh, TexturePath: tex, Asb: data.GetAsb(i), Adb: data.GetAdb(i));
            })
            .Where(p => !string.IsNullOrEmpty(p.MeshPath));

        try
        {
            _game!.LoadC3Parts(parts, motionPath: null); // motion applied separately
            StatusMessage = data.MeshPaths.Length == 1
                ? $"Loaded: {Path.GetFileName(data.MeshPaths[0])}"
                : $"Loaded {data.MeshPaths.Length} parts";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void SelectFirstMotion(AssetData data)
    {
        if (data.Motions.Length > 0)
        {
            _selectedMotion = data.Motions[0];
            OnPropertyChanged(nameof(SelectedMotion));
            ApplyMotionSilent(data.Motions[0].Path);
        }
        else
        {
            _selectedMotion = null;
            OnPropertyChanged(nameof(SelectedMotion));
        }
    }

    private void ApplyMotionSilent(string path)
    {
        if (_game == null || string.IsNullOrEmpty(path)) return;
        try
        {
            _game.ChangeMotion(path);
            MotionPath = path;
            StatusMessage += $" · Motion: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Motion error: {ex.Message}";
        }
    }

    // ── Browse / manual load ──────────────────────────────────────────────

    private void BrowseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select one or more C3 model files",
            Filter = "C3 files (*.c3)|*.c3|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dlg.ShowDialog() != DialogResult.OK || dlg.FileNames.Length == 0)
            return;

        _modelPaths = dlg.FileNames.ToList();

        ModelPath = _modelPaths.Count == 1
            ? _modelPaths[0]
            : $"{_modelPaths.Count} files selected";

        TexturePath = _modelPaths.Count == 1
            ? FindTexture(_modelPaths[0])
            : string.Empty;
    }

    private static string FindTexture(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return string.Empty;
        foreach (var ext in new[] { ".dds", ".tga", ".png", ".jpg" })
        {
            string p = Path.Combine(dir, baseName + ext);
            if (File.Exists(p)) return p;
        }
        return string.Empty;
    }

    /// <summary>
    /// Manual load: blend defaults to 5/6 since there's no type-info source.
    /// </summary>
    private void LoadModel()
    {
        if (_game == null || _modelPaths.Count == 0) return;

        try
        {
            IEnumerable<(string MeshPath, string? TexturePath, int Asb, int Adb)> parts;

            if (_modelPaths.Count == 1)
            {
                var tex = !string.IsNullOrEmpty(TexturePath) ? TexturePath : FindTexture(_modelPaths[0]);
                parts = [(MeshPath: _modelPaths[0], TexturePath: string.IsNullOrEmpty(tex) ? null : tex, Asb, Adb)];
            }
            else
            {
                parts = _modelPaths.Select(mesh =>
                {
                    string tex = FindTexture(mesh);
                    return (MeshPath: mesh, TexturePath: string.IsNullOrEmpty(tex) ? null : tex, Asb, Adb);
                });
            }

            _game.LoadC3Parts(parts, motionPath: MotionPath);

            StatusMessage = _modelPaths.Count == 1
                ? $"Loaded: {Path.GetFileName(_modelPaths[0])}"
                : $"Loaded {_modelPaths.Count} files";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void BrowseTexture()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Texture files (*.dds;*.tga;*.png;*.jpg)|*.dds;*.tga;*.png;*.jpg|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK) TexturePath = dlg.FileName;
    }

    private void BrowseMotion()
    {
        using var dlg = new OpenFileDialog { Filter = "C3 motion files (*.c3)|*.c3|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == DialogResult.OK) MotionPath = dlg.FileName;
    }

    private void ApplyMotion() => ApplyMotionSilent(MotionPath);

    // ── Playback ──────────────────────────────────────────────────────────

    private void TogglePlay()
    {
        if (_game == null) return;
        _game.IsPlaying = !_game.IsPlaying;
        PlayPauseLabel = _game.IsPlaying ? "⏸" : "▶";
    }

    private void OnFrameChanged(int current, int total) =>
        FrameLabel = $"{current} / {total}";
}