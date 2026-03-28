using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.Models;
using C3Studio.MonoGame;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
namespace C3Studio.ViewModels;

public class WorkspaceViewModel : ViewModelBase
{
    private readonly IGameDataService _gameData;
    private readonly IAssetFileService _assets;
    private readonly ISettingsService _settings;

    private C3StudioGame? _game;

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

    public string ModelPath
    {
        get => _modelPath;
        set => Set(ref _modelPath, value);
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
        private set
        {
            _isExporting = value;
            OnPropertyChanged();
        }
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
    public ICommand ExportNodeCommand { get; } // assigned in ctor
    private bool CanExport() => SelectedNode?.IsLoadable == true && !IsExporting;

    public WorkspaceViewModel(IGameDataService gameData,
                               IAssetFileService assets,
                               IAssetExportService exportService,
                               ISettingsService settings)
    {
        _gameData = gameData;
        _assets = assets;
        _settings = settings;
        _exportService = exportService;
        BrowseFileCommand = Cmd(BrowseFile);
        LoadModelCommand = Cmd(LoadModel, () => !string.IsNullOrEmpty(ModelPath));
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
    }

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

    /// <summary>Cancels an in-progress export (bind to a Cancel button).</summary>
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
            StatusMessage = $"Ready — {_gameData.Npcs.Count} NPCs, {_gameData.SimpleObjs.Count} objects, {_gameData.Effects.Count} effects.";
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
        RefreshFilter();
    }

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

        var (objMeshes, objTextures) = obj != null
            ? BuildMeshArrays(obj)
            : ([], []);

        var (efxMeshes, efxTextures) = !string.IsNullOrEmpty(npc.Effect)
            ? BuildEffectParts(npc.Effect)
            : ([], []);

        var allMeshes = objMeshes.Concat(efxMeshes).ToArray();
        var allTextures = objTextures.Concat(efxTextures).ToArray();

        if (allMeshes.Length > 0 || motions.Length > 0)
        {
            node.AssetData = new AssetData
            {
                MeshPaths = allMeshes,
                TexturePaths = allTextures,
                Motions = motions
            };
        }

