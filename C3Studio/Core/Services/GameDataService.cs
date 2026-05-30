using C3Studio.Core.Models;
using C3Studio.Infrastructure.Ini;
using C3Studio.Infrastructure.Wdb;
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
    IReadOnlyList<SimpleRoleTypeInfo> SimpleRoles { get; }

    IReadOnlyDictionary<ulong, string> MeshMap { get; }
    IReadOnlyDictionary<ulong, string> TextureMap { get; }
    IReadOnlyDictionary<ulong, string> MotionMap { get; }
    IReadOnlyDictionary<ulong, string> EffectObjMap { get; }
    IReadOnlyDictionary<ulong, string> WeaponMotionMap { get; }
    IReadOnlyDictionary<ulong, string> MountMotionMap { get; }
    IReadOnlyDictionary<ulong, string> CapeMotionMap { get; }
    IReadOnlyDictionary<ulong, string> MiscMotionMap { get; }
    IReadOnlyDictionary<ulong, string> ArmetMotionMap { get; }
    IReadOnlyDictionary<ulong, string> SpiritMotionMap { get; }
    IReadOnlyDictionary<ulong, string> HeadMotionMap { get; }
    IReadOnlyDictionary<ulong, string> PelvisMotionMap { get; }

    IReadOnlyDictionary<int, MagicSkillGroup> MagicSkills { get; }

    Task LoadAsync(string conquerPath);

    string? ResolveMesh(ulong id);
    string? ResolveTexture(ulong id);
    string? ResolveMotion(ulong motionId);
    string? ResolveEffectObj(uint id);
    string? ResolveWeaponMotion(ulong weaponId, int actionType);
    string? ResolveMountMotion(ulong mountId, int actionType);
    string? ResolveCapeMotion(ulong capeId, int actionType);
    string? ResolveMiscMotion(ulong miscId, int actionType);
    string? ResolveArmetMotion(ulong armetId, int actionType);
    string? ResolveSpiritMotion(ulong spiritId, int actionType);
    string? ResolveHeadMotion(ulong headId, int actionType);
    string? ResolvePelvisMotion(ulong pelvisId, int actionType);

    C3DSimpleObjInfo? FindSimpleObj(uint typeId);
    C3DEffectInfo? FindEffect(uint id);
    C3DEffectInfo? FindEffect(string key);
    TransformInfo? FindTransform(int index);
    SimpleRoleTypeInfo? FindSimpleRole(int index);
    ItemTextureInfo? FindItemTexture(uint id);

    uint ResolveItemTexture(uint itemId, ItemColor color);
    uint ResolveItemTexture(uint itemId, byte colorValue);

    MagicSkillGroup? FindMagicSkill(int baseId);
    TmeEntry[] ResolveTme(string tmeKey);
    RolePart? FindRolePart(uint id, RolePartType type);
}

// ─────────────────────────────────────────────────────────────────────────────

public class GameDataService : IGameDataService
{
    // ── Backing stores ────────────────────────────────────────────────────────

    private List<NpcTypeInfo> _npcs = new();
    private List<C3DSimpleObjInfo> _simpleObjs = new();
    private List<C3DEffectInfo> _effects = new();
    private List<TransformInfo> _transforms = new();
    private List<ItemTextureInfo> _itemTextures = new();
    private List<SimpleRoleTypeInfo> _simpleRoles = new();
    private List<RolePart> _roleParts = new();

    private Dictionary<ulong, string> _mesh = new();
    private Dictionary<ulong, string> _tex = new();
    private Dictionary<ulong, string> _motion = new();
    private Dictionary<ulong, string> _effectObj = new();
    private Dictionary<ulong, string> _weaponMotion = new();
    private Dictionary<ulong, string> _mountMotion = new();
    private Dictionary<ulong, string> _capeMotion = new();
    private Dictionary<ulong, string> _miscMotion = new();
    private Dictionary<ulong, string> _armetMotion = new();
    private Dictionary<ulong, string> _spiritMotion = new();
    private Dictionary<ulong, string> _headMotion = new();
    private Dictionary<ulong, string> _pelvisMotion = new();
    private Dictionary<int, MagicSkillGroup> _magicSkills = new();

    // ── Public properties ─────────────────────────────────────────────────────

