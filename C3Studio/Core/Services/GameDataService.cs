using System.IO;
using C3Studio.Infrastructure.Ini;
using C3Studio.Models;

namespace C3Studio.Core.Services;

public interface IGameDataService
{
    IReadOnlyList<NpcTypeInfo>      Npcs       { get; }
    IReadOnlyList<C3DSimpleObjInfo> SimpleObjs { get; }
    IReadOnlyDictionary<ulong, string> MeshMap    { get; }
    IReadOnlyDictionary<ulong, string> TextureMap { get; }
    IReadOnlyDictionary<ulong, string> MotionMap  { get; }

    Task LoadAsync(string conquerPath);
}

public class GameDataService : IGameDataService
{
    private List<NpcTypeInfo>      _npcs       = new();
    private List<C3DSimpleObjInfo> _simpleObjs = new();
    private Dictionary<ulong,string> _mesh    = new();
    private Dictionary<ulong,string> _tex     = new();
    private Dictionary<ulong,string> _motion  = new();

    public IReadOnlyList<NpcTypeInfo>         Npcs       => _npcs;
    public IReadOnlyList<C3DSimpleObjInfo>    SimpleObjs => _simpleObjs;
    public IReadOnlyDictionary<ulong, string> MeshMap    => _mesh;
    public IReadOnlyDictionary<ulong, string> TextureMap => _tex;
    public IReadOnlyDictionary<ulong, string> MotionMap  => _motion;

    private string _loadedPath = string.Empty;

    public Task LoadAsync(string conquerPath) => Task.Run(() =>
    {
        // Skip re-parse if already loaded from the same path
        if (_loadedPath == conquerPath) return;

        string Ini(string f) => Path.Combine(conquerPath, "ini", f);

        _npcs       = NpcIniParser.Parse(Ini("npc.ini"));
        _simpleObjs = SimpleObjIniParser.Parse(Ini("3DSimpleObj.ini"));

        _mesh   = Cast(ResIniParser.Parse(Ini("3dobj.ini")));
        _tex    = Cast(ResIniParser.Parse(Ini("3dtexture.ini")));
        _motion = Cast(ResIniParser.Parse(Ini("3dmotion.ini")));

        _loadedPath = conquerPath;
    });

    private static Dictionary<ulong,string> Cast(Dictionary<ulong,string> d) => d;
}
