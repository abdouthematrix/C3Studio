using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public class C3TextureEntry
{
    public int           ID       { get; set; } = -1;
    public int           RefCount { get; set; } =  0;
    public string        Name     { get; set; } = string.Empty;
    public Texture2D     Texture  { get; set; } = null!;
    public SurfaceFormat Format   { get; set; }
    public int           Width    { get; set; }
    public int           Height   { get; set; }
}

/// <summary>Global ref-counted texture cache. Call Initialize(GraphicsDevice) once at startup.</summary>
public static partial class C3Texture
{
    public const int TEX_MAX = 512;

    internal static readonly C3TextureEntry?[] _cache = new C3TextureEntry[TEX_MAX];
    internal static readonly object            _lock  = new();
    private  static GraphicsDevice?            _gd;

    public static void Initialize(GraphicsDevice gd) { _gd = gd; }

    public static int Texture_Load(string name, bool bDuplicate = true)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        if (_gd == null) throw new InvalidOperationException("C3Texture.Initialize() not called.");

        lock (_lock)
        {
            if (bDuplicate)
                for (int t = 0; t < TEX_MAX; t++)
                {
                    var e = _cache[t];
                    if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    { e.RefCount++; return t; }
                }

            Texture2D tex;
            try { tex = LoadFromDisk(name); }
            catch (Exception ex)
            { System.Diagnostics.Debug.WriteLine($"[C3Texture] Failed '{name}': {ex.Message}"); return -1; }

            for (int t = 0; t < TEX_MAX; t++)
                if (_cache[t] == null)
                {
                    _cache[t] = new C3TextureEntry { ID=t, RefCount=1, Name=name, Texture=tex,
                        Format=tex.Format, Width=tex.Width, Height=tex.Height };
                    return t;
                }

            tex.Dispose(); return -1;
        }
    }

    public static void Texture_Unload(int index)
    {
        if (index < 0 || index >= TEX_MAX) return;
        lock (_lock)
        {
            var e = _cache[index]; if (e == null) return;
            if (--e.RefCount <= 0) { e.Texture?.Dispose(); _cache[index] = null; }
        }
    }

    public static void Texture_UnloadAll()
    {
        lock (_lock)
            for (int t = 0; t < TEX_MAX; t++) { _cache[t]?.Texture?.Dispose(); _cache[t] = null; }
    }

    public static C3TextureEntry? Get(int index) =>
        (index >= 0 && index < TEX_MAX) ? _cache[index] : null;

    public static int GetLoadedCount()
    { int n=0; lock(_lock) for(int t=0;t<TEX_MAX;t++) if(_cache[t]!=null) n++; return n; }

    private static Texture2D LoadFromDisk(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".dds" => DDSLoader.Load(_gd!, path),
            ".tga" => TGALoader.Load(_gd!, path),
            _      => LoadStream(path)
        };
    }

    private static Texture2D LoadStream(string p)
    { using var s = File.OpenRead(p); return Texture2D.FromStream(_gd!, s); }

    public static int FindByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        lock (_lock)
            for (int t = 0; t < TEX_MAX; t++)
            {
                var e = _cache[t];
                if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        return -1;
    }

    /// <summary>Insert an already-created Texture2D. RefCount=1. Returns slot or -1 if full.</summary>
    public static int InsertTexture(string name, Texture2D tex)
    {
        if (string.IsNullOrEmpty(name) || tex == null) return -1;
        lock (_lock)
        {
            for (int t = 0; t < TEX_MAX; t++)
            {
                var e = _cache[t];
                if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                { e.RefCount++; return t; }
            }
            for (int t = 0; t < TEX_MAX; t++)
                if (_cache[t] == null)
                {
                    _cache[t] = new C3TextureEntry
                    {
                        ID = t,
                        RefCount = 1,
                        Name = name,
                        Texture = tex,
                        Format = tex.Format,
                        Width = tex.Width,
                        Height = tex.Height
                    };
                    return t;
                }
        }
        return -1;
    }
}