    public IReadOnlyList<NpcTypeInfo> Npcs => _npcs;
    public IReadOnlyList<C3DSimpleObjInfo> SimpleObjs => _simpleObjs;
    public IReadOnlyList<C3DEffectInfo> Effects => _effects;
    public IReadOnlyList<TransformInfo> Transforms => _transforms;
    public IReadOnlyList<ItemTextureInfo> ItemTextures => _itemTextures;
    public IReadOnlyList<SimpleRoleTypeInfo> SimpleRoles => _simpleRoles;
    public IReadOnlyList<RolePart> RoleParts => _roleParts;

    public IReadOnlyDictionary<ulong, string> MeshMap => _mesh;
    public IReadOnlyDictionary<ulong, string> TextureMap => _tex;
    public IReadOnlyDictionary<ulong, string> MotionMap => _motion;
    public IReadOnlyDictionary<ulong, string> EffectObjMap => _effectObj;
    public IReadOnlyDictionary<ulong, string> WeaponMotionMap => _weaponMotion;
    public IReadOnlyDictionary<ulong, string> MountMotionMap => _mountMotion;
    public IReadOnlyDictionary<ulong, string> CapeMotionMap => _capeMotion;
    public IReadOnlyDictionary<ulong, string> MiscMotionMap => _miscMotion;
    public IReadOnlyDictionary<ulong, string> ArmetMotionMap => _armetMotion;
    public IReadOnlyDictionary<ulong, string> SpiritMotionMap => _spiritMotion;
    public IReadOnlyDictionary<ulong, string> HeadMotionMap => _headMotion;
    public IReadOnlyDictionary<ulong, string> PelvisMotionMap => _pelvisMotion;

    public IReadOnlyDictionary<int, MagicSkillGroup> MagicSkills => _magicSkills;

    // ── Internal state ────────────────────────────────────────────────────────

    private string _loadedPath = string.Empty;
    private string _iniPath = string.Empty;

