using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>
/// Equivalent to original C++ C3Texture structure.
/// </summary>
public sealed class C3TextureEntry
{
    public int ID = -1;

    /// <summary>Duplicate/shared reference count.</summary>
    public int DupCount = 0;

    /// <summary>Texture file name.</summary>
    public string Name = string.Empty;

    /// <summary>Texture object.</summary>
    public Texture2D? Texture;

    /// <summary>Texture format.</summary>
    public SurfaceFormat Format;

    public int Width;

    public int Height;
}

/// <summary>
/// MonoGame port of original DX8 C3Texture system.
/// Keeps original API style and behavior as closely as possible.
/// </summary>
public static class C3Texture
{
    public const int TEX_MAX = 10240;

    public static int TextureCount = 0;

    public static readonly C3TextureEntry?[] Textures =
        new C3TextureEntry[TEX_MAX];

    private static readonly object _lock = new();

    private static GraphicsDevice? _graphicsDevice;

    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>
    /// Equivalent to Texture_Clear()
    /// </summary>
    public static void Texture_Clear(C3TextureEntry tex)
    {
        tex.ID = -1;
        tex.DupCount = 0;
        tex.Name = string.Empty;
        tex.Texture = null;
        tex.Width = 0;
        tex.Height = 0;
        tex.Format = SurfaceFormat.Color;
    }

    /// <summary>
    /// Equivalent to original Texture_Load()
    /// Returns texture slot index or -1 on failure.
    /// </summary>
    public static int Texture_Load(string name, Texture2D texture = null,
        bool duplicate = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;

        if (_graphicsDevice == null)
            throw new InvalidOperationException(
                "C3Texture.Initialize() not called.");

        lock (_lock)
        {
            // Duplicate/shared texture lookup
            if (duplicate)
            {
                int add = 0;

                for (int t = 0; t < TEX_MAX; t++)
                {
                    if (Textures[t] != null)
                    {
                        if (string.Equals(
                                Textures[t]!.Name,
                                name,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            Textures[t]!.DupCount++;
                            return t;
                        }

                        if (++add == TextureCount)
                            break;
                    }
                }
            }
            if (texture == null)
                return -1;
            // Create texture entry
            var tex = new C3TextureEntry();

            Texture_Clear(tex);
            tex.Texture = texture;
            tex.Name = name;
            tex.DupCount = 1;
            tex.Width = texture.Width;
            tex.Height = texture.Height;
            tex.Format = texture.Format;

            // Insert into global cache
            for (int t = 0; t < TEX_MAX; t++)
            {
                if (Textures[t] == null)
                {
                    tex.ID = t;

                    Textures[t] = tex;

                //    TextureCount++;

                    return t;
                }
            }

            texture.Dispose();
            return -1;
        }
    }

    public static void Texture_Unload(int texIndex)
    {
        Texture_Unload(Get(texIndex));
    }
    /// <summary>
    /// Equivalent to Texture_Unload()
    /// </summary>
    public static void Texture_Unload(C3TextureEntry? tex)
    {
        if (tex == null)
            return;

        lock (_lock)
        {
            tex.DupCount--;

            if (tex.DupCount <= 0)
            {
                int id = tex.ID;

                tex.Texture?.Dispose();

                Texture_Clear(tex);

                if (id >= 0 && id < TEX_MAX)
                    Textures[id] = null;

             //   TextureCount--;
            }

            tex = null;
        }
    }

    /// <summary>
    /// Unload and dispose all textures.
    /// Equivalent to engine shutdown cleanup.
    /// </summary>
    public static void Texture_UnloadAll()
    {
        lock (_lock)
        {
            for (int t = 0; t < TEX_MAX; t++)
            {
                var tex = Textures[t];

                if (tex == null)
                    continue;

                tex.Texture?.Dispose();

                Texture_Clear(tex);

                Textures[t] = null;
            }

          //  TextureCount = 0;
        }
    }

    public static C3TextureEntry? Get(int index)
    {
        if (index < 0 || index >= TEX_MAX)
            return null;

        lock (_lock)
        {
            if (Textures[index] == null)
                return null;

            return Textures[index];
        }
    }
}