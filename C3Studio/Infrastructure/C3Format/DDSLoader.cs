using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>
/// DDS texture loader for DXT1/3/5 compressed and 32/24-bit uncompressed.
/// Swizzles BGRA→RGBA for D3D8 uncompressed textures.
/// </summary>
public static class DDSLoader
{
    private const uint DDS_MAGIC = 0x20534444;
    private const uint DDSD_MIPMAPCOUNT = 0x00020000;
    private const uint DDPF_FOURCC = 0x00000004;
    private const uint DDPF_RGB = 0x00000040;
    private const uint FOURCC_DX10 = 0x30315844;
    private const uint FOURCC_DXT1 = 0x31545844;
    private const uint FOURCC_DXT2 = 0x32545844;
    private const uint FOURCC_DXT3 = 0x33545844;
    private const uint FOURCC_DXT4 = 0x34545844;
    private const uint FOURCC_DXT5 = 0x35545844;

    public static Texture2D Load(GraphicsDevice device, string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var br = new BinaryReader(fs);
        return Load(device, br);
    }

    public static Texture2D Load(GraphicsDevice device, BinaryReader br)
    {
        if (br.ReadUInt32() != DDS_MAGIC)
            throw new InvalidDataException("Not a DDS file.");

        br.ReadUInt32();
        uint flags = br.ReadUInt32();
        uint height = br.ReadUInt32();
        uint width = br.ReadUInt32();
        br.ReadUInt32(); br.ReadUInt32();
        uint mipCount = br.ReadUInt32();
        br.ReadBytes(44);
        br.ReadUInt32();
        uint pfFlags = br.ReadUInt32();
        uint fourCC = br.ReadUInt32();
        uint bitCnt = br.ReadUInt32();
        uint rMask = br.ReadUInt32();
        uint gMask = br.ReadUInt32();
        uint bMask = br.ReadUInt32();
        uint aMask = br.ReadUInt32();
        br.ReadBytes(16); br.ReadUInt32();

        if ((pfFlags & DDPF_FOURCC) != 0 && fourCC == FOURCC_DX10)
            br.ReadBytes(20);

        SurfaceFormat fmt = DetectFormat(pfFlags, fourCC, bitCnt);
        bool hasMips = (flags & DDSD_MIPMAPCOUNT) != 0 && mipCount > 1;
        int levels = hasMips ? (int)mipCount : 1;
        bool is24bit = (pfFlags & DDPF_RGB) != 0 && bitCnt == 24;
        bool swizzle = (pfFlags & DDPF_RGB) != 0 && bitCnt == 32
                        && bMask == 0x000000FF && rMask == 0x00FF0000;

        var texture = new Texture2D(device, (int)width, (int)height, hasMips, fmt);
        int w = (int)width, h = (int)height;

        for (int lvl = 0; lvl < levels; lvl++)
        {
            int rawSize = is24bit ? w * h * 3 : CalcSize(w, h, fmt);
            byte[] raw = br.ReadBytes(rawSize);
            if (raw.Length < rawSize) break;

            byte[] data = is24bit ? Expand24to32(raw, w * h, bMask == 0x000000FF)
                        : swizzle ? SwizzleBGRA(raw)
                        : raw;

            texture.SetData(lvl, null, data, 0, data.Length);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return texture;
    }

    private static SurfaceFormat DetectFormat(uint pfFlags, uint fourCC, uint bitCnt) =>
        (pfFlags & DDPF_FOURCC) != 0 ? fourCC switch
        {
            FOURCC_DXT1 => SurfaceFormat.Dxt1,
            FOURCC_DXT2 or FOURCC_DXT3 => SurfaceFormat.Dxt3,
            FOURCC_DXT4 or FOURCC_DXT5 => SurfaceFormat.Dxt5,
            _ => SurfaceFormat.Color,
        } : SurfaceFormat.Color;

    private static int CalcSize(int w, int h, SurfaceFormat fmt) => fmt switch
    {
        SurfaceFormat.Dxt1 or SurfaceFormat.Dxt1a
            => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8,
        SurfaceFormat.Dxt3 or SurfaceFormat.Dxt5
            => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16,
        _ => w * h * 4,
    };

    private static byte[] SwizzleBGRA(byte[] src)
    {
        var dst = (byte[])src.Clone();
        for (int i = 0; i + 3 < dst.Length; i += 4)
        { byte t = dst[i]; dst[i] = dst[i + 2]; dst[i + 2] = t; }
        return dst;
    }

    private static byte[] Expand24to32(byte[] src, int pixels, bool isBGR)
    {
        var dst = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            int s = i * 3, d = i * 4;
            if (isBGR) { dst[d] = src[s + 2]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 0]; }
            else { dst[d] = src[s + 0]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; }
            dst[d + 3] = 255;
        }
        return dst;
    }
}
