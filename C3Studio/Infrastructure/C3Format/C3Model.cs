using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Documents;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>
/// Top-level .c3 loader. Handles all chunk types.
/// Partial class – stream loading lives in C3Model_StreamExtensions.cs.
/// </summary>
public partial class C3Model
{
    // ── World transform applied to every loaded model ─────────────────────
    private static readonly Matrix WorldCorrection =
        Matrix.CreateRotationX(MathHelper.ToRadians(90f));
    internal const string ExpectedVersion = "MAXFILE C3 00001";
    internal const int MaxPhys = 32;
    internal const int MaxMotions = 32;
    public List<C3Phy> Phys { get; } = new();
    public List<C3Motion> Motions { get; } = new();
    public List<C3Omni> Omnis { get; } = new();
    public List<C3Ptcl> Ptcls { get; } = new();
    public List<C3Scene> Scenes { get; } = new();
    public List<C3Shape> Shapes { get; } = new();

    public List<C3SMotion> SMotions = new();

    public Matrix World { get; set; } = Matrix.Identity;
    // ------------------------------------------------------------------
    private readonly Dictionary<string, bool> _meshVisibility = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DefaultHiddenSlots = new(
     [
        "V_ARMET_EFFECT01",
        "V_ARMET_EFFECT02",
        "v_armet",
        "v_back",
        "v_extend1",
        "v_extend10",
        "v_extend11",
        "v_extend12",
        "v_extend13",
        "v_extend14",
        "v_extend15",
        "v_extend2",
        "v_extend3",
        "v_extend4",
        "v_extend5",
        "v_extend6",
        "v_extend7",
        "v_extend8",
        "v_extend9",
        "v_hair",
        "v_head",
        "v_hit",
        "v_l_arm",
        "v_l_flap",
        "v_l_foot",
        "v_l_forearm",
        "v_l_leg",
        "v_l_shield",
        "v_l_shoulder",
        "v_l_slot01",
        "v_l_slot02",
        "v_l_weapon",
        "v_mantle",
        "v_misc",
        "v_mount",
        "v_mount_01",
        "v_pelvis",
        "v_pet",
        "v_r_arm",
        "v_r_flap",
        "v_r_foot",
        "v_r_forearm",
        "v_r_leg",
        "v_r_shield",
        "v_r_shoulder",
        "v_r_slot01",
        "v_r_slot02",
        "v_r_weapon",
        "v_rootloc",
        "v_shell",
        "v_slot",
        "v_wsocket1",
        "v_wsocket2",
        "v_wsocket3",
        "v_zero",
        "v_l_shoe",
        "v_r_shoe"
     ],
     StringComparer.OrdinalIgnoreCase);
    private bool IsMeshVisible(C3Phy phy) =>
    _meshVisibility.TryGetValue(phy.Name, out bool visible) ? visible : true;
    public IEnumerable<string> GetPhyNames() => _meshVisibility.Keys;
    public bool GetPhyVisibility(string name) => _meshVisibility.TryGetValue(name, out bool v) ? v : true;
    public void SetPhyVisibility(string name, bool visible) => _meshVisibility[name] = visible;
    // ------------------------------------------------------------------
    public void Initialize(GraphicsDevice gd)
    {
        // Populate the visibility dictionary with discovered slots
        _meshVisibility.Clear();
        foreach (var phy in Phys)
        {
            if (!_meshVisibility.ContainsKey(phy.Name))
            {
                // Default them to hidden if they match the original blacklist
                _meshVisibility[phy.Name] = !DefaultHiddenSlots.Contains(phy.Name);
            }
            phy.InitializeGPU(gd);
            phy.GpuTexture = C3Texture.Get(phy.TexIndex)?.Texture;
        }
        foreach (var scene in Scenes) scene.UploadGPU(gd);
    }
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
                case "PHY5":
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
                    model.SMotions.Add(C3SMotion.Load(br));
                    break;
                    
