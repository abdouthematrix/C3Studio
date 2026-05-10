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
    internal const int MaxPhys = 16;
    internal const int MaxMotions = 16;
    public List<C3Phy> Phys { get; } = new();
    public List<C3Motion> Motions { get; } = new();
    public List<C3Omni> Omnis { get; } = new();
    public List<C3Ptcl> Ptcls { get; } = new();
    public List<C3Scene> Scenes { get; } = new();
    public List<C3Shape> Shapes { get; } = new();

    internal readonly List<C3SMotion> _pendingMotions = new();

    /// <summary>Raised when a PHY slot is replaced. Arg = slot index.</summary>
    public event Action<int>? PhyReplaced;

    // ------------------------------------------------------------------
    public static C3Model Load(string filePath, GraphicsDevice? gd = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"C3 file not found: {filePath}");
        using var fs = File.OpenRead(filePath);
        var model = LoadFromStream(fs, gd);
        return model;
    }

    public static C3Model LoadFromStream(Stream stream, GraphicsDevice? gd = null)
    {
        var model = new C3Model();
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
                    model.Omnis.Add(C3Omni.Load(br));
                    break;

                case "PTCX":
                case "PTC3":
                case "PTCL3":
                    {
                        var ptcl = C3Ptcl.Load(br, chunk.Tag);
                        model.Ptcls.Add(ptcl);
                    }
                    break;
                case "PTCL":
                    {
                        var ptcl = C3Ptcl.Load(br, chunk.Tag);
                        model.Ptcls.Add(ptcl);
                    }
                    break;

                case "SCEN":
                    model.Scenes.Add(C3Scene.Load(br));
                    break;

                case "SHAP":
                    model.Shapes.Add(C3Shape.Load(br));
                    break;

                case "SMOT":
                    model._pendingMotions.Add(C3SMotion.Load(br));
                    break;
                    
                case "CAME":
                    stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
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
    
    //// ------------------------------------------------------------------
    //public bool ReplacePhy(string targetName, string sourcePath,
    //                       string? sourceMeshName  = null,
    //                       string? texturePath     = null,
    //                       bool    useSourceMotion = false)
    //{
    //    if (!File.Exists(sourcePath)) return false;
    //    C3Model src;
    //    try { src = Load(sourcePath, loadTextures: false); } catch { return false; }
    //    return ReplacePhyFromModel(targetName, src, sourceMeshName, texturePath, useSourceMotion);
    //}

    //public bool ReplacePhyFromModel(string targetName, C3Model sourceModel,
    //                                string? sourceMeshName  = null,
    //                                string? texturePath     = null,
    //                                bool    useSourceMotion = false)
    //{
    //    int slot = Phys.FindIndex(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
    //    if (slot == -1) return false;
    //    int srcIdx = sourceMeshName == null ? 0
    //        : sourceModel.Phys.FindIndex(p => string.Equals(p.Name, sourceMeshName, StringComparison.OrdinalIgnoreCase));
    //    if (srcIdx < 0 || srcIdx >= sourceModel.Phys.Count) return false;

    //    var newPhy = sourceModel.Phys[srcIdx];
    //    newPhy.Name = targetName;
    //    if (texturePath != null) newPhy.TexIndex = C3Texture.Texture_Load(texturePath);

    //    C3Motion? oldMotion = Phys[slot].Motion;
    //    if (!useSourceMotion || srcIdx >= sourceModel.Motions.Count)
    //    {
    //        newPhy.Motion = oldMotion;
    //    }
    //    else
    //    {
    //        var newMotion = sourceModel.Motions[srcIdx];
    //        newPhy.Motion = newMotion;
    //        if (oldMotion != null)
    //        {
    //            Matrix combined = oldMotion.GetBoneMatrix(0)
    //                            * (oldMotion.BoneMatrix.Count > 0 ? oldMotion.BoneMatrix[0] : Matrix.Identity);
    //            for (int n = 0; n < newMotion.BoneMatrix.Count; n++)
    //                newMotion.BoneMatrix[n] = combined;
    //        }
    //    }

    //    Phys[slot] = newPhy;
    //    newPhy.Calculate();
    //    PhyReplaced?.Invoke(slot);
    //    return true;
    //}

    //// ------------------------------------------------------------------
    public C3Phy? FindPhy(string name) =>
        Phys.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsPlaceholder(string name)
    { var p = FindPhy(name); return p == null || p.TotalIndexCount == 0; }

    public void ChangeMotion(string motionFilePath, Matrix rotationMatrix)
    {
        using var fs = File.OpenRead(motionFilePath);
        ChangeMotion(fs, rotationMatrix);
    }

    // New — used by the renderer when a stream is already open
    public void ChangeMotion(Stream stream, Matrix rotationMatrix, int index=0)
    {
        if (index == -1)
        {
            Motions.Clear();
            foreach (var phy in Phys) phy.Motion = null;

            var src = LoadFromStream(stream);
            if (src.Motions.Count == 0) return;

            // For merged multi-part models, Phys.Count is a multiple of the
            // per-part motion count (e.g. 14 phys / 7 motions = 2 parts).
            // Tile the incoming motions so every phy slot gets a binding.
            int perPart = src.Motions.Count;
            for (int i = 0; i < Phys.Count; i++)
                Motions.Add(src.Motions[i % perPart]);
        }
        else
        {
            var src = LoadFromStream(stream);
            if (src.Motions.Count == 0) return;

            for (int i = 0; i < Phys.Count; i++)
            {
                if (Phys[i].PartIndex == index)
                    Phys[i].Motion = src.Motions[i];
            }
        }
       

        BindPhyMotions(this, rotationMatrix);
    }

    public void AdvanceFrame(int step = 1)
    {
        foreach (var p in Phys) p.Motion?.NextFrame(step);
        foreach (var p in Ptcls) p.NextFrame(step);
        foreach (var s in Scenes) s.NextFrame(step);
        foreach (var s in Shapes) s.NextFrame(step);
    }

    public void SetFrame(int frame)
    {
        foreach (var p in Phys) p.Motion?.SetFrame(frame);
        foreach (var p in Ptcls) p.SetFrame(frame);
        foreach (var s in Shapes) s.SetFrame(frame);
    }

    public void Calculate() { foreach (var p in Phys) p.Calculate(); }
    public void UpdateShapes(bool b = false) { foreach (var s in Shapes) s.Update(b); }

    public int MaxFrameCount
    {
        get
        {
            int m = 0;

            // Include standard motions
            foreach (var mo in Motions)
                if (mo.FrameCount > m) m = mo.FrameCount;
            // Include shape motions
            foreach (var s in Shapes)
                if (s.Motion?.FrameCount > m) m = s.Motion.FrameCount;

            foreach (var p in Ptcls)
                if (p.Frames?.Length > m) m = p.Frames.Length;

            foreach (var s in Scenes)
                if (s.Frames?.Length > m) m = s.Frames.Length;
            return m;
        }
    }

    internal static void BindPhyMotions(C3Model model, Matrix? rotation = null)
    {
        for (int i = 0; i < model.Phys.Count; i++)
        {
            if (i < model.Motions.Count)
                model.Phys[i].Motion = model.Motions[i];
            else
            {
                var stub = new C3Motion { BoneCount = 1, FrameCount = 1 };
                stub.BoneMatrix.Add(Matrix.Identity);
                stub.KeyFrames.Add(new C3KeyFrame { Pos = 0, BoneMatrices = { Matrix.Identity } });
                model.Phys[i].Motion = stub;
            }
            if (rotation.HasValue)
            { model.Phys[i].Motion!.ClearMatrix(); model.Phys[i].Motion!.Multiply(-1, rotation.Value); }
        }
    }
}
