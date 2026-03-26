using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>
/// Top-level .c3 loader. Handles all chunk types.
/// Partial class – stream loading lives in C3Model_StreamExtensions.cs.
/// </summary>
public partial class C3Model
{
    internal const string ExpectedVersion = "MAXFILE C3 00001";
    internal const int    MaxPhys         = 16;
    internal const int    MaxMotions       = 16;

    public string SourcePath { get; set; } = string.Empty;

    public List<C3Phy>    Phys    { get; } = new();
    public List<C3Motion> Motions { get; } = new();
    public List<C3Omni>   Omnis   { get; } = new();
    public List<C3Ptcl>   Ptcls   { get; } = new();
    public List<C3Scene>  Scenes  { get; } = new();
    public List<C3Shape>  Shapes  { get; } = new();

    internal readonly List<C3SMotion> _pendingMotions = new();

    /// <summary>Raised when a PHY slot is replaced. Arg = slot index.</summary>
    public event Action<int>? PhyReplaced;

    // ------------------------------------------------------------------
    public static C3Model Load(string filePath,
                               bool loadTextures = true,
                               GraphicsDevice? gd = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"C3 file not found: {filePath}");
        using var fs    = File.OpenRead(filePath);
        var model       = LoadFromStream(fs, loadTextures, gd);
        model.SourcePath = filePath;
        return model;
    }

    public static C3Model LoadFromStream(Stream stream,
                                     bool loadTextures = true,
                                     GraphicsDevice? gd = null)
    {
        var model = new C3Model { SourcePath = "<stream>" };
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        string version = Encoding.ASCII.GetString(br.ReadBytes(16)).TrimEnd('\0');
        if (version != ExpectedVersion)
            throw new InvalidDataException(
                $"Unsupported C3 version '{version}'. Expected '{ExpectedVersion}'.");

        long fileSize = stream.Length;
        while (stream.Position < fileSize)
        {
            if (fileSize - stream.Position < 8) break;
            var chunk = ChunkHeader.Read(br);
            long chunkEnd = stream.Position + chunk.ChunkSize;

            switch (chunk.Tag)
            {
                case "PHY ":
                case "PHY3":
                case "PHY4":
                    if (model.Phys.Count < MaxPhys)
                        model.Phys.Add(C3Phy.Load(br, chunk.Tag));
                    else stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;

                case "MOTI":
                    if (model.Motions.Count < MaxMotions)
                        model.Motions.Add(C3Motion.Load(br));
                    else stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;

                case "OMNI":
                    {
                        var o = new C3Omni();
                        uint nl = br.ReadUInt32();
                        o.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nl)).TrimEnd('\0');
                        o.Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        o.Color = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        model.Omnis.Add(o);
                        break;
                    }

                case "PTCL":
                    model.Ptcls.Add(C3Ptcl.Load(br, loadTextures));
                    break;

                case "SCEN":
                    {
                        var sc = ReadSceneBlock(br, loadTextures, gd);
                        if (sc != null) model.Scenes.Add(sc);
                        break;
                    }

                case "SHAP":
                    model.Shapes.Add(C3Shape.Load(br, loadTextures));
                    break;

                case "SMOT":
                    model._pendingMotions.Add(C3SMotion.Load(br));
                    break;

                default:
                    stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;
            }

            if (stream.Position > chunkEnd) stream.Seek(chunkEnd, SeekOrigin.Begin);
        }

        BindPhyMotions(model);

        for (int i = 0; i < model.Shapes.Count; i++)
            if (i < model._pendingMotions.Count)
                model.Shapes[i].Motion = model._pendingMotions[i];

        return model;
    }

    // ------------------------------------------------------------------
    public bool ReplacePhy(string targetName, string sourcePath,
                           string? sourceMeshName  = null,
                           string? texturePath     = null,
                           bool    useSourceMotion = false)
    {
        if (!File.Exists(sourcePath)) return false;
        C3Model src;
        try { src = Load(sourcePath, loadTextures: false); } catch { return false; }
        return ReplacePhyFromModel(targetName, src, sourceMeshName, texturePath, useSourceMotion);
    }

    public bool ReplacePhyFromModel(string targetName, C3Model sourceModel,
                                    string? sourceMeshName  = null,
                                    string? texturePath     = null,
                                    bool    useSourceMotion = false)
    {
        int slot = Phys.FindIndex(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (slot == -1) return false;
        int srcIdx = sourceMeshName == null ? 0
            : sourceModel.Phys.FindIndex(p => string.Equals(p.Name, sourceMeshName, StringComparison.OrdinalIgnoreCase));
        if (srcIdx < 0 || srcIdx >= sourceModel.Phys.Count) return false;

        var newPhy = sourceModel.Phys[srcIdx];
        newPhy.Name = targetName;
        if (texturePath != null) newPhy.TexIndex = C3Texture.Texture_Load(texturePath);

        C3Motion? oldMotion = Phys[slot].Motion;
        if (!useSourceMotion || srcIdx >= sourceModel.Motions.Count)
        {
            newPhy.Motion = oldMotion;
        }
        else
        {
            var newMotion = sourceModel.Motions[srcIdx];
            newPhy.Motion = newMotion;
            if (oldMotion != null)
            {
                Matrix combined = oldMotion.GetBoneMatrix(0)
                                * (oldMotion.BoneMatrix.Count > 0 ? oldMotion.BoneMatrix[0] : Matrix.Identity);
                for (int n = 0; n < newMotion.BoneMatrix.Count; n++)
                    newMotion.BoneMatrix[n] = combined;
            }
        }

        Phys[slot] = newPhy;
        newPhy.Calculate();
        PhyReplaced?.Invoke(slot);
        return true;
    }

    // ------------------------------------------------------------------
    public C3Phy? FindPhy(string name) =>
        Phys.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsPlaceholder(string name)
    { var p = FindPhy(name); return p == null || p.TotalIndexCount == 0; }

    public void ChangeMotion(string motionFilePath, Matrix rotationMatrix)
    {
        Motions.Clear();
        foreach (var phy in Phys) phy.Motion = null;
        var src = Load(motionFilePath);
        foreach (var m in src.Motions) Motions.Add(m);
        BindPhyMotions(this, rotationMatrix);
    }

    public void AdvanceFrame(int step=1)
    {
        foreach (var p in Phys)   p.Motion?.NextFrame(step);
        foreach (var p in Ptcls)  p.NextFrame(step);
        foreach (var s in Scenes) s.NextFrame(step);
        foreach (var s in Shapes) s.NextFrame(step);
    }

    public void SetFrame(int frame)
    {
        foreach (var p in Phys)   p.Motion?.SetFrame(frame);
        foreach (var p in Ptcls)  p.SetFrame(frame);
        foreach (var s in Shapes) s.SetFrame(frame);
    }

    public void Calculate()                { foreach (var p in Phys)   p.Calculate(); }
    public void UpdateShapes(bool b=false) { foreach (var s in Shapes) s.Update(b); }

    public int MaxFrameCount
    { get { int m=0; foreach(var mo in Motions) if(mo.FrameCount>m) m=mo.FrameCount; return m; } }

    // ------------------------------------------------------------------
    internal static C3Scene? ReadSceneBlock(BinaryReader br,
                                            bool loadTextures, GraphicsDevice? gd)
    {
        var scene  = new C3Scene();
        uint nLen  = br.ReadUInt32();
        scene.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nLen)).TrimEnd('\0');

        uint vecCount = br.ReadUInt32();
        scene.Vertices = new SceneVertex[vecCount];
        for (int i=0;i<(int)vecCount;i++)
        {
            scene.Vertices[i].Position=new Vector3(br.ReadSingle(),br.ReadSingle(),br.ReadSingle());
            scene.Vertices[i].Normal  =new Vector3(br.ReadSingle(),br.ReadSingle(),br.ReadSingle());
            scene.Vertices[i].UV0     =new Vector2(br.ReadSingle(),br.ReadSingle());
            scene.Vertices[i].UV1     =new Vector2(br.ReadSingle(),br.ReadSingle());
        }

        uint triCount = br.ReadUInt32();
        scene.Indices = new ushort[triCount*3];
        for (int i=0;i<(int)(triCount*3);i++) scene.Indices[i]=br.ReadUInt16();

        uint texLen  = br.ReadUInt32();
        scene.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');
        if (loadTextures) scene.TexIndex = C3Texture.Texture_Load(scene.TexName);

        uint lmLen = br.ReadUInt32();
        if (lmLen > 0)
        {
            scene.LightTexName = Encoding.ASCII.GetString(br.ReadBytes((int)lmLen)).TrimEnd('\0');
            if (loadTextures) scene.LightTexIndex = C3Texture.Texture_Load(scene.LightTexName);
        }

        uint fc = br.ReadUInt32();
        scene.Frames = new Matrix[fc];
        for (int i=0;i<(int)fc;i++) scene.Frames[i]=C3Motion.ReadMatrix(br);

        if (gd != null) scene.UploadGPU(gd);
        return scene;
    }

    internal static void BindPhyMotions(C3Model model, Matrix? rotation = null)
    {
        for (int i = 0; i < model.Phys.Count; i++)
        {
            if (i < model.Motions.Count)
                model.Phys[i].Motion = model.Motions[i];
            else
            {
                var stub = new C3Motion { BoneCount=1, FrameCount=1 };
                stub.BoneMatrix.Add(Matrix.Identity);
                stub.KeyFrames.Add(new C3KeyFrame { Pos=0, BoneMatrices={Matrix.Identity} });
                model.Phys[i].Motion = stub;
            }
            if (rotation.HasValue)
            { model.Phys[i].Motion!.ClearMatrix(); model.Phys[i].Motion!.Multiply(-1, rotation.Value); }
        }
    }
}
