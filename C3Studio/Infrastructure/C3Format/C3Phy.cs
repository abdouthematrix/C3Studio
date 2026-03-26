using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public static class C3Constants { public const int BoneMax=2; public const int MorphMax=4; }

public class PhyVertex
{
    public Vector3[] Positions;
    public Vector2   TexCoord;
    public int[]     BoneIndex;
    public float[]   BoneWeight;

    public PhyVertex(int morphMax)
    {
        Positions  = new Vector3[morphMax];
        BoneIndex  = new int  [C3Constants.BoneMax];
        BoneWeight = new float[C3Constants.BoneMax];
    }
}

public class PhyOutVertex
{
    public Vector3 Position = Vector3.Zero;
    public Vector2 TexCoord = Vector2.Zero;
}

/// <summary>
/// One skinned mesh inside a .c3 file.
/// Vertex colour is always WHITE; material tint (R,G,B,Alpha) applied by renderer.
/// BlendAsb/BlendAdb carry D3D blend factor values: 5/6=AlphaBlend, 2/2=Additive.
/// Index buffer layout: [0..NormalTriCount*3) normal tris, then alpha tris.
/// </summary>
public class C3Phy
{
    public string Name     { get; set; } = string.Empty;
    public string TexName  { get; set; } = string.Empty;
    public int    TexIndex { get; set; } = -1;

    public int NormalVertexCount { get; set; }
    public int AlphaVertexCount  { get; set; }
    public int NormalTriCount    { get; set; }
    public int AlphaTriCount     { get; set; }
    public int BlendCount        { get; set; }

    public List<PhyVertex>    SourceVertices { get; } = new();
    public List<PhyOutVertex> OutputVertices { get; } = new();
    public List<ushort>       IndexBuffer    { get; } = new();

    public Vector3  BBoxMin    { get; set; }
    public Vector3  BBoxMax    { get; set; }
    public Matrix   InitMatrix { get; set; } = Matrix.Identity;
    public C3Motion? Motion    { get; set; }
    public C3Key    Key        { get; set; } = new();

    public float Alpha { get; set; } = 1f;
    public float R     { get; set; } = 1f;
    public float G     { get; set; } = 1f;
    public float B     { get; set; } = 1f;
    public bool  Draw  { get; set; } = true;

    // D3D blend factors: 5=SrcAlpha, 6=InvSrcAlpha (standard AlphaBlend)
    public int BlendAsb { get; set; } = 5;
    public int BlendAdb { get; set; } = 6;

    public int     TexRow { get; set; } = 1;
    public Vector2 UVStep { get; set; } = Vector2.Zero;
    private Vector2 _accumUV;

    public bool TwoSided     { get; set; }
    public bool IsFullyOpaque => Alpha == 1f;

