using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.Loading;
using C3Studio.MonoGame;
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

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand GoToWorkspaceCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StepFwdCommand { get; }
    public ICommand StepBackCommand { get; }

    // ── Base Look Selection ───────────────────────────────────────────────
    public IReadOnlyList<KeyValuePair<int, string>> Looks { get; } = new List<KeyValuePair<int, string>>
    {
        new(1, "Small Female (1)"),
        new(2, "Big Female (2)"),
        new(3, "Small Male (3)"),
        new(4, "Big Male (4)"),
        new(7, "Female (7)"),
        new(8, "Male (8)")
    };

    private int _selectedLook = 1;
    public int SelectedLook
    {
        get => _selectedLook;
        set { if (Set(ref _selectedLook, value)) LoadBaseBody(); }
    }

    // ── Collections ───────────────────────────────────────────────────────
    public ObservableCollection<SocketBindingViewModel> AvailableSockets { get; } = new();
    public ObservableCollection<MotionData> AvailableMotions { get; } = new();

    private MotionData? _selectedMotion;
    public MotionData? SelectedMotion
    {
        get => _selectedMotion;
        set
        {
            if (Set(ref _selectedMotion, value) && value != null)
                _game?.ChangeMotion(value.Path);
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
    private void InitializeSlots()
    {
        var slotDefinitions = new[]
        {
            ("Armor",   new[] { RolePartType.Armor }),
            ("Armet",   new[] { RolePartType.Armet, RolePartType.Head }),
            ("RWeapon", new[] { RolePartType.Weapon }),
            ("LWeapon", new[] { RolePartType.Weapon }),
            ("Mount",   new[] { RolePartType.Mount }),
            ("Mantle",  new[] { RolePartType.Cape }),
            ("Cape",    new[] { RolePartType.Cape }),
            ("Misc",    new[] { RolePartType.Misc }),
            ("Pelvis",  new[] { RolePartType.Pelvis }),
            ("Spirit",  new[] { RolePartType.Spirit })
        };

        AvailableSockets.Clear();

        foreach (var (slotName, validTypes) in slotDefinitions)
        {
            var vm = new SocketBindingViewModel(slotName, this);

            // Default Empty option
            vm.AvailableParts.Add(new RolePartOption(0, "-- None --"));

            // Filter the master list for only matching Part Types
            var matchingParts = _gameData.RoleParts
                .Where(p => validTypes.Contains(p.PartType))
                .OrderBy(p => p.Id);

            foreach (var p in matchingParts)
            {
                string label = p.PartType == RolePartType.Weapon
                    ? $"[{p.Id}] SubType {p.SubType}"
                    : $"[{p.Id}]";

                vm.AvailableParts.Add(new RolePartOption(p.Id, label));
            }

            // Start empty
            vm.SelectedPart = vm.AvailableParts[0];

            AvailableSockets.Add(vm);
        }
    }

    // ── Body & Animation Loading ──────────────────────────────────────────
    public void LoadBaseBody()
    {
        if (_game == null) return;
        AvailableMotions.Clear();

        uint lookId = (uint)(SelectedLook * 1_000_000);

        // Find the naked base mesh for the selected Look
        var armor = _gameData.FindRolePart(lookId, RolePartType.Armor);
        if (armor == null) return;

        var descriptors = BuildPartDescriptors(armor, "Armor");
        if (descriptors.Count == 0) return;

        _game.LoadC3Role(descriptors, motionPath: null);

        // Load StandBy motion
        var standById = (ulong)(lookId + 100);
        var path = _gameData.ResolveMotion(standById);
        if (path != null)
        {
            var m = new MotionData("StandBy", path);
            AvailableMotions.Add(m);
            SelectedMotion = m;
        }

        // Load basic action test motions
        var basicActions = new[] { 110, 111, 120, 121, 130, 250, 350 };
        foreach (var action in basicActions)
        {
            var p = _gameData.ResolveMotion((ulong)(lookId + action));
            if (p != null) AvailableMotions.Add(new MotionData($"Action {action}", p));
        }

        // Reset all UI ComboBoxes to "-- None --" when the Look is changed
        foreach (var socket in AvailableSockets)
        {
            socket.SilentClear();
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

            // Re-apply motion immediately if the main body is hot-swapped
            if (slotName == "Armor" && SelectedMotion != null)
            {
                _game.ChangeMotion(SelectedMotion.Path);
            }
        }
    }

    public void ClearSlot(string slotName)
    {
        if (_game == null) return;

        if (slotName == "Armor")
        {
            // Never leave the character invisible; fall back to the naked base mesh
            uint nakedLookId = (uint)(SelectedLook * 1_000_000);
            var descriptors = TryResolveItem(nakedLookId, "Armor");

            if (descriptors.Count > 0)
            {
                _game.AttachToRole("Armor", descriptors);
                if (SelectedMotion != null) _game.ChangeMotion(SelectedMotion.Path);
            }
        }
        else
        {
            _game.DetachFromRole(slotName);
        }
    }

    // ── Resolution Helpers ────────────────────────────────────────────────
    private List<PartDescriptor> TryResolveItem(uint itemId, string slotName)
    {
        // Search across all types for the given ID
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

        // Fallback to simple mesh if it's not in a RolePart table
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

            result.Add(new PartDescriptor(slotName, m, t, part.Asb[i], part.Adb[i]));
        }
        return result;
    }
}

// ── Supporting UI Classes ─────────────────────────────────────────────────

public record RolePartOption(uint Id, string DisplayName);

public class SocketBindingViewModel : ViewModelBase
{
    private readonly RoleViewerViewModel _parent;
    private bool _isSilentlyClearing;

    public string SocketName { get; }
    public ObservableCollection<RolePartOption> AvailableParts { get; } = new();

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

    public ICommand ClearCommand { get; }

    public SocketBindingViewModel(string socketName, RoleViewerViewModel parent)
    {
        SocketName = socketName;
        _parent = parent;

        ClearCommand = Cmd(() =>
        {
            // Setting this to index 0 ("-- None --") automatically triggers the setter
            SelectedPart = AvailableParts.FirstOrDefault();
        });
    }

    /// <summary>
    /// Resets the UI ComboBox to "-- None --" without triggering the detaching logic.
    /// Useful when the Look changes and the base body is completely reloaded.
    /// </summary>
    public void SilentClear()
    {
        _isSilentlyClearing = true;
        SelectedPart = AvailableParts.FirstOrDefault();
        _isSilentlyClearing = false;
    }
}