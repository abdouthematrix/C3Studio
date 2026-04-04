using C3Studio.Core.Models;
using C3Studio.Infrastructure.Ini;
using System.IO;

namespace C3Studio.Core.Services;

public interface IGameDataService
{
    IReadOnlyList<NpcTypeInfo> Npcs { get; }
    IReadOnlyList<C3DSimpleObjInfo> SimpleObjs { get; }
    IReadOnlyList<C3DEffectInfo> Effects { get; }
    IReadOnlyList<ArmorTypeInfo> Armors { get; }
    IReadOnlyList<ArmetTypeInfo> Armets { get; }
    IReadOnlyList<WeaponTypeInfo> Weapons { get; }

    IReadOnlyDictionary<ulong, string> MeshMap { get; }
    IReadOnlyDictionary<ulong, string> TextureMap { get; }
    IReadOnlyDictionary<ulong, string> MotionMap { get; }
    IReadOnlyDictionary<ulong, string> EffectObjMap { get; }
    IReadOnlyDictionary<ulong, string> WeaponMotionMap { get; }

    Task LoadAsync(string conquerPath);

    string? ResolveMesh(ulong id);
    string? ResolveTexture(ulong id);
    string? ResolveMotion(ulong motionId);
    string? ResolveEffectObj(uint id);

    /// <summary>
    /// Replicates <c>C3DRole::GetWeaponMotion</c>: four-pass fallback lookup.
    /// </summary>
    string? ResolveWeaponMotion(ulong weaponId, int actionType);

    C3DSimpleObjInfo? FindSimpleObj(uint typeId);
    C3DEffectInfo? FindEffect(uint id);
    C3DEffectInfo? FindEffect(string key);
    ArmorTypeInfo? FindArmor(uint id);
    ArmetTypeInfo? FindArmet(uint id);
    WeaponTypeInfo? FindWeapon(uint id);
}

public class GameDataService : IGameDataService
{
    private List<NpcTypeInfo> _npcs = new();
    private List<C3DSimpleObjInfo> _simpleObjs = new();
    private List<C3DEffectInfo> _effects = new();
    private List<ArmorTypeInfo> _armors = new();
    private List<ArmetTypeInfo> _armets = new();
    private List<WeaponTypeInfo> _weapons = new();
    private Dictionary<ulong, string> _mesh = new();
    private Dictionary<ulong, string> _tex = new();
    private Dictionary<ulong, string> _motion = new();
    private Dictionary<ulong, string> _effectObj = new();
    private Dictionary<ulong, string> _weaponMotion = new();

    public IReadOnlyList<NpcTypeInfo> Npcs => _npcs;
    public IReadOnlyList<C3DSimpleObjInfo> SimpleObjs => _simpleObjs;
    public IReadOnlyList<C3DEffectInfo> Effects => _effects;
    public IReadOnlyList<ArmorTypeInfo> Armors => _armors;
    public IReadOnlyList<ArmetTypeInfo> Armets => _armets;
    public IReadOnlyList<WeaponTypeInfo> Weapons => _weapons;

    public IReadOnlyDictionary<ulong, string> MeshMap => _mesh;
    public IReadOnlyDictionary<ulong, string> TextureMap => _tex;
    public IReadOnlyDictionary<ulong, string> MotionMap => _motion;
    public IReadOnlyDictionary<ulong, string> EffectObjMap => _effectObj;
    public IReadOnlyDictionary<ulong, string> WeaponMotionMap => _weaponMotion;

    private string _loadedPath = string.Empty;

    private static Dictionary<ulong, string> Cast(Dictionary<ulong, string> d) => d;

    public Task LoadAsync(string conquerPath) => Task.Run(() =>
    {
        if (_loadedPath == conquerPath) return;

        string Ini(string f) => Path.Combine(conquerPath, "ini", f);

        _npcs = NpcIniParser.Parse(Ini("npc.ini"));
        _simpleObjs = SimpleObjIniParser.Parse(Ini("3DSimpleObj.ini"));
        _effects = EffectIniParser.Parse(Ini("3DEffect.ini"));
        _armors = ArmorIniParser.Parse(Ini("Armor.ini"));
        _armets = ArmetIniParser.Parse(Ini("Armet.ini"));
        _weapons = WeaponIniParser.Parse(Ini("Weapon.ini"));

        _mesh = Cast(ResIniParser.Parse(Ini("3dobj.ini")));
        _tex = Cast(ResIniParser.Parse(Ini("3dtexture.ini")));
        _motion = Cast(ResIniParser.Parse(Ini("3dmotion.ini")));
        _effectObj = Cast(ResIniParser.Parse(Ini("3DEffectObj.ini")));
        // WeaponMotion.ini shares the same id=filepath format.
        // IDs such as 5000009990310 exceed uint32 — ResIniParser already uses ulong.
        _weaponMotion = Cast(ResIniParser.Parse(Ini("WeaponMotion.ini")));

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

    public ArmorTypeInfo? FindArmor(uint id)
    {
        foreach (var a in Armors) if (a.Id == id) return a;
        return null;
    }

    public ArmetTypeInfo? FindArmet(uint id)
    {
        foreach (var a in Armets) if (a.Id == id) return a;
        return null;
    }

    public WeaponTypeInfo? FindWeapon(uint id)
    {
        foreach (var w in Weapons) if (w.Id == id) return w;
        return null;
    }
}