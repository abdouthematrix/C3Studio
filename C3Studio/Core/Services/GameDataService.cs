using C3Studio.Core.Models;
using C3Studio.Infrastructure.Ini;
using System.Formats.Tar;
using System.IO;

namespace C3Studio.Core.Services;

public interface IGameDataService
{
    IReadOnlyList<NpcTypeInfo> Npcs { get; }
    IReadOnlyList<C3DSimpleObjInfo> SimpleObjs { get; }
    IReadOnlyList<C3DEffectInfo> Effects { get; }
    IReadOnlyList<RolePart> RoleParts { get; }
    IReadOnlyList<ItemTextureInfo> ItemTextures { get; }

    IReadOnlyList<TransformInfo> Transforms { get; }
    IReadOnlyList<SimpleRoleTypeInfo> SimpleRoles { get; }      // ← NEW
    IReadOnlyDictionary<ulong, string> MeshMap { get; }
    IReadOnlyDictionary<ulong, string> TextureMap { get; }
    IReadOnlyDictionary<ulong, string> MotionMap { get; }
    IReadOnlyDictionary<ulong, string> EffectObjMap { get; }
    IReadOnlyDictionary<ulong, string> WeaponMotionMap { get; }
    IReadOnlyDictionary<ulong, string> MountMotionMap { get; }

    Task LoadAsync(string conquerPath);

    string? ResolveMesh(ulong id);
    string? ResolveTexture(ulong id);
    string? ResolveMotion(ulong motionId);
    string? ResolveEffectObj(uint id);

    /// <summary>
    /// Replicates <c>C3DRole::GetWeaponMotion</c>: four-pass fallback lookup.
    /// </summary>
    string? ResolveWeaponMotion(ulong weaponId, int actionType);
    string? ResolveMountMotion(ulong weaponId, int actionType);

    C3DSimpleObjInfo? FindSimpleObj(uint typeId);
    C3DEffectInfo? FindEffect(uint id);
    C3DEffectInfo? FindEffect(string key);    
    TransformInfo? FindTransform(int index);
    SimpleRoleTypeInfo? FindSimpleRole(int index);              // ← NEW
    ItemTextureInfo? FindItemTexture(uint id);
    uint ResolveItemTexture(uint itemId, ItemColor color);
    uint ResolveItemTexture(uint itemId, byte colorValue);
    IReadOnlyDictionary<int, MagicSkillGroup> MagicSkills { get; }
    MagicSkillGroup? FindMagicSkill(int baseId);
    TmeEntry[] ResolveTme(string tmeKey);
    RolePart? FindRolePart(uint id, RolePartType type);
}

public class GameDataService : IGameDataService
{
    private List<NpcTypeInfo> _npcs = new();
    private List<C3DSimpleObjInfo> _simpleObjs = new();
    private List<C3DEffectInfo> _effects = new();
    private List<TransformInfo> _transforms = new();
    private List<ItemTextureInfo> _itemTextures = new();

    private List<SimpleRoleTypeInfo> _simpleRoles = new();      // ← NEW
    private Dictionary<ulong, string> _mesh = new();
    private Dictionary<ulong, string> _tex = new();
    private Dictionary<ulong, string> _motion = new();
    private Dictionary<ulong, string> _effectObj = new();
    private Dictionary<ulong, string> _weaponMotion = new();
    private Dictionary<ulong, string> _mountMotion = new();

    public IReadOnlyList<NpcTypeInfo> Npcs => _npcs;
    public IReadOnlyList<C3DSimpleObjInfo> SimpleObjs => _simpleObjs;
    public IReadOnlyList<C3DEffectInfo> Effects => _effects;
    public IReadOnlyList<TransformInfo> Transforms => _transforms;
    public IReadOnlyList<ItemTextureInfo> ItemTextures => _itemTextures;

    public IReadOnlyList<SimpleRoleTypeInfo> SimpleRoles => _simpleRoles;   // ← NEW

    public IReadOnlyDictionary<ulong, string> MeshMap => _mesh;
    public IReadOnlyDictionary<ulong, string> TextureMap => _tex;
    public IReadOnlyDictionary<ulong, string> MotionMap => _motion;
    public IReadOnlyDictionary<ulong, string> EffectObjMap => _effectObj;
    public IReadOnlyDictionary<ulong, string> WeaponMotionMap => _weaponMotion;
    public IReadOnlyDictionary<ulong, string> MountMotionMap => _mountMotion;

    private Dictionary<int, MagicSkillGroup> _magicSkills = new();
    public IReadOnlyDictionary<int, MagicSkillGroup> MagicSkills => _magicSkills;

    private string _loadedPath = string.Empty;
    private string _iniPath = string.Empty;

    // Cache: tme key (lowercase, no extension) → parsed entries
    private readonly Dictionary<string, TmeEntry[]> _tmeCache = new(
        StringComparer.OrdinalIgnoreCase);

