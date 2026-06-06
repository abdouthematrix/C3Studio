using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.Loading;
using C3Studio.MonoGame;
using SharpDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace C3Studio.ViewModels;

public class RoleViewerViewModel : ViewModelBase
{
    private readonly IGameDataService _gameData;
    private readonly INavigationService _nav;
    private C3StudioGame? _game;

    // ── Slot → socket phy name map ────────────────────────────────────────
    private static readonly IReadOnlyDictionary<string, string> SlotSocketName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Armor",   "" },
            { "Armet",   "v_armet" },
            { "RWeapon", "v_r_weapon" },
            { "LWeapon", "v_l_weapon" },           
            { "Mantle",  "v_mantle" },
            { "Cape",    "v_back" },
            { "Misc",    "v_misc" },
            { "Pelvis",  "v_pelvis" },
            { "Spirit",  "v_rootloc" },
        };

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand GoToWorkspaceCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StepFwdCommand { get; }
    public ICommand StepBackCommand { get; }

    // ── Base Look Selection ───────────────────────────────────────────────
    public IReadOnlyList<KeyValuePair<int, string>> Looks { get; } = Enum.GetValues<RoleLook>()
    .Where(l => l != RoleLook.Other)
    .Select(l => new KeyValuePair<int, string>((int)l, $"{l.ToDisplayString()} ({(int)l})"))
    .ToList();

    private int _selectedLook = 1;
    public int SelectedLook
    {
        get => _selectedLook;
        set { if (Set(ref _selectedLook, value)) LoadBaseBody(); }
    }

    // ── Collections ───────────────────────────────────────────────────────
    public ObservableCollection<SocketBindingViewModel> AvailableSockets { get; } = new();
    public ObservableCollection<RoleActionType> AvailableMotions { get; } = new();

    private RoleActionType _selectedMotion;
    public RoleActionType SelectedMotion
    {
        get => _selectedMotion;
        set
        {
            if (Set(ref _selectedMotion, value))
                ChangeMotion(_selectedMotion);
        }
    }

    private string _playPauseLabel = "⏸";
    public string PlayPauseLabel
    {
        get => _playPauseLabel;
        private set => Set(ref _playPauseLabel, value);
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public RoleViewerViewModel(IGameDataService gameData, INavigationService nav)
    {
        _gameData = gameData;
        _nav = nav;

        GoToWorkspaceCommand = Cmd(() => _nav.GoToWorkspace());
        PlayPauseCommand = Cmd(() => {
            if (_game != null)
            {
                _game.IsPlaying = !_game.IsPlaying;
                PlayPauseLabel = _game.IsPlaying ? "⏸" : "▶";
            }
        });
        StepFwdCommand = Cmd(() => _game?.StepFrame(1));
        StepBackCommand = Cmd(() => _game?.StepFrame(-1));

        InitializeSlots();
    }

    // ── Engine Initialization ─────────────────────────────────────────────
    public void SetGame(C3StudioGame game)
    {
        _game = game;
    }

    // ── UI Slot Setup ─────────────────────────────────────────────────────
    private static readonly (string SlotName, RolePartType[] ValidTypes, bool AllLooks)[] SlotDefinitions =
    [
        ("Armor",   new[] { RolePartType.Armor },                      false),
        ("Armet",   new[] { RolePartType.Armet, RolePartType.Head },   false),
        ("RWeapon", new[] { RolePartType.Weapon },                     true),
        ("LWeapon", new[] { RolePartType.Weapon },                     true),
        ("Mount",   new[] { RolePartType.Mount },                      true),
        ("Mantle",  new[] { RolePartType.Cape },                       false),
        ("Cape",    new[] { RolePartType.Cape },                       false),
        ("Misc",    new[] { RolePartType.Misc },                       false),
        ("Pelvis",  new[] { RolePartType.Pelvis },                     false),
        ("Spirit",  new[] { RolePartType.Spirit },                     false),
    ];

    private void InitializeSlots()
    {
        AvailableSockets.Clear();

        foreach (var (slotName, _, __) in SlotDefinitions)
        {
            SlotSocketName.TryGetValue(slotName, out var phyName);
            var vm = new SocketBindingViewModel(slotName, phyName ?? "", this);

            var noneOption = new RolePartOption(0, "-- None --", 0, "Normal");
            vm.AvailableParts.Add(noneOption);
            vm.PartTree.Add(new PartTreeNode { Name = "-- None --", Part = noneOption, SelectCommand = vm.SelectNodeCommand });

            vm.SelectedPart = vm.AvailableParts[0];
            vm.IsAvailable = string.IsNullOrEmpty(phyName);
            AvailableSockets.Add(vm);
        }
    }

    private void RefreshSlotParts(int look)
    {
        foreach (var (slotName, validTypes, allLooks) in SlotDefinitions)
        {
            var vm = AvailableSockets.FirstOrDefault(
                s => string.Equals(s.SocketName, slotName, StringComparison.OrdinalIgnoreCase));
            if (vm == null) continue;

            var previousId = vm.SelectedPart?.Id ?? 0;

            vm.AvailableParts.Clear();
            vm.PartTree.Clear();

            var noneOption = new RolePartOption(0, "-- None --", 0, "Normal");
            vm.AvailableParts.Add(noneOption);
            vm.PartTree.Add(new PartTreeNode { Name = "-- None --", Part = noneOption, SelectCommand = vm.SelectNodeCommand });

            var groupedParts = _gameData.RoleParts
                .Where(p => validTypes.Contains(p.PartType) && (allLooks || p.Look == look))
                .GroupBy(p => p.SubType)
                .OrderBy(g => g.Key);

            foreach (var group in groupedParts)
            {
                var folderNode = new PartTreeNode { Name = $"SubType {group.Key}" };

                foreach (var p in group.OrderBy(p => p.Id))
                {
                    string quality = "Normal";

                    // Parse weapon quality based on the last digit of the ID
                    if (p.PartType == RolePartType.Weapon)
                    {
                        uint lastDigit = p.Id % 10;
                        quality = lastDigit switch
                        {
                            9 => "Super",
                            8 => "Elite",
                            7 => "Unique",
                            6 => "Refined",
                            _ => "Normal"
                        };
                    }

                    string label = p.PartType == RolePartType.Weapon
                        ? $"[{p.Id}]" + (quality != "Normal" ? $" - {quality}" : "")
                        : $"[{p.Id}]";

                    var opt = new RolePartOption(p.Id, label, p.SubType, quality);
                    vm.AvailableParts.Add(opt);

                    folderNode.Children.Add(new PartTreeNode
                    {
                        Name = opt.DisplayName,
                        Part = opt,
                        SelectCommand = vm.SelectNodeCommand
                    });
                }

                vm.PartTree.Add(folderNode);
            }

            var restore = previousId != 0
                ? vm.AvailableParts.FirstOrDefault(o => o.Id == previousId)
                : null;
            vm.SilentClear();
            if (restore != null) vm.SilentSelect(restore);
        }
    }

    // ── Body & Animation Loading ──────────────────────────────────────────
    public void LoadBaseBody()
    {
        if (_game == null) return;
        AvailableMotions.Clear();

        uint lookId = (uint)(SelectedLook * 1_000_000);

        var armor = _gameData.FindRolePart(lookId, RolePartType.Armor);
        if (armor == null) return;

        var descriptors = BuildPartDescriptors(armor, "Armor");
        if (descriptors.Count == 0) return;

        _game.LoadC3Role(descriptors, motionPath: null);

        RefreshSlotParts(SelectedLook);
        RefreshSlotAvailability();


        AvailableMotions.Add(RoleActionType.StandBy);
        SelectedMotion = RoleActionType.StandBy;

        var basicActions = new[]
{   
    RoleActionType.Rest1,            // 101
    RoleActionType.StandBy_Injured,  // 105
    RoleActionType.WalkL,            // 110
    RoleActionType.WalkR,            // 111
    RoleActionType.Action113,    // 113 (not defined, closest is WalkR_Injured=116, so this one is unmapped)
    RoleActionType.RunL,             // 120
    RoleActionType.RunR,             // 121
    RoleActionType.Jump,             // 130
    RoleActionType.JumpBack,         // 131
    RoleActionType.JumpRun,          // 132
    RoleActionType.JumpAtk,          // 140
    RoleActionType.Sit,              // 250
    RoleActionType.DodgePhysicalAttack, // 310
    RoleActionType.HitPhysicalAttack,   // 320
    RoleActionType.Bruise,              // 321
    RoleActionType.Die,                 // 330
    RoleActionType.DeadBody,            // 331
    RoleActionType.Die1,                // 332
    RoleActionType.DeadBody1,           // 333
    RoleActionType.Die2,                // 334
    RoleActionType.DeadBody2,           // 335
    RoleActionType.Die3,                // 336
    RoleActionType.DeadBody3,           // 337
    RoleActionType.PhysicalAttack_401,  // 401
    RoleActionType.PhysicalAttack_402,  // 402
    RoleActionType.PhysicalAttack_403,  // 403
    RoleActionType.PhysicalAttack_409,   
    RoleActionType.Action700,
    RoleActionType.Action701,
    RoleActionType.Action702,
    RoleActionType.Action703,
    RoleActionType.Action704,

    RoleActionType.Action711,
    RoleActionType.Action712,
    RoleActionType.Action713,

    RoleActionType.Action918,
    RoleActionType.Action919,
    RoleActionType.Action920,
    RoleActionType.Action921,
    RoleActionType.Action922,
    RoleActionType.Action923,
    RoleActionType.Action924,
    RoleActionType.PhysicalAttack_401   // 931 (closest mapping to 401 group)
};

        foreach (var action in basicActions)        
            AvailableMotions.Add(action);
        //for (int i = 0; i < 999; i++)
        //    if (i != (int)RoleActionType.StandBy)
        //        AvailableMotions.Add((RoleActionType)i); 
           
    }

    private void RefreshSlotAvailability()
    {
        if (_game == null) return;

        var bodyPhys = new HashSet<string>(
            _game.GetBodyPhyNames(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var socket in AvailableSockets)
        {
            socket.IsAvailable = string.IsNullOrEmpty(socket.SocketPhyName)
                || bodyPhys.Contains(socket.SocketPhyName);
        }
    }

    // ── Attachment Handling ───────────────────────────────────────────────
    public void ApplyPartToSlot(string slotName, uint itemId)
    {
        if (_game == null) return;

        if (itemId == 0)
        {
            ClearSlot(slotName);
            return;
        }

        var descriptors = TryResolveItem(itemId, slotName);
        if (descriptors.Count > 0)
        {
            _game.AttachToRole(slotName, descriptors);
            if (slotName == "Mount")
            {
                SelectedMotion = RoleActionType.StandBy;
                ChangeMotion(SelectedMotion);
            }
            ChangeMotion(SelectedMotion);
        }
    }

    public void ApplyManualPart(string slotName, string meshPath, string? texturePath)
    {
        if (_game == null || string.IsNullOrWhiteSpace(meshPath)) return;

        var descriptors = new List<PartDescriptor>
        {
            new PartDescriptor(slotName, meshPath.Trim(),
                               string.IsNullOrWhiteSpace(texturePath) ? null : texturePath.Trim(),
                               5, 6)
        };

        _game.AttachToRole(slotName, descriptors);

        if (slotName == "Mount")
        {
            SelectedMotion = RoleActionType.StandBy;
            ChangeMotion(SelectedMotion);
        }
        if (slotName == "Armor")
            ChangeMotion(SelectedMotion);
    }

    public void ClearSlot(string slotName)
    {
        if (_game == null) return;

        if (slotName == "Armor")
        {
            uint nakedLookId = (uint)(SelectedLook * 1_000_000);
            var descriptors = TryResolveItem(nakedLookId, "Armor");

            if (descriptors.Count > 0)            
                _game.AttachToRole("Armor", descriptors);
        }
        else
            _game.DetachFromRole(slotName);
        
        ChangeMotion(SelectedMotion);
    }

    public void ChangeMotion(RoleActionType action)
    {
        if (_game == null) return;
        var actionkey = _game.SetAction(action);
        var path = _gameData.ResolveMotion((ulong)actionkey);
        if (path != null)
            _game?.ChangeMotion(path);
    }

    // ── Resolution Helpers ────────────────────────────────────────────────
    private List<PartDescriptor> TryResolveItem(uint itemId, string slotName)
    {
        var part = _gameData.FindRolePart(itemId, RolePartType.Weapon) ??
                   _gameData.FindRolePart(itemId, RolePartType.Armet) ??
                   _gameData.FindRolePart(itemId, RolePartType.Armor) ??
                   _gameData.FindRolePart(itemId, RolePartType.Mount) ??
                   _gameData.FindRolePart(itemId, RolePartType.Cape) ??
                   _gameData.FindRolePart(itemId, RolePartType.Misc) ??
                   _gameData.FindRolePart(itemId, RolePartType.Pelvis) ??
                   _gameData.FindRolePart(itemId, RolePartType.Spirit) ??
                   _gameData.FindRolePart(itemId, RolePartType.Head);

        if (part != null) return BuildPartDescriptors(part, slotName);

        var mesh = _gameData.ResolveMesh(itemId);
        if (!string.IsNullOrEmpty(mesh))
            return new List<PartDescriptor> { new PartDescriptor(slotName, mesh, null, 5, 2) };

        return new List<PartDescriptor>();
    }

    private List<PartDescriptor> BuildPartDescriptors(RolePart part, string slotName)
    {
        var result = new List<PartDescriptor>();
        for (int i = 0; i < part.Parts; i++)
        {
            var m = (part.PartType == RolePartType.Armet
                ? _gameData.ResolveMesh(part.MeshIds[i]) ?? _gameData.ResolveMesh(1990000000 + part.MeshIds[i])
                : _gameData.ResolveMesh(part.MeshIds[i]));

            if (string.IsNullOrEmpty(m)) continue;

            var t = (part.PartType == RolePartType.Armet
                ? _gameData.ResolveTexture(part.TextureIds[i]) ?? _gameData.ResolveTexture(1990000000 + part.TextureIds[i])
                : _gameData.ResolveTexture(part.TextureIds[i]));

            result.Add(new PartDescriptor(slotName, m, t, part.Id, part.Asb[i], part.Adb[i]));
        }
        return result;
    }
}

// ── Supporting UI Classes ─────────────────────────────────────────────────

public record RolePartOption(uint Id, string DisplayName, int SubType, string Quality);

public class PartTreeNode
{
    public string Name { get; init; } = string.Empty;
    public RolePartOption? Part { get; init; }
    public ObservableCollection<PartTreeNode> Children { get; } = new();
    public ICommand? SelectCommand { get; init; }

    // Helpers for XAML styling
    public bool IsLeaf => Part != null;
}

public class SocketBindingViewModel : ViewModelBase
{
    private readonly RoleViewerViewModel _parent;
    private bool _isSilentlyClearing;

    public string SocketName { get; }
    public string SocketPhyName { get; }

    // ── Availability ──────────────────────────────────────────────────────
    private bool _isAvailable = true;
    public bool IsAvailable
    {
        get => _isAvailable;
        set => Set(ref _isAvailable, value);
    }

    // ── Tree & Dropdown State ─────────────────────────────────────────────
    public ObservableCollection<RolePartOption> AvailableParts { get; } = new();
    public ObservableCollection<PartTreeNode> PartTree { get; } = new();

    private bool _isDropdownOpen;
    public bool IsDropdownOpen
    {
        get => _isDropdownOpen;
        set => Set(ref _isDropdownOpen, value);
    }

    private RolePartOption? _selectedPart;
    public RolePartOption? SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (Set(ref _selectedPart, value))
            {
                if (_isSilentlyClearing) return;

                if (value != null && value.Id != 0)
                    _parent.ApplyPartToSlot(SocketName, value.Id);
                else
                    _parent.ClearSlot(SocketName);
            }
        }
    }

    // ── Manual override ───────────────────────────────────────────────────
    private bool _isManualExpanded;
    public bool IsManualExpanded
    {
        get => _isManualExpanded;
        set => Set(ref _isManualExpanded, value);
    }

    private string _manualMeshPath = string.Empty;
    public string ManualMeshPath
    {
        get => _manualMeshPath;
        set => Set(ref _manualMeshPath, value);
    }

    private string _manualTexturePath = string.Empty;
    public string ManualTexturePath
    {
        get => _manualTexturePath;
        set => Set(ref _manualTexturePath, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand SelectNodeCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleManualCommand { get; }
    public ICommand ApplyManualCommand { get; }
    public ICommand BrowseMeshCommand { get; }
    public ICommand BrowseTextureCommand { get; }

    public SocketBindingViewModel(string socketName, string socketPhyName, RoleViewerViewModel parent)
    {
        SocketName = socketName;
        SocketPhyName = socketPhyName;
        _parent = parent;

        SelectNodeCommand = Cmd<RolePartOption>(part =>
        {
            SelectedPart = part;
            IsDropdownOpen = false;
        });

        ClearCommand = Cmd(() =>
        {
            SelectedPart = AvailableParts.FirstOrDefault();
        });

        ToggleManualCommand = Cmd(() => IsManualExpanded = !IsManualExpanded);

        ApplyManualCommand = Cmd(() =>
        {
            if (!string.IsNullOrWhiteSpace(ManualMeshPath))
                _parent.ApplyManualPart(SocketName, ManualMeshPath, ManualTexturePath);
        });

        BrowseMeshCommand = Cmd(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select mesh for {SocketName}",
                Filter = "C3 Mesh (*.c3)|*.c3|All files (*.*)|*.*",
                CheckFileExists = true,
            };

            if (!string.IsNullOrWhiteSpace(ManualMeshPath))
            {
                var dir = System.IO.Path.GetDirectoryName(ManualMeshPath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
            }

            if (dlg.ShowDialog() != true) return;

            ManualMeshPath = dlg.FileName;

            var autoTex = FindMatchingTexture(dlg.FileName);
            if (autoTex != null)
                ManualTexturePath = autoTex;
        });

        BrowseTextureCommand = Cmd(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select texture for {SocketName}",
                Filter = "DDS texture (*.dds)|*.dds|TGA texture (*.tga)|*.tga|All files (*.*)|*.*",
                CheckFileExists = true,
            };

            if (!string.IsNullOrWhiteSpace(ManualTexturePath))
            {
                var dir = System.IO.Path.GetDirectoryName(ManualTexturePath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
            }
            else if (!string.IsNullOrWhiteSpace(ManualMeshPath))
            {
                var dir = System.IO.Path.GetDirectoryName(ManualMeshPath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
            }

            if (dlg.ShowDialog() == true)
                ManualTexturePath = dlg.FileName;
        });
    }

    private static string? FindMatchingTexture(string meshPath)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(meshPath);
            var stem = System.IO.Path.GetFileNameWithoutExtension(meshPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem)) return null;

            foreach (var ext in new[] { ".dds", ".tga" })
            {
                var candidate = System.IO.Path.Combine(dir, stem + ext);
                if (System.IO.File.Exists(candidate)) return candidate;
            }

            if (System.IO.Directory.Exists(dir))
            {
                var files = System.IO.Directory.EnumerateFiles(dir)
                    .Where(f =>
                    {
                        var n = System.IO.Path.GetFileNameWithoutExtension(f);
                        var e = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        return string.Equals(n, stem, StringComparison.OrdinalIgnoreCase)
                               && (e == ".dds" || e == ".tga");
                    })
                    .OrderBy(f => System.IO.Path.GetExtension(f).ToLowerInvariant())
                    .ToList();

                if (files.Count > 0) return files[0];
            }
        }
        catch { }

        return null;
    }

    public void SilentClear()
    {
        _isSilentlyClearing = true;
        SelectedPart = AvailableParts.FirstOrDefault();
        _isSilentlyClearing = false;
    }

    public void SilentSelect(RolePartOption option)
    {
        _isSilentlyClearing = true;
        SelectedPart = option;
        _isSilentlyClearing = false;
    }
}