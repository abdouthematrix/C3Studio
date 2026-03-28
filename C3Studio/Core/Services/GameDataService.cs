using System.IO;
using C3Studio.Infrastructure.Ini;
using C3Studio.Models;

namespace C3Studio.Core.Services;

public interface IGameDataService
{
    IReadOnlyList<NpcTypeInfo> Npcs { get; }
    IReadOnlyList<C3DSimpleObjInfo> SimpleObjs { get; }
    IReadOnlyList<C3DEffectInfo> Effects { get; }
    IReadOnlyDictionary<ulong, string> MeshMap { get; }
    IReadOnlyDictionary<ulong, string> TextureMap { get; }
    IReadOnlyDictionary<ulong, string> MotionMap { get; }
    IReadOnlyDictionary<ulong, string> EffectObjMap { get; }
    Task LoadAsync(string conquerPath);
    string? ResolveMesh(ulong id);
    string? ResolveTexture(ulong id);
    string? ResolveMotion(ulong motionId);
    string? ResolveEffectObj(uint id);
    C3DSimpleObjInfo? FindSimpleObj(uint typeId);
    C3DEffectInfo? FindEffect(uint id);
    C3DEffectInfo? FindEffect(string key);
}

public class GameDataService : IGameDataService
{
    private List<NpcTypeInfo> _npcs = new();
    private List<C3DSimpleObjInfo> _simpleObjs = new();
    private List<C3DEffectInfo> _effects = new();
    private Dictionary<ulong, string> _mesh = new();
    private Dictionary<ulong, string> _tex = new();
    private Dictionary<ulong, string> _motion = new();
    private Dictionary<ulong, string> _effectObj = new();

    public IReadOnlyList<NpcTypeInfo> Npcs => _npcs;
    public IReadOnlyList<C3DSimpleObjInfo> SimpleObjs => _simpleObjs;
    public IReadOnlyList<C3DEffectInfo> Effects => _effects;
    public IReadOnlyDictionary<ulong, string> MeshMap => _mesh;
    public IReadOnlyDictionary<ulong, string> TextureMap => _tex;
    public IReadOnlyDictionary<ulong, string> MotionMap => _motion;
    public IReadOnlyDictionary<ulong, string> EffectObjMap => _effectObj;

    private string _loadedPath = string.Empty;

    private static Dictionary<ulong, string> Cast(Dictionary<ulong, string> d) => d;

    public Task LoadAsync(string conquerPath) => Task.Run(() =>
    {
        if (_loadedPath == conquerPath) return;

        string Ini(string f) => Path.Combine(conquerPath, "ini", f);

        _npcs = NpcIniParser.Parse(Ini("npc.ini"));
        _simpleObjs = SimpleObjIniParser.Parse(Ini("3DSimpleObj.ini"));
        _effects = EffectIniParser.Parse(Ini("3DEffect.ini"));

        _mesh = Cast(ResIniParser.Parse(Ini("3dobj.ini")));
        _tex = Cast(ResIniParser.Parse(Ini("3dtexture.ini")));
        _motion = Cast(ResIniParser.Parse(Ini("3dmotion.ini")));
        _effectObj = Cast(ResIniParser.Parse(Ini("3DEffectObj.ini")));

        _loadedPath = conquerPath;
    });

    public string? ResolveMesh(ulong id) => MeshMap.GetValueOrDefault(id);
    public string? ResolveTexture(ulong id) => TextureMap.GetValueOrDefault(id);
    public string? ResolveEffectObj(uint id) => EffectObjMap.GetValueOrDefault(id);

    /// <summary>
    /// Two-pass motion resolver.
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

    public C3DSimpleObjInfo? FindSimpleObj(uint typeId)
    {
        foreach (var o in SimpleObjs) if (o.IdType == typeId) return o;
        return null;
    }

    public C3DEffectInfo? FindEffect(uint id)
    {
        foreach (var e in _effects) if (e.Id == id) return e;
        return null;
    }

    /// <summary>Looks up an effect by its raw section key (e.g. "1ghost" or "10000").</summary>
    public C3DEffectInfo? FindEffect(string key)
    {
        foreach (var e in _effects)
            if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }
}