        return node;
    }

    private AssetNode BuildSimpleObjRoot()
    {
        var root = new AssetNode { Icon = "🗿", Label = $"Simple Objects ({_gameData.SimpleObjs.Count})" };
        foreach (var obj in _gameData.SimpleObjs)
            root.Children.Add(BuildSimpleObjNode(obj));
        return root;
    }

    private AssetNode BuildSimpleObjNode(C3DSimpleObjInfo obj)
    {
        var (meshPaths, texturePaths) = BuildMeshArrays(obj);

        var node = new AssetNode
        {
            Icon = "📦",
            Label = $"[{obj.IdType}]",
            AssetData = new AssetData { MeshPaths = meshPaths, TexturePaths = texturePaths }
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
                    TexturePaths = [texturePaths[i]]
                }
            });
        }

        return node;
    }

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

        // Resolve all slots up front
        var meshPaths = new string[effect.Amount];
        var texPaths = new string[effect.Amount];
        for (int i = 0; i < effect.Amount; i++)
        {
            meshPaths[i] = _gameData.ResolveEffectObj(effect.EffectIds[i])
                           ?? $"? ({effect.EffectIds[i]})";
            texPaths[i] = _gameData.ResolveTexture((ulong)effect.TextureIds[i])
                           ?? $"? ({effect.TextureIds[i]})";
        }

        // Parent node loads all slots merged (same pattern as SimpleObj)
        var node = new AssetNode
        {
            Icon = "✨",
            Label = label,
            AssetData = effect.Amount > 0
                ? new AssetData { MeshPaths = meshPaths, TexturePaths = texPaths }
                : null
        };

        // Per-slot children for individual loading
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
                    TexturePaths = [texPaths[i]]
                }
            });
        }

        return node;
    }

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

    /// <summary>
    /// Returns a (possibly shallow-cloned) node if it or any descendant
    /// matches <paramref name="query"/>.  Returns null when nothing matches.
    /// When <paramref name="query"/> is empty every node passes.
    /// </summary>
    private static AssetNode? FilterNode(AssetNode node, string query)
    {
        // Empty query → pass everything through as-is (no allocation)
        if (string.IsNullOrEmpty(query))
            return node;

        bool selfMatch = node.Label.Contains(query, StringComparison.OrdinalIgnoreCase);

        // Leaf node
        if (node.Children.Count == 0)
            return selfMatch ? node : null;

        // Recurse into children
        var matchedChildren = node.Children
            .Select(c => FilterNode(c, query))
            .OfType<AssetNode>()
            .ToList();

        if (!selfMatch && matchedChildren.Count == 0)
            return null;

        // Build a proxy root that holds only the matching subtree
        var proxy = new AssetNode
        {
            Icon = node.Icon,
            Label = node.Label,
            AssetData = node.AssetData
        };

        // If the root label itself matched, show ALL its children unfiltered.
        // If only children matched, show only those.
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

    private void LoadParts(AssetData data)
    {
        if (data.MeshPaths.Length == 0) return;

        // Build display labels
        ModelPath = data.MeshPaths.Length == 1 ? data.MeshPaths[0] : $"{data.MeshPaths.Length} parts";
        TexturePath = data.TexturePaths.Length == 1 ? data.TexturePaths[0] : string.Empty;

        // Build (mesh, texture) pairs for the loader
        var parts = data.MeshPaths
            .Select((mesh, i) =>
            {
                // Manual texture override applies only to single-part loads
                string? tex = i < data.TexturePaths.Length ? data.TexturePaths[i] : null;
                if (data.MeshPaths.Length == 1 && !string.IsNullOrEmpty(TexturePath))
                    tex = TexturePath;
                return (MeshPath: mesh, TexturePath: tex);
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
        var dlg = new OpenFileDialog { Filter = "C3 files (*.c3)|*.c3|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            ModelPath = dlg.FileName;
            TexturePath = FindTexture(ModelPath);
        }
    }
    private static string FindTexture(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return null;
        foreach (var ext in new[] { ".dds", ".tga", ".png", ".jpg" })
        { string p = Path.Combine(dir, baseName + ext); if (File.Exists(p)) return p; }
        return "";
    }

    private void LoadModel()
    {
        if (_game == null || string.IsNullOrEmpty(ModelPath)) return;
        try
        {
            IEnumerable<(string MeshPath, string? TexturePath)> parts = new List<(string, string?)>
            {
                (ModelPath, TexturePath),
            };

            _game!.LoadC3Parts(parts, motionPath: MotionPath);
            //_game.LoadC3Asset(ModelPath,
            //       texturePath: string.IsNullOrEmpty(TexturePath) ? null : TexturePath);
            StatusMessage = $"Loaded: {Path.GetFileName(ModelPath)}";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    private void BrowseTexture()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Texture files (*.dds;*.tga;*.png;*.jpg)|*.dds;*.tga;*.png;*.jpg|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK) TexturePath = dlg.FileName;
    }

    private void BrowseMotion()
    {
        var dlg = new OpenFileDialog { Filter = "C3 motion files (*.c3)|*.c3|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == DialogResult.OK) MotionPath = dlg.FileName;
    }

    private void ApplyMotion() => ApplyMotionSilent(MotionPath);

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves all renderable slots for a named effect key (e.g. "1ghost", "10000").
    /// Returns empty arrays when the key is unknown or has no resolvable slots.
    /// </summary>
    private (string[] Meshes, string[] Textures) BuildEffectParts(string effectKey)
    {
        var effect = _gameData.FindEffect(effectKey);
        if (effect == null || effect.Amount == 0) return ([], []);

        var meshes = new List<string>(effect.Amount);
        var textures = new List<string>(effect.Amount);

        for (int i = 0; i < effect.Amount; i++)
        {
            var mesh = _gameData.ResolveEffectObj(effect.EffectIds[i]);
            if (string.IsNullOrEmpty(mesh)) continue;   // unresolvable slot — skip

            meshes.Add(mesh);
            textures.Add(_gameData.ResolveTexture((ulong)effect.TextureIds[i])
                         ?? $"? ({effect.TextureIds[i]})");
        }

        return (meshes.ToArray(), textures.ToArray());
    }

    private (string[] Paths, string[] Textures) BuildMeshArrays(C3DSimpleObjInfo obj)
    {
        var meshes = new string[obj.Parts];
        var textures = new string[obj.Parts];
        for (int i = 0; i < obj.Parts; i++)
        {
            meshes[i] = _gameData.ResolveMesh(obj.MeshIds[i]) ?? $"? ({obj.MeshIds[i]})";
            textures[i] = _gameData.ResolveTexture(obj.TextureIds[i]) ?? $"? ({obj.TextureIds[i]})";
        }
        return (meshes, textures);
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