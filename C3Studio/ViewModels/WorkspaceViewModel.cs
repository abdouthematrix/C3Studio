using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.Loading;
using C3Studio.MonoGame;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public ObservableCollection<MeshSlotViewModel> MeshSlots { get; } = new();
    private bool _meshSlotsAreAllVisible;
    public bool MeshSlotsAreAllVisible
    {
        get => MeshSlots.All(s => s.IsVisible);
        set
        {
            foreach (var slot in MeshSlots)
                slot.IsVisible = value;
            Set(ref _meshSlotsAreAllVisible, value);
        }
    }

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
        GoToRoleViewerCommand = Cmd(() => { _game?.Dispose(); _nav.GoToRoleViewer(); });

    }

    public ICommand GoToSetupCommand { get; }
    public ICommand GoToRoleViewerCommand { get; }

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
                $"{_gameData.SimpleRoles.Count} roles, " +
                $"{_gameData.Effects.Count} effects, {_gameData.MagicSkills.Count} magic skills, " +
                $"{_gameData.Transforms.Count} transforms." +
                $"{_gameData.ItemTextures.Count} item textures.";
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
        _game.ModelLoaded += OnModelLoaded;
        _game.SetFps(_fps);
    }
    private void OnModelLoaded()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            MeshSlots.Clear();
            if (_game == null) return;

            foreach (var name in _game.GetMeshNames())
            {
                MeshSlots.Add(new MeshSlotViewModel(
                    name,
                    _game.GetMeshVisibility(name),
                    (slotName, isVisible) => _game.SetMeshVisibility(slotName, isVisible)
                ));
            }
        });
    }

    // ── Asset tree construction ───────────────────────────────────────────

    private void BuildAssetTree()
    {
        AssetTree.Clear();
        AssetTree.Add(BuildNpcRoot());
        AssetTree.Add(BuildSimpleObjRoot());
        AssetTree.Add(BuildSimpleRoleRoot());
        AssetTree.Add(BuildEffectRoot());
        AssetTree.Add(BuildMagicSkillRoot());
        AssetTree.Add(BuildTransformRoot());
        AssetTree.Add(BuildRolePartsRoot());
        AssetTree.Add(BuildItemTextureRoot());
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
        var obj = _gameData.FindSimpleObj(npc.SimpleObjId);
        var motions = BuildMotionEntries(npc);

        // ── Main mesh (SimpleObj) ─────────────────────────────────────────
        string[] objMeshes = [], objTextures = [];
        int[] objAsb = [], objAdb = [];
        uint[] rolepartsids = [];
        if (obj != null)
            (objMeshes, objTextures, rolepartsids, objAsb, objAdb) = BuildMeshArrays(obj, npc.Asb, npc.Adb);

        // ── Effect parts ──────────────────────────────────────────────────
        string[] efxMeshes = [], efxTextures = [];
        int[] efxAsb = [], efxAdb = [];
        if (!string.IsNullOrEmpty(npc.Effect))
            (efxMeshes, efxTextures, efxAsb, efxAdb) = BuildEffectParts(npc.Effect);

        // ── Root node — loads everything combined ─────────────────────────
        var efxDescriptors = BuildEffectDescriptors(efxMeshes, efxTextures, efxAsb, efxAdb);

        var node = new AssetNode { Icon = "⚔", Label = $"[{npc.NpcType}] {npc.Name}" };

        if (objMeshes.Length > 0 || motions.Length > 0 || efxDescriptors.Length > 0)
        {
            node.AssetData = new AssetData
            {
                MeshPaths = objMeshes,
                TexturePaths = objTextures,
                RolePartsIds = rolepartsids,
                Motions = motions,
                Asb = objAsb,
                Adb = objAdb,
                Effects = efxDescriptors,
            };
        }

        // When both mesh and effect exist, group them under labelled headers.
        // When only one exists, hoist its parts directly as children (no extra wrapper).
        bool hasMesh = objMeshes.Length > 0;
        bool hasEfx = efxMeshes.Length > 0;
        bool grouped = hasMesh && hasEfx;

        // ── Main NPC Mesh parts ───────────────────────────────────────────
        if (hasMesh)
        {
            AssetNode meshContainer;
            if (grouped)
            {
                meshContainer = new AssetNode
                {
                    Icon = "🔷",
                    Label = $"{npc.SimpleObjId}",
                    AssetData = new AssetData
                    {
                        MeshPaths = objMeshes,
                        TexturePaths = objTextures,
                        RolePartsIds = rolepartsids,
                        Motions = motions,
                        Asb = objAsb,
                        Adb = objAdb,
                    }
                };
                node.Children.Add(meshContainer);
            }
            else
            {
                meshContainer = node; // hoist directly onto root
            }

            if (objMeshes.Length > 1)
                for (int i = 0; i < objMeshes.Length; i++)
                {
                    if (string.IsNullOrEmpty(objMeshes[i]) || objMeshes[i].StartsWith('?'))
                        continue;

                    meshContainer.Children.Add(new AssetNode
                    {
                        Icon = "▫",
                        Label = Path.GetFileNameWithoutExtension(objMeshes[i]),
                        AssetData = new AssetData
                        {
                            MeshPaths = [objMeshes[i]],
                            TexturePaths = [objTextures[i]],
                            RolePartsIds = [rolepartsids[i]],
                            Motions = motions,
                            Asb = [objAsb[i]],
                            Adb = [objAdb[i]],
                        }
                    });
                }
        }

        // ── Effect Nodes ──────────────────────────────────────────────────
        if (hasEfx)
        {
            AssetNode efxContainer;
            if (grouped)
            {
                efxContainer = new AssetNode
                {
                    Icon = "✨",
                    Label = npc.Effect,
                    AssetData = new AssetData
                    {
                        Effects = BuildEffectDescriptors(efxMeshes, efxTextures, efxAsb, efxAdb)
                    }
                };
                node.Children.Add(efxContainer);
            }
            else
            {
                efxContainer = node; // hoist directly onto root
            }

            if (efxMeshes.Length > 1)
                for (int i = 0; i < efxMeshes.Length; i++)
                {
                    if (efxMeshes[i].StartsWith('?')) continue;

                    efxContainer.Children.Add(new AssetNode
                    {
                        Icon = "▫",
                        Label = Path.GetFileNameWithoutExtension(efxMeshes[i]),
                        AssetData = new AssetData
                        {
                            Effects = BuildEffectDescriptors([efxMeshes[i]], [efxTextures[i]], [efxAsb[i]], [efxAdb[i]])
                        }
                    });
                }
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
        var (meshPaths, texturePaths, rolepartsids, asb, adb) = BuildMeshArrays(obj);

        var node = new AssetNode
        {
            Icon = "📦",
            Label = $"[{obj.IdType}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                RolePartsIds = rolepartsids,
                Asb = asb,
                Adb = adb,
            }
        };

        if (obj.Parts > 1)
            for (int i = 0; i < obj.Parts; i++)
            {
                if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                    continue;

                node.Children.Add(new AssetNode
                {
                    Icon = "🔷",
                    Label = Path.GetFileNameWithoutExtension(meshPaths[i]),
                    AssetData = new AssetData
                    {
                        MeshPaths = [meshPaths[i]],
                        TexturePaths = [texturePaths[i]],
                        RolePartsIds = [rolepartsids[i]],
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
        var label = effect.Key;
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
                    Effects = BuildEffectDescriptors(meshPaths, texPaths, asbArr, adbArr)
                }
                : null
        };

        if (effect.Amount > 1)
            for (int i = 0; i < effect.Amount; i++)
            {
                if (meshPaths[i].StartsWith('?')) continue;

                node.Children.Add(new AssetNode
                {
                    Icon = "▫",
                    Label = Path.GetFileNameWithoutExtension(meshPaths[i]),
                    AssetData = new AssetData
                    {
                        Effects = BuildEffectDescriptors([meshPaths[i]], [texPaths[i]], [asbArr[i]], [adbArr[i]])
                    }
                });
            }

        return node;
    }

    // ── Magic Skill tree ──────────────────────────────────────────────────

    /// <summary>
    /// Root node: "🔮 Magic Skills (N)"
    ///
    /// Tree layout:
    ///   🔮 Magic Skills
    ///     └── 🔮 [100000] Thunder          ← base group — combined AssetData of all resolved effects
    ///          ├── 🎨 [Skin 1] ChasingFire ← skin override child (only when NameN keys exist)
    ///          ├── 🎨 [Skin 2] Aspired-PrairieFire
    ///          └── 📶 [100001]             ← level child (shown when effects differ from base)
    ///               └── (per-effect children if multi-effect)
    /// </summary>
    private AssetNode BuildMagicSkillRoot()
    {
        var root = new AssetNode
        {
            Icon = "🔮",
            Label = $"Magic Skills ({_gameData.MagicSkills.Count})"
        };

        // Group the dictionary entries by the SortOfAct of their first level
        var groupedSkills = _gameData.MagicSkills
            .GroupBy(kvp => kvp.Value.Levels.Values.FirstOrDefault()?.SortOfAct);

        foreach (var sortGroup in groupedSkills)
        {
            // Handle potential nulls if a group somehow has no levels
            string sortLabel = sortGroup.Key?.ToString() ?? "Unknown";

            var sortNode = new AssetNode
            {
                Icon = "📁",
                Label = $"{sortLabel} ({sortGroup.Count()})"
            };

            foreach (var (baseId, group) in sortGroup)
            {
                sortNode.Children.Add(BuildMagicSkillGroupNode(baseId, group));
            }

            root.Children.Add(sortNode);
        }

        // Optional: Alphabetize the folders so they appear in a consistent order
        // root.Children = root.Children.OrderBy(c => c.Label).ToList();

        return root;
    }

    private AssetNode BuildMagicSkillGroupNode(int baseId, MagicSkillGroup group)
    {
        var baseEffect = group.Levels.GetValueOrDefault(baseId);
        string baseName = baseEffect?.Name ?? string.Empty;
        string label = string.IsNullOrWhiteSpace(baseName)
                            ? $"[{baseId}]"
                            : $"[{baseId}] {baseName}";

        // Collect all effect-field assets for the base level.
        var (baseMeshes, baseTextures, baseAsb, baseAdb, baseEffectParts) =
            ResolveMagicEffectParts(baseEffect);

        var node = new AssetNode
        {
            Icon = "🔮",
            Label = label,
            AssetData = baseMeshes.Length > 0
                ? new AssetData
                {
                    Effects = BuildEffectDescriptors(baseMeshes, baseTextures, baseAsb, baseAdb)
                }
                : null
        };

        // ── Per-effect-field children (only when base has multiple effect refs) ──
        if (baseEffectParts.Count > 1)
            foreach (var (fieldLabel, data) in baseEffectParts)
                node.Children.Add(new AssetNode
                {
                    Icon = "✨",
                    Label = fieldLabel,
                    AssetData = data,
                });

        // ── Skin children ─────────────────────────────────────────────────────
        // Skins exist when the ini contains Name1, Name2, … (stored by MagicEffectLoader).
        foreach (var (skinId, overrides) in group.Skins.OrderBy(s => s.Key))
        {
            var skinEffect = group.GetEffect(baseId, skinId);
            string skinName = overrides.TryGetValue("Name", out var n) ? n : $"Skin {skinId}";

            var (sMeshes, sTextures, sAsb, sAdb, sEffectParts) =
                ResolveMagicEffectParts(skinEffect);

            var skinNode = new AssetNode
            {
                Icon = "🎨",
                Label = $"[Skin {skinId}] {skinName}",
                AssetData = sMeshes.Length > 0
                    ? new AssetData
                    {
                        Effects = BuildEffectDescriptors(sMeshes, sTextures, sAsb, sAdb)
                    }
                    : null
            };

            if (sEffectParts.Count > 1)
                foreach (var (fieldLabel, data) in sEffectParts)
                    skinNode.Children.Add(new AssetNode
                    {
                        Icon = "✨",
                        Label = fieldLabel,
                        AssetData = data,
                    });

            node.Children.Add(skinNode);
        }

        // ── Level children ────────────────────────────────────────────────────
        // Show every non-base level; inherit from base (MagicEffectLoader already cloned).
        foreach (var (lvlId, lvlEffect) in group.Levels
                                                .Where(kv => kv.Key != baseId)
                                                .OrderBy(kv => kv.Key))
        {
            var (lMeshes, lTextures, lAsb, lAdb, lEffectParts) =
                ResolveMagicEffectParts(lvlEffect);

            // Only promote to loadable if the level's effects differ visually from base.
            bool visuallyDistinct = !MagicEffectKeysEqual(baseEffect, lvlEffect);

            var lvlNode = new AssetNode
            {
                Icon = "📶",
                Label = $"[{lvlId}]",
                AssetData = visuallyDistinct && lMeshes.Length > 0
                    ? new AssetData
                    {
                        Effects = BuildEffectDescriptors(lMeshes, lTextures, lAsb, lAdb)
                    }
                    : null
            };

            node.Children.Add(lvlNode);
        }

        return node;
    }

    /// <summary>
    /// Resolves every 3D-effect field of <paramref name="effect"/> into mesh/texture arrays,
    /// returning both the combined flat arrays (for the node's AssetData) and
    /// a per-field list for optional child-node expansion.
    /// </summary>
    private (string[] Meshes, string[] Textures, int[] Asb, int[] Adb,
             List<(string Label, AssetData Data)> Parts)
        ResolveMagicEffectParts(MagicEffect? effect)
    {
        var parts = new List<(string Label, AssetData Data)>();

        if (effect == null)
            return ([], [], [], [], parts);

        // All string fields that reference a C3DEffect key.
        // We capture both the human-readable label and the key value.
        var effectRefs = new (string Label, string? Key)[]
        {
        ("IntoneEffect",                        effect.IntoneEffect),
        ("TraceEffect",                         effect.TraceEffect),
        ("Role3DEffectOfAttacker",              effect.Role3DEffectOfAttacker),
        ("Role3DEffectOfAttaker",               effect.Role3DEffectOfAttaker),
        ("Role3DEffectOfAffectTarget",          effect.Role3DEffectOfAffectTarget),
        ("Role3DEffectOfTarget",                effect.Role3DEffectOfTarget),
        ("Role3DEffectOfTargetHit",             effect.Role3DEffectOfTargetHit),
        ("Role3DEffectOfTargetMiss",            effect.Role3DEffectOfTargetMiss),
        ("Role3DEffectOfTargetExtraCmdBegin",   effect.Role3DEffectOfTargetExtraCmdBegin),
        ("TerrainEffect",                       effect.TerrainEffect),
        ("MapEffect",                           effect.MapEffect),
        ("WarningEffectOfTarget",               effect.WarningEffectOfTarget),
        ("WarningEffOnTarget",                  effect.WarningEffOnTarget),
        };

        // Deduplicate keys so the same effect isn't resolved twice.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allMeshes = new List<string>();
        var allTextures = new List<string>();
        var allAsb = new List<int>();
        var allAdb = new List<int>();

        foreach (var (fieldLabel, key) in effectRefs)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!seen.Add(key)) continue;           // already resolved

            var (meshes, textures, asb, adb) = BuildEffectParts(key);
            if (meshes.Length == 0)
                continue;       // effect key not found or no mesh

            var partData = new AssetData
            {
                Effects = BuildEffectDescriptors(meshes, textures, asb, adb)
            };

            parts.Add(($"{fieldLabel}: {key}", partData));

            allMeshes.AddRange(meshes);
            allTextures.AddRange(textures);
            allAsb.AddRange(asb);
            allAdb.AddRange(adb);
        }

        return (allMeshes.ToArray(), allTextures.ToArray(),
                allAsb.ToArray(), allAdb.ToArray(), parts);
    }

    /// <summary>
    /// Returns <c>true</c> when every 3D-effect key field is identical between
    /// <paramref name="a"/> and <paramref name="b"/> (case-insensitive).
    /// Used to decide whether a level child needs its own AssetData.
    /// </summary>
    private static bool MagicEffectKeysEqual(MagicEffect? a, MagicEffect? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        static bool Eq(string? x, string? y) =>
            string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        return Eq(a.IntoneEffect, b.IntoneEffect)
            && Eq(a.TraceEffect, b.TraceEffect)
            && Eq(a.Role3DEffectOfAttacker, b.Role3DEffectOfAttacker)
            && Eq(a.Role3DEffectOfAttaker, b.Role3DEffectOfAttaker)
            && Eq(a.Role3DEffectOfAffectTarget, b.Role3DEffectOfAffectTarget)
            && Eq(a.Role3DEffectOfTarget, b.Role3DEffectOfTarget)
            && Eq(a.Role3DEffectOfTargetHit, b.Role3DEffectOfTargetHit)
            && Eq(a.Role3DEffectOfTargetMiss, b.Role3DEffectOfTargetMiss)
            && Eq(a.Role3DEffectOfTargetExtraCmdBegin, b.Role3DEffectOfTargetExtraCmdBegin)
            && Eq(a.TerrainEffect, b.TerrainEffect)
            && Eq(a.MapEffect, b.MapEffect)
            && Eq(a.WarningEffectOfTarget, b.WarningEffectOfTarget)
            && Eq(a.WarningEffOnTarget, b.WarningEffOnTarget);
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

        var armor = _gameData.FindRolePart((uint)(t.Look * 1_000_000), RolePartType.Armor);
        if (armor != null)
        {
            var (meshPaths, texturePaths, rolepartsids, asb, adb) = BuildMeshArraysForRolePart(armor);
            if (meshPaths.Length > 0)
                assetData = new AssetData
                {
                    MeshPaths = meshPaths,
                    TexturePaths = texturePaths,
                    RolePartsIds = rolepartsids,
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

    // ── Item Texture tree ─────────────────────────────────────────────────

    private AssetNode BuildItemTextureRoot()
    {
        var root = new AssetNode { Icon = "🎨", Label = $"Item Textures ({_gameData.ItemTextures.Count})" };

        // Group by SubType first, then by Look/Gender
        var grouped = _gameData.ItemTextures
            .GroupBy(item => item.SubType) // group by subtype first
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                SubType = g.Key,
                LookGroups = g.GroupBy(item => item.Look)
                              .OrderBy(lg => lg.Key)
            });

        foreach (var subGroup in grouped)
        {
            var subNode = new AssetNode
            {
                Icon = "🔷",
                Label = $"Subtype {subGroup.SubType}"
            };

            foreach (var lookGroup in subGroup.LookGroups)
            {
                var lookNode = new AssetNode
                {
                    Icon = "👤",
                    Label = ((RoleLook)lookGroup.Key).ToDisplayString()
                };

                foreach (var item in lookGroup)
                    lookNode.Children.Add(BuildItemTextureNode(item));

                subNode.Children.Add(lookNode);
            }

            root.Children.Add(subNode);
        }

        return root;
    }
    private AssetNode BuildItemTextureNode(ItemTextureInfo item)
    {
        // Try to find the renderable mesh by matching the item ID across all equipment tables.
        var (baseMeshes, baseRolePartsIds, baseAsb, baseAdb) = TryFindItemMesh(item.Id);

        var parentNode = new AssetNode
        {
            Icon = "🎨",
            Label = $"[{item.Id}]  ({item.Amount} colors)",
        };

        uint previousTextureIds = 0;
        uint duplicates = 0;
        // Build one child per color variant.
        for (int c = 0; c < item.Amount && c < ItemTextureInfo.MaxColors; c++)
        {
            var colorVal = (ItemColor)item.Colors[c];
            uint texId = item.TextureIds[c];
            if (previousTextureIds == texId)
            {
                duplicates++;
                parentNode.Label = $"[{item.Id}]  ({item.Amount - duplicates} colors)";
                continue;
            }
            previousTextureIds = texId;
            string? texPath = _gameData.ResolveTexture(texId);

            // Skip entirely unresolvable slots.
            if (texPath == null && baseMeshes.Length == 0) continue;

            // Replace every texture slot with this color's texture.
            var texturePaths = new string[Math.Max(1, baseMeshes.Length)];
            for (int i = 0; i < texturePaths.Length; i++)
                texturePaths[i] = texPath ?? $"? ({texId})";

            var colorNode = new AssetNode
            {
                Icon = "🎨",//ColorIcon(colorVal),
                Label = $"{colorVal}  (tex {texId})",
                AssetData = baseMeshes.Length > 0
                    ? new AssetData
                    {
                        MeshPaths = baseMeshes,
                        TexturePaths = texturePaths,
                        RolePartsIds = baseRolePartsIds,
                        Asb = baseAsb,
                        Adb = baseAdb,
                    }
                    : null,
            };

            parentNode.Children.Add(colorNode);
        }

        return parentNode;
    }
    // ── SimpleRole  tree  ────────────────────────────────────────────────────

    private AssetNode BuildSimpleRoleRoot()
    {
        var root = new AssetNode { Icon = "🧑", Label = $"Simple Roles ({_gameData.SimpleRoles.Count})" };
        foreach (var role in _gameData.SimpleRoles)
            root.Children.Add(BuildSimpleRoleNode(role));
        return root;
    }    
    private AssetNode BuildSimpleRoleNode(SimpleRoleTypeInfo role)
    {
        var motions = new List<MotionData>();

        string[] bodyMeshes = [], bodyTextures = [];
        int[] bodyAsb = [], bodyAdb = [];
        uint[] rolepartsids = [];

        string[] hairMeshes = [], hairTextures = [];
        int[] hairAsb = [], hairAdb = [];
        uint[] hairRolePartIds = [];
        if (role.IsSimpleObjRole)
        {
            var obj = _gameData.FindSimpleObj(role.SimpleObjId);
            if (obj != null)
                (bodyMeshes, bodyTextures, rolepartsids, bodyAsb, bodyAdb) = BuildMeshArrays(obj);

            TryAddMotion(motions, "StandBy", role.StandByMotionId);
            TryAddMotion(motions, "Blaze", role.BlazeMotionId);
        }
        else if (role.IsEquipmentRole)
        {
            var armor = _gameData.FindRolePart(role.RawArmorId, RolePartType.Armor)
                     ?? _gameData.FindRolePart(role.EffectiveArmorId, RolePartType.Armor)
                     ?? (role.RawArmorId != 0 ? _gameData.FindRolePart((uint)(role.Look * 1_000_000), RolePartType.Armor) : null);
            if (armor != null)
                (bodyMeshes, bodyTextures, rolepartsids, bodyAsb, bodyAdb) = BuildMeshArraysForRolePart(armor);

            if (role.RawHairId != 0)
            {
                var armet = _gameData.FindRolePart(role.RawHairId, RolePartType.Armet)
                         ?? _gameData.FindRolePart(role.EffectiveHairId, RolePartType.Armet);
                if (armet != null)
                    (hairMeshes, hairTextures, hairRolePartIds, hairAsb, hairAdb) = BuildMeshArraysForRolePart(armet);
            }

            TryAddMotion(motions, "StandBy", (ulong)(role.Look * 10_000_000L + (int)RoleActionType.StandBy));
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                string name = action.ToString();
                ulong idBodyMotion = (ulong)(role.Look * 10_000_000L + (int)action);
                TryAddMotion(motions, name, idBodyMotion);
            }
        }

        string[] fxfMeshes = [], fxfTextures = [], fxbMeshes = [], fxbTextures = [];
        int[] fxfAsb = [], fxfAdb = [], fxbAsb = [], fxbAdb = [];

        if (!string.IsNullOrEmpty(role.FEffect))
            (fxfMeshes, fxfTextures, fxfAsb, fxfAdb) = BuildEffectParts(role.FEffect);
        if (!string.IsNullOrEmpty(role.BEffect))
            (fxbMeshes, fxbTextures, fxbAsb, fxbAdb) = BuildEffectParts(role.BEffect);

        var fxDescriptors = BuildEffectDescriptors(
            fxfMeshes.Concat(fxbMeshes).ToArray(),
            fxfTextures.Concat(fxbTextures).ToArray(),
            fxfAsb.Concat(fxbAsb).ToArray(),
            fxfAdb.Concat(fxbAdb).ToArray());

        // --- NEW: Map to Descriptors instead of flattening arrays ---
        var bodyDescriptors = bodyMeshes.Select((m, i) => new PartDescriptor("Body", m, bodyTextures[i], rolepartsids[i], bodyAsb[i], bodyAdb[i]))
                                        .Where(p => !string.IsNullOrEmpty(p.MeshPath) && !p.MeshPath.StartsWith('?'))
                                        .ToArray();

        var hairDescriptors = hairMeshes.Select((m, i) => new PartDescriptor("Armet", m, hairTextures[i], hairRolePartIds[i], hairAsb[i], hairAdb[i]))
                                        .Where(p => !string.IsNullOrEmpty(p.MeshPath) && !p.MeshPath.StartsWith('?'))
                                        .ToArray();

        var allRoleParts = bodyDescriptors.Concat(hairDescriptors).ToArray();
        var motionArr = motions.ToArray();

        var node = new AssetNode
        {
            Icon = "🧑",
            Label = role.Key,
        };

        // Feed to the new RoleParts property
        if (allRoleParts.Length > 0 || motionArr.Length > 0 || fxDescriptors.Length > 0)
        {
            node.AssetData = new AssetData
            {
                RoleParts = allRoleParts,
                Motions = motionArr,
                Effects = fxDescriptors,
            };
        }

        bool hasBody = bodyDescriptors.Length > 0;
        bool hasHair = hairDescriptors.Length > 0;
        bool hasFxF = fxfMeshes.Length > 0;
        bool hasFxB = fxbMeshes.Length > 0;
        int sectionCount = (hasBody ? 1 : 0) + (hasHair ? 1 : 0) + (hasFxF ? 1 : 0) + (hasFxB ? 1 : 0);
        bool grouped = sectionCount >= 2;

        // ── Body section ──
        if (hasBody)
        {
            AssetNode bodyContainer;
            if (grouped)
            {
                var bodyLabel = role.IsSimpleObjRole
                    ? $"{role.SimpleObjId}"
                    : $"[{role.EffectiveArmorId}] {(role.Look)}";

                bodyContainer = new AssetNode
                {
                    Icon = "🔷",
                    Label = bodyLabel,
                    AssetData = new AssetData { RoleParts = bodyDescriptors, Motions = motionArr }
                };
                node.Children.Add(bodyContainer);
            }
            else bodyContainer = node;

            if (bodyDescriptors.Length > 1)
                foreach (var part in bodyDescriptors)
                    bodyContainer.Children.Add(new AssetNode
                    {
                        Icon = "▫",
                        Label = Path.GetFileNameWithoutExtension(part.MeshPath),
                        AssetData = new AssetData { RoleParts = [part], Motions = motionArr }
                    });
        }

        // ── Hair section ──
        if (hasHair)
        {
            AssetNode hairContainer;
            if (grouped)
            {
                hairContainer = new AssetNode
                {
                    Icon = "💇",
                    Label = $"[{role.EffectiveHairId}] Hair",
                    AssetData = new AssetData { RoleParts = hairDescriptors, Motions = motionArr }
                };
                node.Children.Add(hairContainer);
            }
            else hairContainer = node;

            if (hairDescriptors.Length > 1)
                foreach (var part in hairDescriptors)
                    hairContainer.Children.Add(new AssetNode
                    {
                        Icon = "▫",
                        Label = Path.GetFileNameWithoutExtension(part.MeshPath),
                        AssetData = new AssetData { RoleParts = [part], Motions = motionArr }
                    });
        }

        // ── Front effect section ──
        if (hasFxF)
        {
            AssetNode fxfContainer;
            if (grouped)
            {
                fxfContainer = new AssetNode
                {
                    Icon = "✨",
                    Label = role.FEffect,
                    AssetData = new AssetData { Effects = BuildEffectDescriptors(fxfMeshes, fxfTextures, fxfAsb, fxfAdb) }
                };
                node.Children.Add(fxfContainer);
            }
            else fxfContainer = node;

            if (fxfMeshes.Length > 1)
                for (int i = 0; i < fxfMeshes.Length; i++)
                {
                    if (fxfMeshes[i].StartsWith('?')) continue;
                    fxfContainer.Children.Add(new AssetNode
                    {
                        Icon = "▫",
                        Label = Path.GetFileNameWithoutExtension(fxfMeshes[i]),
                        AssetData = new AssetData { Effects = BuildEffectDescriptors([fxfMeshes[i]], [fxfTextures[i]], [fxfAsb[i]], [fxfAdb[i]]) }
                    });
                }
        }

        // ── Back effect section ──
        if (hasFxB)
        {
            AssetNode fxbContainer;
            if (grouped)
            {
                fxbContainer = new AssetNode
                {
                    Icon = "✨",
                    Label = role.BEffect,
                    AssetData = new AssetData { Effects = BuildEffectDescriptors(fxbMeshes, fxbTextures, fxbAsb, fxbAdb) }
                };
                node.Children.Add(fxbContainer);
            }
            else fxbContainer = node;

            if (fxbMeshes.Length > 1)
                for (int i = 0; i < fxbMeshes.Length; i++)
                {
                    if (fxbMeshes[i].StartsWith('?')) continue;
                    fxbContainer.Children.Add(new AssetNode
                    {
                        Icon = "▫",
                        Label = Path.GetFileNameWithoutExtension(fxbMeshes[i]),
                        AssetData = new AssetData { Effects = BuildEffectDescriptors([fxbMeshes[i]], [fxbTextures[i]], [fxbAsb[i]], [fxbAdb[i]]) }
                    });
                }
        }

        return node;
    }

    // ── RolePart tree ────────────────────────────────────────────────────

    private AssetNode BuildRolePartsRoot()
    {
        var root = new AssetNode
        {
            Icon = "🎒",
            Label = $"Role Parts ({_gameData.RoleParts.Count})"
        };

        // Group by Part Type (Armor, Weapon, Mount, etc.)
        var groupedByType = _gameData.RoleParts
            .GroupBy(p => p.PartType)
            .OrderBy(g => g.Key);

        foreach (var typeGroup in groupedByType)
        {
            var typeNode = new AssetNode
            {
                Icon = GetIconForPartType(typeGroup.Key),
                Label = $"{typeGroup.Key}s ({typeGroup.Count()})"
            };

            // Sub-group by SubType
            var groupedBySubType = typeGroup.GroupBy(p => p.SubType).OrderBy(g => g.Key);

            foreach (var subGroup in groupedBySubType)
            {
                string subTypeLabel = GetSubTypeLabel(typeGroup.Key, subGroup.Key);
                var subTypeNode = new AssetNode { Icon = "🔷", Label = subTypeLabel };

                // Apply type-specific grouping (Look for Armors/Armets, Level for Mounts)
                if (typeGroup.Key == RolePartType.Armor || typeGroup.Key == RolePartType.Armet || typeGroup.Key == RolePartType.Misc || typeGroup.Key == RolePartType.Pelvis || typeGroup.Key == RolePartType.Head || typeGroup.Key == RolePartType.Cape)
                {
                    // Your cleaner grouping approach
                    var knownLooks = subGroup.Where(a =>
                      (a.Look >= 1 && a.Look <= 4) ||
                      (a.Look == 7 || a.Look == 8))
                          .GroupBy(a => a.Look)
                          .OrderBy(lg => lg.Key);

                    var otherLooks = subGroup.Where(a =>
                                         (a.Look < 1 || a.Look > 4) &&
                                         (a.Look != 7 && a.Look != 8));


                    // 1. Process the standard looks (1-4)
                    foreach (var lookGroup in knownLooks)
                    {
                        var lookNode = new AssetNode { Icon = "👤", Label = ((RoleLook)lookGroup.Key).ToDisplayString() };

                        foreach (var part in lookGroup)
                            lookNode.Children.Add(BuildRolePartNode(part));

                        subTypeNode.Children.Add(lookNode);
                    }

                    // 2. Process the "Other" bucket
                    if (otherLooks.Any())
                    {
                        var otherNode = new AssetNode { Icon = "👤", Label = "Other" };

                        foreach (var part in otherLooks)
                            otherNode.Children.Add(BuildRolePartNode(part));

                        subTypeNode.Children.Add(otherNode);
                    }
                }
                else if (typeGroup.Key == RolePartType.Mount)
                {
                    var levels = subGroup.GroupBy(p => p.Level).OrderBy(g => g.Key);
                    foreach (var levelGroup in levels)
                    {
                        var levelNode = new AssetNode { Icon = "🎚️", Label = $"Level {levelGroup.Key} ({levelGroup.Count()})" };
                        foreach (var part in levelGroup)
                            levelNode.Children.Add(BuildRolePartNode(part));

                        subTypeNode.Children.Add(levelNode);
                    }
                }
                else
                {
                    // Default: Flat list for Weapons, Capes, etc.
                    foreach (var part in subGroup)
                        subTypeNode.Children.Add(BuildRolePartNode(part));
                }

                typeNode.Children.Add(subTypeNode);
            }

            root.Children.Add(typeNode);
        }

        return root;
    }
    private AssetNode BuildRolePartNode(RolePart part)
    {
        var (meshPaths, texturePaths, rolepartsids, asb, adb) = BuildMeshArraysForRolePart(part);
        var motions = new List<MotionData>();

        // Reattach motions based on part type
        if (part.PartType == RolePartType.Armor)
        {
            // Armor (and by extension Armet/Cape/Head/Misc/Pelvis/Spirit that share 3dmotion.ini)
            // use the look-based key: look * 1_000_000 + actionType
            TryAddMotion(motions, "StandBy", (ulong)(part.Look * 10_000_000L) + (int)RoleActionType.StandBy);
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddMotion(motions, action.ToString(), (ulong)(part.Look * 10_000_000L + (int)action));
            }
        }
        else if (part.PartType == RolePartType.Armet)
        {
            // armetmotion.ini — key: partId * 1000 + actionType
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddArmetMotion(motions, action.ToString(), part.Id, i);
            }
        }
        else if (part.PartType == RolePartType.Cape)
        {
            // capemotion.ini — key: partId * 1000 + actionType
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddCapeMotion(motions, action.ToString(), part.Id, i);
            }
        }
        else if (part.PartType == RolePartType.Head)
        {
            // headmotion.ini — key: partId * 1000 + actionType
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddHeadMotion(motions, action.ToString(), part.Id, i);
            }
        }
        else if (part.PartType == RolePartType.Weapon)
        {
            // WeaponMotion.ini — uses ResolveWeaponMotion (category + wildcard fallback)
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddWeaponMotion(motions, action.ToString(), part.Id, i);
            }
        }
        else if (part.PartType == RolePartType.Misc)
        {
            // miscmotion.ini — key: partId * 1000 + actionType
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddMiscMotion(motions, action.ToString(), part.Id, i);
            }
        }
        else if (part.PartType == RolePartType.Mount)
        {
            // MountMotion.ini — uses ResolveMountMotion (category + wildcard fallback)
            TryAddMountMotion(motions, "StandBy", (ulong)part.SubType * 10_000, (int)RoleActionType.StandBy);
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddMountMotion(motions, action.ToString(), (ulong)part.SubType * 10_000, i);
            }
        }
        else if (part.PartType == RolePartType.Pelvis)
        {
            // pelvismotion.ini — key: partId * 1000 + actionType
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddPelvisMotion(motions, action.ToString(), part.Id, i);
            }
        }
        else if (part.PartType == RolePartType.Spirit)
        {
            for (int i = 0; i < 999; i++)
            {
                RoleActionType action = (RoleActionType)i;
                TryAddSpiritMotion(motions, action.ToString(), part.Id, i);
                TryAddSpiritMotion(motions, "[1]" + action.ToString(), (ulong)(100_000_000 + part.Id), i);
            }
        }

        var node = new AssetNode
        {
            Icon = GetIconForPartType(part.PartType),
            Label = $"[{part.Id}]",
            AssetData = new AssetData
            {
                MeshPaths = meshPaths,
                TexturePaths = texturePaths,
                RolePartsIds = rolepartsids,
                Motions = motions.ToArray(),
                Asb = asb,
                Adb = adb,
            }
        };

        // Attach sub-parts if the item is multi-part
        if (part.Parts > 1)
        {
            for (int i = 0; i < part.Parts; i++)
            {
                if (string.IsNullOrEmpty(meshPaths[i]) || meshPaths[i].StartsWith('?'))
                    continue;

                node.Children.Add(new AssetNode
                {
                    Icon = "▫",
                    Label = Path.GetFileNameWithoutExtension(meshPaths[i]),
                    AssetData = new AssetData
                    {
                        MeshPaths = [meshPaths[i]],
                        TexturePaths = [texturePaths[i]],
                        RolePartsIds = [rolepartsids[i]],
                        Motions = motions.ToArray(),
                        Asb = [asb[i]],
                        Adb = [adb[i]],
                    }
                });
            }
        }

        return node;
    }
    private static string GetIconForPartType(RolePartType type) => type switch
    {
        RolePartType.Armor => "🛡",
        RolePartType.Armet => "⛑",
        RolePartType.Weapon => "⚔",
        RolePartType.Mount => "🐴",
        RolePartType.Cape => "🧣",
        RolePartType.Head => "👤",
        _ => "📦"
    };
    private static string GetSubTypeLabel(RolePartType type, int subType)
    {
        if (type == RolePartType.Weapon)
        {
            return WeaponSubTypes.TryGetValue(subType, out var name)
                ? $"{name} ({subType})"
                : $"Subtype {subType}";
        }

        return $"Subtype {subType}";
    }
    
    // Define your subtype lookup
    private static readonly Dictionary<int, string> WeaponSubTypes = new Dictionary<int, string>
{
    {000, "Boxing"},
    {410, "Blade"},
    {420, "Sword"},
    {421, "Backsword"},
    {422, "Other"},
    {430, "Hook"},
    {440, "Whip"},
    {441, "Mace"},
    {450, "Axe"},
    {460, "Hammer"},
    {470, "Crutch"},
    {480, "Club"},
    {481, "Scepter"},
    {490, "Dagger"},
    {491, "Prod"},
    {492, "Fan"},
    {493, "Flute"},
    {500, "Bow"},
    {510, "Glaive"},
    {511, "Scythe"},
    {520, "Epee"},
    {521, "Zither"},
    {522, "Lute"},
    {530, "Poleaxe"},
    {540, "LongHammer"},
    {550, "Scythe"}, // duplicate preserved
    {560, "Spear"},
    {561, "Wand"},
    {562, "Pickaxe"},
    {570, "Spade"},
    {580, "Halbert"},
    {601, "NinjaSword"},
    {606, "Crossbow"},
    {608, "Alabarda Extrania"},
    {610, "PrayerBeads"},
    {611, "Rapier"},
    {612, "Pistol"},
    {613, "ThrowingKnife"},
    {614, "CrossSaber"},
    {616, "Nobunaga`sClaw"},
    {617, "Nunchaku"},
    {619, "Hossu"},
    {620, "PolarBacksword"},
    {622, "DivinePillar"},
    {624, "Fist"},
    {626, "WindwalkerFan"},
    {670, "HandGun"},
    {671, "MightyRapier"},
    {680, "Flashaxe"},
    {681, "Stormhammer"},
    {700, "Boxing"},
    {710, "Blade"},
    {720, "Sword"},
    {721, "MagicSword"},
    {730, "Hook"},
    {740, "Whip"},
    {741, "Mace"},
    {750, "Axe"},
    {760, "Hammer"},
    {770, "Crutch"},
    {780, "Club"},
    {781, "Scepter"},
    {790, "Dagger"},
    {791, "Prod"},
    {792, "Fan"},
    {793, "Flute"},
    {900, "Shield"}
};

    /// <summary>
    /// Tries to find the mesh(es) for <paramref name="itemId"/> by probing
    /// Armor → Armet → Weapon in order.
    /// Returns empty arrays when no match is found (texture-only entry).
    /// </summary>
    private (string[] Meshes, uint[] RolePartsIds, int[] Asb, int[] Adb) TryFindItemMesh(uint itemId)
    {
        var armor = _gameData.FindRolePart(itemId, RolePartType.Armor);
        if (armor != null)
        {
            var meshes = new string[armor.Parts];
            var rolepartsids = new uint[armor.Parts];
            var asb = new int[armor.Parts];
            var adb = new int[armor.Parts];
            for (int i = 0; i < armor.Parts; i++)
            {
                meshes[i] = _gameData.ResolveMesh(armor.MeshIds[i]) ?? $"? ({armor.MeshIds[i]})";
                rolepartsids[i] = armor.Id;
                asb[i] = armor.Asb[i];
                adb[i] = armor.Adb[i];
            }
            return (meshes, rolepartsids, asb, adb);
        }

        var armet = _gameData.FindRolePart(itemId, RolePartType.Armet);
        if (armet != null)
        {
            var meshes = new string[armet.Parts];
            var rolepartsids = new uint[armet.Parts];
            var asb = new int[armet.Parts];
            var adb = new int[armet.Parts];
            for (int i = 0; i < armet.Parts; i++)
            {
                meshes[i] = _gameData.ResolveMesh(armet.MeshIds[i]) ?? $"? ({armet.MeshIds[i]})";
                rolepartsids[i] = armet.Id;
                asb[i] = armet.Asb[i];
                adb[i] = armet.Adb[i];
            }
            return (meshes, rolepartsids, asb, adb);
        }

        var weapon = _gameData.FindRolePart(itemId, RolePartType.Weapon);
        if (weapon != null)
        {
            var meshes = new string[weapon.Parts];
            var rolepartsids = new uint[weapon.Parts];
            var asb = new int[weapon.Parts];
            var adb = new int[weapon.Parts];
            for (int i = 0; i < weapon.Parts; i++)
            {
                meshes[i] = _gameData.ResolveMesh(weapon.MeshIds[i]) ?? $"? ({weapon.MeshIds[i]})";
                rolepartsids[i] = weapon.Id;
                asb[i] = weapon.Asb[i];
                adb[i] = weapon.Adb[i];
            }
            return (meshes, rolepartsids, asb, adb);
        }

        var obj = _gameData.ResolveMesh(itemId);
        if (!string.IsNullOrEmpty(obj))
        {
            var meshes = new[] { obj };
            var rolepartsids = new[] { itemId };
            var asb = new[] { 5 };
            var adb = new[] { 2 };
            return (meshes, rolepartsids, asb, adb);
        }

        return ([], [], [], []);
    }

    // ── Mesh-array helpers ────────────────────────────────────────────────
    // All helpers return (Paths, Textures, Asb, Adb) so blend state flows
    // from every type's ini data through AssetData into the renderer.

    /// <summary>
    /// Converts parallel effect arrays into an <see cref="EffectDescriptor"/> array,
    /// skipping slots whose mesh path is empty or starts with '?' (unresolvable).
    /// </summary>
    private static EffectDescriptor[] BuildEffectDescriptors(
        string[] meshes, string[] textures, int[] asb, int[] adb)
    {
        var result = new List<EffectDescriptor>(meshes.Length);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (string.IsNullOrEmpty(meshes[i]) || meshes[i].StartsWith('?')) continue;
            result.Add(new EffectDescriptor(
                meshes[i],
                i < textures.Length ? textures[i] : null,
                i < asb.Length ? asb[i] : 5,
                i < adb.Length ? adb[i] : 6));
        }
        return result.ToArray();
    }

    /// <summary>SimpleObj has no Asb/Adb — fills arrays with the supplied defaults (or 5/6).</summary>
    private (string[] Paths, string[] Textures,uint[] Rolepartsids, int[] Asb, int[] Adb) BuildMeshArrays(
        C3DSimpleObjInfo obj, int defaultAsb = 5, int defaultAdb = 6)
    {
        var meshes = new string[obj.Parts];
        var textures = new string[obj.Parts];
        var rolepartsids = new uint[obj.Parts];
        var asb = new int[obj.Parts];
        var adb = new int[obj.Parts];
        for (int i = 0; i < obj.Parts; i++)
        {
            rolepartsids[i] = obj.IdType;
            meshes[i] = _gameData.ResolveMesh(obj.MeshIds[i]) ?? $"? ({obj.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(obj.TextureIds[i]) ?? $"? ({obj.TextureIds[i]})";
            asb[i] = defaultAsb;
            adb[i] = defaultAdb;
        }
        return (meshes, textures, rolepartsids, asb, adb);
    }
    private (string[] Paths, string[] Textures, uint[] Rolepartsids, int[] Asb, int[] Adb) BuildMeshArraysForRolePart(RolePart part)
    {
        var meshes = new string[part.Parts];
        var textures = new string[part.Parts];
        var rolepartsids = new uint[part.Parts];
        var asb = new int[part.Parts];
        var adb = new int[part.Parts];

        for (int i = 0; i < part.Parts; i++)
        {
            rolepartsids[i] = part.Id;
            meshes[i] = (part.PartType == RolePartType.Armet
                ? _gameData.ResolveMesh(part.MeshIds[i]) ?? _gameData.ResolveMesh(1990000000 + part.MeshIds[i])
                : _gameData.ResolveMesh(part.MeshIds[i]))
                ?? $"? ({part.MeshIds[i]})";

            textures[i] = (part.PartType == RolePartType.Armet
                ? _gameData.ResolveTexture(part.TextureIds[i]) ?? _gameData.ResolveTexture(1990000000 + part.TextureIds[i])
                : _gameData.ResolveTexture(part.TextureIds[i])) ?? $"? ({part.TextureIds[i]})";
            asb[i] = part.Asb[i];
            adb[i] = part.Adb[i];
        }

        return (meshes, textures, rolepartsids, asb, adb);
    }

    /// <summary>
    /// Resolves all renderable slots for a named effect key (e.g. "1ghost", "10000"),
    /// including per-slot Asb/Adb from the effect's ini data.
    /// Returns empty arrays when the key is unknown or has no resolvable slots.
    /// </summary>
    private (string[] Meshes, string[] Textures, int[] Asb, int[] Adb) BuildEffectParts(string effectKey)
    {
        if (effectKey.EndsWith(".tme", StringComparison.OrdinalIgnoreCase))
        {
            var tmeEntries = _gameData.ResolveTme(effectKey);

            var meshPaths = new List<string>();
            var texturePaths = new List<string>();
            var asb = new List<int>();
            var adb = new List<int>();

            foreach (var entry in tmeEntries)
            {
                var (m, t, a, d) = BuildEffectParts(entry.EffectKey);

                for (int i = 0; i < m.Length; i++)
                {
                    meshPaths.Add(m[i]);
                    texturePaths.Add(i < t.Length ? t[i] : string.Empty);
                    asb.Add(i < a.Length ? a[i] : 5);
                    adb.Add(i < d.Length ? d[i] : 6);
                }
            }
            return ([.. meshPaths],
                [.. texturePaths],
                [.. asb],
                [.. adb]
            );
        }
        else
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
    private void TryAddMountMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveMountMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddWeaponMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveWeaponMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddCapeMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveCapeMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddMiscMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveMiscMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddArmetMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveArmetMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddSpiritMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveSpiritMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddHeadMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolveHeadMotion(id, action);
        if (path != null)
            entries.Add(new MotionData(label, path));
    }

    private void TryAddPelvisMotion(List<MotionData> entries, string label, ulong id, int action)
    {
        if (id == 0) return;
        var path = _gameData.ResolvePelvisMotion(id, action);
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

        // 1. Role-based loading (multi-slot with PartDescriptors)
        if (data.RoleParts != null && data.RoleParts.Length > 0)
            LoadRoleParts(data);

        // 2. Pure-effect node (no mesh body, only effect slots)
        else if (data.MeshPaths.Length == 0 && data.Effects.Length > 0)
            LoadEffectNode(data);

        // 3. Legacy flat mesh loading fallback (SimpleObjs etc)
        else
            LoadParts(data);

        SelectFirstMotion(data);
    }

    // Add the routing method:
    private void LoadRoleParts(AssetData data)
    {
        ModelPath = data.RoleParts.Length == 1 ? data.RoleParts[0].MeshPath : $"{data.RoleParts.Length} role parts";

        try
        {
            _game!.LoadC3Role(data.RoleParts, motionPath: null); // motion applied separately in SelectFirstMotion

            if (data.Effects.Length > 0)
                _game!.BindEffects(data.Effects);

            StatusMessage = data.RoleParts.Length == 1
                ? $"Loaded Role Part: {Path.GetFileName(data.RoleParts[0].MeshPath)}"
                : $"Loaded Role ({data.RoleParts.Length} slots)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Handles an Effect asset node whose <see cref="AssetData.MeshPaths"/> is empty
    /// but <see cref="AssetData.Effects"/> carries one or more slots.
    /// All slots are forwarded to <see cref="C3StudioGame.LoadStandaloneEffect"/> so
    /// they become a single multi-model <see cref="C3Effect"/> rendered directly by
    /// the renderer — no body part required.
    /// </summary>
    private void LoadEffectNode(AssetData data)
    {
        try
        {
            _game!.LoadStandaloneEffect(data.Effects);

            StatusMessage = data.Effects.Length == 1
                ? $"Effect: {Path.GetFileName(data.Effects[0].MeshPath)}"
                : $"Effect ({data.Effects.Length} parts)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
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

                // Fetch the ID safely, falling back to 0 if bounds are mismatched
                uint rolePartId = i < data.RolePartsIds.Length ? data.RolePartsIds[i] : 0;

                return (MeshPath: mesh, TexturePath: tex, rolePartId, Asb: data.GetAsb(i), Adb: data.GetAdb(i));
            })
            .Where(p => !string.IsNullOrEmpty(p.MeshPath));

        try
        {
            _game!.LoadC3Parts(parts, motionPath: null); // motion applied separately

            // Bind effect models onto the body part's Effects list.
            if (data.Effects.Length > 0)
                _game!.BindEffects(data.Effects);

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
            IEnumerable<(string MeshPath, string? TexturePath,uint rolepartid, int Asb, int Adb)> parts;

            if (_modelPaths.Count == 1)
            {
                var tex = !string.IsNullOrEmpty(TexturePath) ? TexturePath : FindTexture(_modelPaths[0]);
                parts = [(MeshPath: _modelPaths[0], TexturePath: string.IsNullOrEmpty(tex) ? null : tex, (uint)0, Asb, Adb)];
            }
            else
            {
                parts = _modelPaths.Select(mesh =>
                {
                    string tex = FindTexture(mesh);
                    return (MeshPath: mesh, TexturePath: string.IsNullOrEmpty(tex) ? null : tex, (uint)0, Asb, Adb);
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