    private readonly Dictionary<string, TmeEntry[]> _tmeCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] s_tmeDirs = ["TerrainMagic", "tme"];

    // ── Load ──────────────────────────────────────────────────────────────────

    public Task LoadAsync(string conquerPath) => Task.Run(() =>
    {
        if (_loadedPath == conquerPath) return;

        _iniPath = Path.Combine(conquerPath, "ini");
        _tmeCache.Clear();

        using var src = new WdbIniSource(Path.Combine(_iniPath, "c3.wdb"));

        // ── Helper: return full path to an INI file inside the ini folder ──
        string Ini(string f) => Path.Combine(_iniPath, f);

        // ── Plain INI Parsers (Non-Stream Based) ───────────────────────────────
        _npcs = NpcIniParser.Parse(Ini("npc.ini"));

        // ── Stream-based Parsers via WDB / DBC / Plain INI Fallback ──────────
        _simpleObjs = ParseFromWdb(src, Ini("3DSimpleObj.ini"), SimpleObjIniParser.Parse);
        _effects = ParseFromWdb(src, Ini("3DEffect.ini"), EffectIniParser.Parse);

        // Role parts
        _roleParts.Clear();
        _roleParts.AddRange(ParseFromWdb(src, Ini("Armor.ini"), r => RolePartIniParser.Parse(r, RolePartType.Armor)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Armet.ini"), r => RolePartIniParser.Parse(r, RolePartType.Armet)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Weapon.ini"), r => RolePartIniParser.Parse(r, RolePartType.Weapon)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Mount.ini"), r => RolePartIniParser.Parse(r, RolePartType.Mount)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Cape.ini"), r => RolePartIniParser.Parse(r, RolePartType.Cape)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Head.ini"), r => RolePartIniParser.Parse(r, RolePartType.Head)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Misc.ini"), r => RolePartIniParser.Parse(r, RolePartType.Misc)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Pelvis.ini"), r => RolePartIniParser.Parse(r, RolePartType.Pelvis)));
        _roleParts.AddRange(ParseFromWdb(src, Ini("Spirit.ini"), r => RolePartIniParser.Parse(r, RolePartType.Spirit)));

        // Transforms & Other Plain INIs
        var transformById = new Dictionary<int, TransformInfo>();
        foreach (var t in AdditiveIniParser.Parse(Ini("AdditiveSize.ini"))) transformById[t.Index] = t;
        foreach (var t in TransFormIniParser.Parse(Ini("TransForm.ini"))) transformById[t.Index] = t;
        _transforms = transformById.Values.ToList();

        _simpleRoles = SimpleRoleIniParser.Parse(Ini("3DSimpleRole.ini"));
        _itemTextures = ItemTextureIniParser.Parse(Ini("ItemTexture.ini"));

        // ── ResIni Maps (Using Generic ParseFromWdb Helper) ───────────────────
        _mesh = ParseFromWdb(src, Ini("3dobj.ini"), ResIniParser.Parse);
        _tex = ParseFromWdb(src, Ini("3dtexture.ini"), ResIniParser.Parse);
        _motion = ParseFromWdb(src, Ini("3dmotion.ini"), ResIniParser.Parse);
        _effectObj = ParseFromWdb(src, Ini("3DEffectObj.ini"), ResIniParser.Parse);
        _weaponMotion = ParseFromWdb(src, Ini("WeaponMotion.ini"), ResIniParser.Parse);
        _mountMotion = ParseFromWdb(src, Ini("MountMotion.ini"), ResIniParser.Parse);
        _capeMotion = ParseFromWdb(src, Ini("capemotion.ini"), ResIniParser.Parse);
        _miscMotion = ParseFromWdb(src, Ini("miscmotion.ini"), ResIniParser.Parse);
        _armetMotion = ParseFromWdb(src, Ini("armetmotion.ini"), ResIniParser.Parse);
        _spiritMotion = ParseFromWdb(src, Ini("spiritmotion.ini"), ResIniParser.Parse);
        _headMotion = ParseFromWdb(src, Ini("headmotion.ini"), ResIniParser.Parse);
        _pelvisMotion = ParseFromWdb(src, Ini("pelvismotion.ini"), ResIniParser.Parse);

        _magicSkills = MagicEffectIniParser.Parse(Ini("MagicEffect.ini"));
        _loadedPath = conquerPath;
    });

    /// <summary>
    /// Opens a configuration file via <see cref="WdbIniSource"/> (prioritizing .dbc versions inside the WDB archive)
    /// with an automatic fallback to physical disk-bound .ini files if the archive structure is absent.
    /// </summary>
    private static T ParseFromWdb<T>(WdbIniSource src, string plainIniPath, Func<StreamReader, T> parseFunc) where T : new()
    {
        using var reader = src.OpenIni(plainIniPath);
        return reader is not null
            ? parseFunc(reader)
            : new T();
    }

    // ── Basic resolvers ───────────────────────────────────────────────────────

    public string? ResolveMesh(ulong id) => MeshMap.GetValueOrDefault(id);
    public string? ResolveTexture(ulong id) => TextureMap.GetValueOrDefault(id);
    public string? ResolveEffectObj(uint id) => EffectObjMap.GetValueOrDefault(id);

    public string? ResolveMotion(ulong motionId)
    {
        if (MotionMap.TryGetValue(motionId, out var direct)) return direct;

        var s = motionId.ToString();
        if (s.Length == 10)
        {
            var stripped = s[..6] + s[7..];
            if (ulong.TryParse(stripped, out var key2) && MotionMap.TryGetValue(key2, out var p2))
                return p2;
        }
        if (s.Length >= 7)
        {
            var stretched = s.Insert(4, "0");
            if (ulong.TryParse(stretched, out var key3) && MotionMap.TryGetValue(key3, out var p3))
                return p3;
        }
        return null;
    }

    public string? ResolveWeaponMotion(ulong weaponId, int actionType)
    {
        weaponId = (weaponId / 10) * 10;

        ulong key = weaponId * 1000 + (ulong)actionType;
        if (WeaponMotionMap.TryGetValue(key, out var p1)) return p1;

        key = weaponId * 1000 + 999;
        if (WeaponMotionMap.TryGetValue(key, out var p2)) return p2;

        ulong categoryId = (weaponId / 1000) * 1000 + 999;
        key = categoryId * 1000 + (ulong)actionType;
        if (WeaponMotionMap.TryGetValue(key, out var p3)) return p3;

        key = categoryId * 1000 + 999;
        if (WeaponMotionMap.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    public string? ResolveMountMotion(ulong mountId, int actionType)
    {
        mountId = (mountId / 10) * 10;

        ulong key = mountId * 1000 + (ulong)actionType;
        if (MountMotionMap.TryGetValue(key, out var p1)) return p1;

        key = mountId * 1000 + 999;
        if (MountMotionMap.TryGetValue(key, out var p2)) return p2;

        ulong categoryId = (mountId / 1000) * 1000 + 999;
        key = categoryId * 1000 + (ulong)actionType;
        if (MountMotionMap.TryGetValue(key, out var p3)) return p3;

        key = categoryId * 1000 + 999;
        if (MountMotionMap.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    /// <summary>
    /// Generic resolver for the per-part-type motion maps (Cape, Misc, Armet, Spirit, Head, Pelvis).
    /// Key scheme: partId * 1000 + actionType, with a fallback to partId * 1000 + 999.
    /// </summary>
    private static string? ResolvePartMotion(IReadOnlyDictionary<ulong, string> map, ulong partId, int actionType)
    {
        ulong key = partId * 1000 + (ulong)actionType;
        if (map.TryGetValue(key, out var p1)) return p1;

        key = partId * 1000 + 999;
        if (map.TryGetValue(key, out var p2)) return p2;

        key = partId + (ulong)actionType;
        if (map.TryGetValue(key, out var p3)) return p3;

        key = partId + 999;
        if (map.TryGetValue(key, out var p4)) return p4;

        return null;
    }

    public string? ResolveCapeMotion(ulong capeId, int actionType)
        => ResolvePartMotion(_capeMotion, capeId, actionType);

    public string? ResolveMiscMotion(ulong miscId, int actionType)
        => ResolvePartMotion(_miscMotion, miscId, actionType);

    public string? ResolveArmetMotion(ulong armetId, int actionType)
        => ResolvePartMotion(_armetMotion, armetId, actionType);

    public string? ResolveSpiritMotion(ulong spiritId, int actionType)
        => ResolvePartMotion(_spiritMotion, spiritId, actionType);

    public string? ResolveHeadMotion(ulong headId, int actionType)
        => ResolvePartMotion(_headMotion, headId, actionType);

    public string? ResolvePelvisMotion(ulong pelvisId, int actionType)
        => ResolvePartMotion(_pelvisMotion, pelvisId, actionType);

    // ── Finders ───────────────────────────────────────────────────────────────

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

    public C3DEffectInfo? FindEffect(string key)
    {
        foreach (var e in Effects)
            if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    public TransformInfo? FindTransform(int index) { foreach (var t in Transforms) if (t.Index == index) return t; return null; }
    public SimpleRoleTypeInfo? FindSimpleRole(int index) { foreach (var r in SimpleRoles) if (r.Index == index) return r; return null; }
    public ItemTextureInfo? FindItemTexture(uint id) { foreach (var it in ItemTextures) if (it.Id == id) return it; return null; }

    public uint ResolveItemTexture(uint itemId, ItemColor color)
        => FindItemTexture(itemId)?.GetTexture(color) ?? 0;

    public uint ResolveItemTexture(uint itemId, byte colorValue)
        => FindItemTexture(itemId)?.GetTexture(colorValue) ?? 0;

    public MagicSkillGroup? FindMagicSkill(int baseId)
        => _magicSkills.GetValueOrDefault(baseId);

    public RolePart? FindRolePart(uint id, RolePartType type)
    {
        foreach (var p in _roleParts)
            if (p.Id == id && p.PartType == type) return p;
        return null;
    }

    // ── TME ───────────────────────────────────────────────────────────────────

    public TmeEntry[] ResolveTme(string tmeKey)
    {
        string cacheKey = tmeKey.EndsWith(".tme", StringComparison.OrdinalIgnoreCase)
            ? tmeKey[..^4] : tmeKey;

        if (_tmeCache.TryGetValue(cacheKey, out var cached)) return cached;

        string fileName = cacheKey + ".tme";
        foreach (var dir in s_tmeDirs)
        {
            string fullPath = Path.Combine(_iniPath, dir, fileName);
            if (!File.Exists(fullPath)) continue;

            var entries = TmeParser.Parse(fullPath);
            _tmeCache[cacheKey] = entries;
            return entries;
        }

        _tmeCache[cacheKey] = [];
        return [];
    }
}