    // Candidate directories, searched in order
    private static readonly string[] s_tmeDirs =
        ["TerrainMagic", "tme"];

    private List<RolePart> _roleParts = new();
    public IReadOnlyList<RolePart> RoleParts => _roleParts;

    private static Dictionary<ulong, string> Cast(Dictionary<ulong, string> d) => d;

    public Task LoadAsync(string conquerPath) => Task.Run(() =>
    {
        if (_loadedPath == conquerPath) return;
        _iniPath = Path.Combine(conquerPath, "ini");
        _tmeCache.Clear();

        string Ini(string f) => Path.Combine(_iniPath, f);

        // Core items
        _npcs = NpcIniParser.Parse(Ini("npc.ini"));
        _simpleObjs = SimpleObjIniParser.Parse(Ini("3DSimpleObj.ini"));
        _effects = EffectIniParser.Parse(Ini("3DEffect.ini"));

        // Unified Role Parts Parsing Loop
        _roleParts.Clear();
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Armor.ini"), RolePartType.Armor));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Armet.ini"), RolePartType.Armet));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Weapon.ini"), RolePartType.Weapon));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Mount.ini"), RolePartType.Mount));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Cape.ini"), RolePartType.Cape));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Head.ini"), RolePartType.Head));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Misc.ini"), RolePartType.Misc));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Pelvis.ini"), RolePartType.Pelvis));
        _roleParts.AddRange(RolePartIniParser.Parse(Ini("Spirit.ini"), RolePartType.Spirit));

        // Transforms and other entries...
        var transformById = new Dictionary<int, TransformInfo>();
        foreach (var t in AdditiveIniParser.Parse(Ini("AdditiveSize.ini"))) transformById[t.Index] = t;
        foreach (var t in TransFormIniParser.Parse(Ini("TransForm.ini"))) transformById[t.Index] = t;
        _transforms = transformById.Values.ToList();

        _simpleRoles = SimpleRoleIniParser.Parse(Ini("3DSimpleRole.ini"));
        _itemTextures = ItemTextureIniParser.Parse(Ini("ItemTexture.ini"));

        _mesh = ResIniParser.Parse(Ini("3dobj.ini"));
        _tex = ResIniParser.Parse(Ini("3dtexture.ini"));
        _motion = ResIniParser.Parse(Ini("3dmotion.ini"));
        _effectObj = ResIniParser.Parse(Ini("3DEffectObj.ini"));
        _weaponMotion = ResIniParser.Parse(Ini("WeaponMotion.ini"));
        _mountMotion = ResIniParser.Parse(Ini("MountMotion.ini"));

        _magicSkills = MagicEffectIniParser.Parse(Ini("MagicEffect.ini"));
        _loadedPath = conquerPath;
    });

    // ── Basic resolvers (go through the public property, not the backing field) ──
    public string? ResolveMesh(ulong id) => MeshMap.GetValueOrDefault(id);
    public string? ResolveTexture(ulong id) => TextureMap.GetValueOrDefault(id);
    public string? ResolveEffectObj(uint id) => EffectObjMap.GetValueOrDefault(id);

    /// <summary>
    /// Two-pass motion resolver for <c>3dmotion.ini</c>.
    /// Pass 1: direct lookup (e.g. 999001100 → found directly).
    /// Pass 2: strip-at-6 for 10-digit IDs: 9990010100 → remove index-6 '0' → 999001100.
    /// </summary>
    public string? ResolveMotion(ulong motionId)
    {
        if (MotionMap.TryGetValue(motionId, out var direct)) return direct;

        var s = motionId.ToString();
        if (s.Length == 10)
        {
            var stripped = s[..6] + s[7..];
            if (ulong.TryParse(stripped, out var key2)
                && MotionMap.TryGetValue(key2, out var path2))
                return path2;
        }
        if (s.Length >= 7)
        {
            var stripped = s.Insert(4, "0");
            if (ulong.TryParse(stripped, out var key2)
                && MotionMap.TryGetValue(key2, out var path2))
                return path2;
        }


        return null;
    }

    /// <summary>
    /// Four-pass weapon-motion resolver, replicating <c>C3DRole::GetWeaponMotion</c>.
    ///
    /// The C++ logic (simplified):
    /// <code>
    ///   weaponId = (weaponId / 10) * 10;                          // snap to 10-boundary
    ///   try weaponId * 1000 + actionType
    ///   try weaponId * 1000 + 999                                 // generic action fallback
    ///   try ((weaponId / 1000) * 1000 + 999) * 1000 + actionType // category fallback
    ///   try ((weaponId / 1000) * 1000 + 999) * 1000 + 999        // category + generic
    /// </code>
    /// </summary>
    public string? ResolveWeaponMotion(ulong weaponId, int actionType)
    {
        weaponId = (weaponId / 10) * 10;

        // Pass 1 — exact weapon + exact action
        ulong key = weaponId * 1000 + (ulong)actionType;
        if (WeaponMotionMap.TryGetValue(key, out var p1)) return p1;

        // Pass 2 — exact weapon + generic action (999)
        key = weaponId * 1000 + 999;
        if (WeaponMotionMap.TryGetValue(key, out var p2)) return p2;

        // Pass 3 — category weapon (e.g. 410000 → 410999) + exact action
        ulong categoryId = (weaponId / 1000) * 1000 + 999;
        key = categoryId * 1000 + (ulong)actionType;
        if (WeaponMotionMap.TryGetValue(key, out var p3)) return p3;

        // Pass 4 — category weapon + generic action
        key = categoryId * 1000 + 999;
        if (WeaponMotionMap.TryGetValue(key, out var p4)) return p4;

        return null;
    }
    public string? ResolveMountMotion(ulong MountId, int actionType)
    {
        MountId = (MountId / 10) * 10;

        // Pass 1 — exact Mount + exact action
        ulong key = MountId * 1000 + (ulong)actionType;
        if (MountMotionMap.TryGetValue(key, out var p1)) return p1;

        // Pass 2 — exact Mount + generic action (999)
        key = MountId * 1000 + 999;
        if (MountMotionMap.TryGetValue(key, out var p2)) return p2;

        // Pass 3 — category Mount (e.g. 410000 → 410999) + exact action
        ulong categoryId = (MountId / 1000) * 1000 + 999;
        key = categoryId * 1000 + (ulong)actionType;
        if (MountMotionMap.TryGetValue(key, out var p3)) return p3;

        // Pass 4 — category Mount + generic action
        key = categoryId * 1000 + 999;
        if (MountMotionMap.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    // ── Finders ───────────────────────────────────────────────────────────
    public C3DSimpleObjInfo? FindSimpleObj(uint typeId)
    {
        foreach (var o in SimpleObjs) if (o.IdType == typeId) return o;
        return null;
    }

    public C3DEffectInfo? FindEffect(uint id)
    {
        foreach (var e in Effects) if (e.Id == id) return e;
        return null;
    }

    /// <summary>Looks up an effect by its raw section key (e.g. "1ghost" or "10000").</summary>
    public C3DEffectInfo? FindEffect(string key)
    {
        foreach (var e in Effects)
            if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    public TransformInfo? FindTransform(int index)
    {
        foreach (var t in Transforms) if (t.Index == index) return t;
        return null;
    }

    public ItemTextureInfo? FindItemTexture(uint id)
    {
        foreach (var it in ItemTextures) if (it.Id == id) return it;
        return null;
    }

    /// <summary>Looks up a simple role by its numeric index (e.g. 0, 100).</summary>
    public SimpleRoleTypeInfo? FindSimpleRole(int index)                    // ← NEW
    {
        foreach (var r in SimpleRoles) if (r.Index == index) return r;
        return null;
    }

    /// <summary>
    /// Returns the texture ID for <paramref name="itemId"/> + <paramref name="color"/>,
    /// or <c>0</c> if no entry is found.
    /// </summary>
    public uint ResolveItemTexture(uint itemId, ItemColor color)
        => FindItemTexture(itemId)?.GetTexture(color) ?? 0;

    /// <inheritdoc cref="ResolveItemTexture(uint,ItemColor)"/>
    public uint ResolveItemTexture(uint itemId, byte colorValue)
        => FindItemTexture(itemId)?.GetTexture(colorValue) ?? 0;

    /// <summary>Returns the skill group whose base ID equals <paramref name="baseId"/>.</summary>
    public MagicSkillGroup? FindMagicSkill(int baseId)
        => _magicSkills.GetValueOrDefault(baseId);

    public TmeEntry[] ResolveTme(string tmeKey)
    {
        // Strip optional .tme extension for the cache key
        string cacheKey = tmeKey.EndsWith(".tme", StringComparison.OrdinalIgnoreCase)
            ? tmeKey[..^4]
            : tmeKey;

        if (_tmeCache.TryGetValue(cacheKey, out var cached))
            return cached;

        string fileName = cacheKey + ".tme";

        foreach (var dir in s_tmeDirs)
        {
            string fullPath = Path.Combine(_iniPath, dir, fileName);

            if (!File.Exists(fullPath))
                continue;

            var entries = TmeParser.Parse(fullPath);
            _tmeCache[cacheKey] = entries;
            return entries;
        }

        _tmeCache[cacheKey] = [];
        return [];
    }

    public RolePart? FindRolePart(uint id, RolePartType type)
    {
        foreach (var p in _roleParts)
        {
            if (p.Id == id && p.PartType == type) return p;
        }
        return null;
    }
}