                case "CAME":
                    stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;
                case "CCFL":
                    stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;
                case "MNEW":
                    stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;                
                default:
                    Debug.WriteLine($"Unknown chunk tag '{chunk.Tag}' at position {stream.Position - 8}. Skipping.");
                    stream.Seek(chunk.ChunkSize, SeekOrigin.Current);
                    break;
            }

            if (stream.Position > chunkEnd) stream.Seek(chunkEnd, SeekOrigin.Begin);
        }

        model.BindPhyMotions();
        model.BindShapeMotions();
        model.ApplyWorldRotation(worldRotation: WorldCorrection);
        return model;
    }
    public void BindPhyMotions()
    {
        for (int i = 0; i < Phys.Count; i++)        
            if (i < Motions.Count)            
                Phys[i].Motion = Motions[i]; 
    }
    public void BindShapeMotions()
    {
        for (int i = 0; i < Shapes.Count; i++)        
            if (i < SMotions.Count)            
                Shapes[i].Motion = SMotions[i]; 
    }
    public C3Phy? FindPhy(string name) =>
        Phys.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    public C3Motion GetVirtualMotion(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        // Returns null if FindPhy(name) is null; otherwise returns phy.Motion
        return FindPhy(name)?.Motion;
    }
    public void SetVirtualMotion(C3Motion pMotion)
    {
        // 1. Safe Null/Bound Checking
        if (pMotion == null ||
            Phys == null ||
            Phys.Count == 0)        
            return;
        
        var targetPhy = Phys.FirstOrDefault();
        if (targetPhy == null ||
            targetPhy?.Motion == null)        
            return;
        // Cache the motion reference to avoid nested pointer-chasing in the loop
        var targetMotion = targetPhy.Motion;
        var dwBoneCount = targetMotion.BoneCount;

        // 2. Bone Matrix Transformation Loop
        for (int n = 0; n < dwBoneCount; n++)
        {            
            var mm = pMotion.GetBoneMatrix(0);
           
            targetMotion.BoneMatrix[n] = mm;
            targetMotion.BoneMatrix[n] *= pMotion.BoneMatrix[0];
        }

        //// 3. Update Position Coords for all Mesh Parts
        //uint dwPhyNum = m_infoPart.p3DMesh.m_dwPhyNum;
        //for (uint n = 0; n < dwPhyNum; n++)
        //{
        //    // M41, M42, M43 represent X, Y, Z translation components in a 4x4 matrix
        //    m_infoPart.p3DMesh.m_x[n] = pMotion.matrix[0].M41;
        //    m_infoPart.p3DMesh.m_y[n] = pMotion.matrix[0].M42;
        //    m_infoPart.p3DMesh.m_z[n] = pMotion.matrix[0].M43;
        //}
    }    
    public void ChangeMotion(Stream stream, int index=-1)
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

            for (int i = 0; i < Motions.Count; i++)
                Motions[i] = src.Motions[i];
        }
        this.BindPhyMotions();
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
    private void ApplyWorldRotation(Matrix? worldRotation)
    {
        if (!worldRotation.HasValue) return;
        var rot = worldRotation.Value;

        foreach (var phy in Phys)
            if (phy.Motion != null)
            { phy.ClearMatrix(); phy.Multiply(-1, rot); }

        foreach (var scene in Scenes)
            scene.ExtraMatrix = scene.ExtraMatrix * rot;

        foreach (var ptc in Ptcls)
            ptc.LocalMatrix = ptc.LocalMatrix * rot;

        foreach (var shape in Shapes)
            if (shape.Motion != null)
            { shape.Motion.ClearMatrix(); shape.Motion.Multiply(rot); }

        foreach (var Motion in Motions)
        {
            //ClearMatrix()
            for (int n = 0; n < Motion.BoneCount; n++)
                Motion.BoneMatrix[n] = Matrix.Identity;
            //Multiply
            for (int n = 0; n < Motion.BoneCount; n++)
                Motion.BoneMatrix[n] = Motion.BoneMatrix[n] * rot;
        }
        foreach (var sMotion in SMotions)
        {
            sMotion.ClearMatrix();
            sMotion.Multiply(rot);
        }
    }
    public void Update()
    {
        foreach (var phy in Phys)
            if (phy.Draw && IsMeshVisible(phy)) phy.UploadVertices();
    }
    public void Draw(GraphicsDevice _gd, Matrix view, Matrix projection)
    {
        _gd.SamplerStates[0] = SamplerState.LinearWrap;
        DrawScene(_gd, view, projection);
        DrawPhy(_gd, view, projection);
        DrawPtcl(_gd, view, projection);
        DrawShape(_gd, view, projection);
    }
    private void DrawScene(GraphicsDevice _gd, Matrix view, Matrix projection)
    {
        foreach (var scene in Scenes)
            scene.Draw(_gd, view, projection);
    }
    private void DrawPhy(GraphicsDevice _gd, Matrix view, Matrix projection)
    {
        if (Phys.Count == 0) return;

        // ── Opaque pass ────────────────────────────────────────────────────
        foreach (var phy in Phys)
        {
            if (!IsMeshVisible(phy)) continue;
            phy.DrawNormal(_gd, view, projection, World);
        }

        // ── Alpha / semi-transparent pass ──────────────────────────────────
        foreach (var phy in Phys)
        {
            if (!IsMeshVisible(phy)) continue;
            phy.DrawAlpha(_gd, view, projection, World, bZ: false);
        }
    }
    private void DrawPtcl(GraphicsDevice _gd, Matrix view, Matrix projection)
    {
        foreach (var p in Ptcls)
            p.Draw(_gd, view, projection, World);
    }
    private void DrawShape(GraphicsDevice _gd, Matrix view, Matrix projection)
    {
        foreach (var s in Shapes)
            s.Draw(_gd, view, projection, World);
    }
    public void UploadAllPhyVertices()
    {
        foreach (var phy in Phys)
            if (phy.Draw) phy.UploadVertices();
    }
    public void Unload()
    {
        foreach (var phy in Phys) phy.Dispose();
        foreach (var scene in Scenes) scene.Dispose();
        foreach (var ptcl in Ptcls) ptcl.Dispose();
        foreach (var shape in Shapes) shape.Dispose();
    }
}