    // ------------------------------------------------------------------
    public static C3Phy Load(BinaryReader br, string chunkTag)
    {
        var phy = new C3Phy();

        uint nameLen = br.ReadUInt32();
        phy.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

        phy.BlendCount        = (int)br.ReadUInt32();
        phy.NormalVertexCount = (int)br.ReadUInt32();
        phy.AlphaVertexCount  = (int)br.ReadUInt32();

        int totalVerts = phy.NormalVertexCount + phy.AlphaVertexCount;
        int morphMax   = (chunkTag == "PHY3" || chunkTag == "PHY4") ? 1 : C3Constants.MorphMax;

        for (int i = 0; i < totalVerts; i++)
        {
            var v = new PhyVertex(morphMax);
            for (int m = 0; m < morphMax; m++)
                v.Positions[m] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            v.TexCoord = new Vector2(br.ReadSingle(), br.ReadSingle());
            br.ReadBytes(4); // vertex diffuse (always white)
            for (int b = 0; b < C3Constants.BoneMax; b++) v.BoneIndex[b]  = (int)br.ReadUInt32();
            for (int b = 0; b < C3Constants.BoneMax; b++) v.BoneWeight[b] = br.ReadSingle();
            if (chunkTag == "PHY3") br.ReadBytes(12); // normal
            phy.SourceVertices.Add(v);
            phy.OutputVertices.Add(new PhyOutVertex { Position=v.Positions[0], TexCoord=v.TexCoord });
        }

        phy.NormalTriCount = (int)br.ReadUInt32();
        phy.AlphaTriCount  = (int)br.ReadUInt32();
        int totalIdx = (phy.NormalTriCount + phy.AlphaTriCount) * 3;
        for (int i = 0; i < totalIdx; i++) phy.IndexBuffer.Add(br.ReadUInt16());

        uint texLen = br.ReadUInt32();
        byte[] txb  = br.ReadBytes((int)texLen);
        try   { phy.TexName = Encoding.GetEncoding("GBK").GetString(txb).TrimEnd('\0'); }
        catch { phy.TexName = Encoding.ASCII.GetString(txb).TrimEnd('\0'); }

        phy.BBoxMin    = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        phy.BBoxMax    = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        phy.InitMatrix = C3Motion.ReadMatrix(br);
        phy.TexRow     = (int)br.ReadUInt32();

        int ac=(int)br.ReadUInt32(); for(int i=0;i<ac;i++) phy.Key.Alphas.Add(C3Frame.Read(br));
        int dc=(int)br.ReadUInt32(); for(int i=0;i<dc;i++) phy.Key.Draws.Add(C3Frame.Read(br));
        int cc=(int)br.ReadUInt32(); for(int i=0;i<cc;i++) phy.Key.ChangeTexs.Add(C3Frame.Read(br));

        byte[] f1 = br.ReadBytes(4);
        if (Encoding.ASCII.GetString(f1) == "STEP")
            phy.UVStep = new Vector2(br.ReadSingle(), br.ReadSingle());
        else br.BaseStream.Seek(-4, SeekOrigin.Current);

        byte[] f2 = br.ReadBytes(4);
        if (Encoding.ASCII.GetString(f2) == "2SID") phy.TwoSided = true;
        else br.BaseStream.Seek(-4, SeekOrigin.Current);

        return phy;
    }

    // ------------------------------------------------------------------
    public void Calculate()
    {
        if (Motion == null) return;

        var (af, alpha) = Key.ProcessAlpha(Motion.CurrentFrame, Motion.FrameCount);
        if (af) Alpha = alpha;
        var (df, vis) = Key.ProcessDraw(Motion.CurrentFrame);
        if (df) Draw = vis;
        var (tf, tex) = Key.ProcessChangeTex(Motion.CurrentFrame);
        if (!tf) tex = -1;

        if (!Draw) return;

        var bone = new Matrix[Motion.BoneCount];
        for (int b = 0; b < Motion.BoneCount; b++)
            bone[b] = InitMatrix * Motion.GetBoneMatrix(b) * Motion.BoneMatrix[b];

        _accumUV += UVStep;
        float seg = TexRow > 0 ? 1f / TexRow : 1f;

        for (int v = 0; v < SourceVertices.Count; v++)
        {
            var sv = SourceVertices[v];
            Vector3 pos = Vector3.Zero;
            for (int l = 0; l < C3Constants.BoneMax; l++)
                if (sv.BoneWeight[l] > 0f)
                { pos = Vector3.Transform(sv.Positions[0], bone[sv.BoneIndex[l]]); break; }

            OutputVertices[v].Position = pos;
            OutputVertices[v].TexCoord = tex > -1
                ? new Vector2(sv.TexCoord.X + (tex % TexRow) * seg, sv.TexCoord.Y + (tex / TexRow) * seg)
                : sv.TexCoord + _accumUV;
        }
    }

    public VertexPositionColorTexture[] BuildGpuVertices()
    {
        var verts = new VertexPositionColorTexture[OutputVertices.Count];
        for (int i = 0; i < OutputVertices.Count; i++)
            verts[i] = new VertexPositionColorTexture(
                OutputVertices[i].Position, Color.White, OutputVertices[i].TexCoord);
        return verts;
    }

    public int TotalVertexCount => NormalVertexCount + AlphaVertexCount;
    public int TotalIndexCount  => (NormalTriCount + AlphaTriCount) * 3;
    public int AlphaIndexStart  => NormalTriCount * 3;
}
