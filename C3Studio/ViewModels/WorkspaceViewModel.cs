using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.Models;
using C3Studio.MonoGame;
using Microsoft.Win32;

namespace C3Studio.ViewModels;

public class WorkspaceViewModel : ViewModelBase
{
    private readonly IGameDataService  _gameData;
    private readonly IAssetFileService _assets;
    private readonly ISettingsService   _settings;

    private C3StudioGame? _game;

    private string _modelPath     = string.Empty;
    private string _texturePath = string.Empty;
    private string _motionPath = string.Empty;
    private string _statusMessage = "Ready.";
    private bool   _isLoading;
    private string _frameLabel    = "0 / 0";
    private string _playPauseLabel= "⏸";
    private float  _fps           = 30f;
    private AssetNode? _selectedNode;

    public ObservableCollection<AssetNode> AssetTree { get; } = new();

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
            if (value?.IsLoadable == true)
                LoadAssetNode(value);
        }
    }

    public ICommand BrowseFileCommand  { get; }
    public ICommand LoadModelCommand   { get; }
    public ICommand ResetCameraCommand { get; }
    public ICommand PlayPauseCommand   { get; }
    public ICommand StepFwdCommand     { get; }
    public ICommand StepBackCommand    { get; }
    public ICommand SetFpsCommand      { get; }
    public ICommand BrowseTextureCommand { get; }
    public ICommand BrowseMotionCommand { get; }
    public ICommand ApplyMotionCommand { get; }

    public WorkspaceViewModel(IGameDataService gameData,
                              IAssetFileService assets,
                              ISettingsService settings)
    {
        _gameData = gameData;
        _assets   = assets;
        _settings = settings;

        BrowseFileCommand  = Cmd(BrowseFile);
        LoadModelCommand   = Cmd(LoadModel, () => !string.IsNullOrEmpty(ModelPath));
        ResetCameraCommand = Cmd(() => _game?.ResetCamera());
        PlayPauseCommand   = Cmd(TogglePlay);
        StepFwdCommand     = Cmd(() => _game?.StepFrame(1));
        StepBackCommand    = Cmd(() => _game?.StepFrame(-1));
        SetFpsCommand      = Cmd<string>(s => { if (float.TryParse(s, out float v)) Fps = v; });

        BrowseTextureCommand = Cmd(BrowseTexture);
        BrowseMotionCommand = Cmd(BrowseMotion);
        ApplyMotionCommand = Cmd(ApplyMotion, () => !string.IsNullOrEmpty(MotionPath));
    }

    /// <summary>
    /// Called by NavigationService after navigating to the workspace.
    /// Mirrors SetupViewModel.LoadAsync — initialises assets and loads game data,
    /// then builds the asset tree. Safe to call even if SetupViewModel already ran it.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading     = true;
        StatusMessage = "Loading game data…";
        try
        {
            _assets.Initialize(_settings.ConquerPath);
            await _gameData.LoadAsync(_settings.ConquerPath);
            BuildAssetTree();
            StatusMessage = $"Ready — {_gameData.Npcs.Count} NPCs, {_gameData.SimpleObjs.Count} objects.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    // ── Called by WorkspacePage code-behind once the game is ready ────────
    public void SetGame(C3StudioGame game)
    {
        _game = game;
        _game.FrameChanged += OnFrameChanged;
        _game.SetFps(_fps);
    }

    // ── Tree ──────────────────────────────────────────────────────────────
    private void BuildAssetTree()
    {
        AssetTree.Clear();

        // NPCs
        var npcRoot = new AssetNode { Icon = "👤", Label = $"NPCs ({_gameData.Npcs.Count})" };
        foreach (var npc in _gameData.Npcs)
        {
            var node = new AssetNode { Icon = "⚔", Label = $"[{npc.NpcType}] {npc.Name}" };

            // Try to resolve mesh path via SimpleObj → mesh map
            if (_gameData.SimpleObjs.FirstOrDefault(s => s.IdType == npc.SimpleObjId) is { } obj
                && obj.Parts > 0
                && _gameData.MeshMap.TryGetValue(obj.MeshIds[0], out var meshPath))
            {
                node.AssetKey = meshPath;
            }

            // Motion nodes
            TryAddMotion(node, "▶ StandBy",  npc.StandByMotionId);
            TryAddMotion(node, "▶ Blaze",    npc.BlazeMotionId);
            TryAddMotion(node, "▶ Rest",     npc.RestMotionId);

            npcRoot.Children.Add(node);
        }
        AssetTree.Add(npcRoot);

        // Simple Objects
        var objRoot = new AssetNode { Icon = "🗿", Label = $"Simple Objects ({_gameData.SimpleObjs.Count})" };
        foreach (var obj in _gameData.SimpleObjs)
        {
            var node = new AssetNode { Icon = "📦", Label = $"[{obj.IdType}]" };
            for (int i = 0; i < obj.Parts; i++)
            {
                if (_gameData.MeshMap.TryGetValue(obj.MeshIds[i], out var mp))
                {
                    var part = new AssetNode { Icon = "🔷", Label = $"Part {i} — {Path.GetFileName(mp)}", AssetKey = mp };
                    node.Children.Add(part);
                }
            }
            if (node.Children.Count > 0) node.AssetKey = node.Children[0].AssetKey;
            objRoot.Children.Add(node);
        }
        AssetTree.Add(objRoot);
    }

    private void TryAddMotion(AssetNode parent, string label, ulong id)
    {
        if (id == 0) return;
        var path = _gameData.ResolveMotion(id);  
        parent.Children.Add(new AssetNode { Icon = "🎬", Label = $"{label} ({id})", AssetKey = path });
    }

    // ── Loading ───────────────────────────────────────────────────────────
    private void BrowseFile()
    {
        var dlg = new OpenFileDialog { Filter = "C3 files (*.c3)|*.c3|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true) ModelPath = dlg.FileName;
    }

    private void LoadModel()
    {
        if (_game == null || string.IsNullOrEmpty(ModelPath)) return;
        TryLoad(ModelPath);
    }
    private void BrowseTexture()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Texture files (*.dds;*.tga;*.png;*.jpg)|*.dds;*.tga;*.png;*.jpg|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) TexturePath = dlg.FileName;
    }

    private void BrowseMotion()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "C3 motion files (*.c3)|*.c3|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) MotionPath = dlg.FileName;
    }

    private void ApplyMotion()
    {
        if (_game == null || string.IsNullOrEmpty(MotionPath)) return;
        try
        {
            _game.ChangeMotion(MotionPath);
            StatusMessage = $"Motion: {Path.GetFileName(MotionPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Motion error: {ex.Message}";
        }
    }

    private void LoadAssetNode(AssetNode node)
    {
        if (node.AssetKey == null || _game == null) return;
        ModelPath = node.AssetKey;
        TryLoad(ModelPath);
    }

    private void TryLoad(string path)
    {
        try
        {
            _game!.LoadC3Asset(path,
                texturePath: string.IsNullOrEmpty(TexturePath) ? null : TexturePath);
            StatusMessage = $"Loaded: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    // ── Playback ──────────────────────────────────────────────────────────
    private void TogglePlay()
    {
        if (_game == null) return;
        _game.IsPlaying   = !_game.IsPlaying;
        PlayPauseLabel    = _game.IsPlaying ? "⏸" : "▶";
    }

    private void OnFrameChanged(int current, int total)
    {
        FrameLabel = $"{current} / {total}";
    }
}
