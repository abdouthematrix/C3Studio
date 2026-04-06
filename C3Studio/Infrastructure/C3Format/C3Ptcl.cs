using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public class PtclFrame
{
    public Vector3[]? Positions;
    public float[]? Ages;
    public float[]? Sizes;
    public Matrix FrameMatrix;
    public int Count => Positions?.Length ?? 0;
}

/// <summary>
/// Pre-baked particle system (PTCL chunk). Renders view-aligned billboards.
/// Blend mode is driven by <see cref="BlendAsb"/>/<see cref="BlendAdb"/>
/// (D3D9 D3DBLEND_* constants, same values used by C3Renderer.ResolveBlendState).
/// Pass an explicit <paramref name="blendOverride"/> to <see cref="Draw"/> only
/// when you need to force a specific state (e.g. during a render-to-texture pass).
/// </summary>
public class C3Ptcl : IDisposable
{
    public int PartIndex = -1;

    // D3D blend factors: 5=SrcAlpha, 6=InvSrcAlpha (standard AlphaBlend).
    // Common alternatives: 2/2 = additive, 5/2 = soft-additive glow.
    public int BlendAsb { get; set; } = 5;
    public int BlendAdb { get; set; } = 6;

    public string Name { get; set; } = string.Empty;
    public string TexName { get; set; } = string.Empty;
    public int TexIndex { get; set; } = -1;
    public int TexRow { get; set; } = 1;
    public int MaxCount { get; set; }

    public PtclFrame[]? Frames { get; set; }
    public int CurrentFrame { get; set; }
    public Matrix LocalMatrix { get; set; } = Matrix.Identity;

    // Both buffers are allocated together in Load(); never null after that.
    private VertexPositionColorTexture[]? _vb;
    private short[]? _ib;

    // ── Blend-state cache shared across all instances ─────────────────────
    // BlendState is a GPU resource; never re-create it every frame.
    private static readonly Dictionary<(int, int), BlendState> _blendCache = new();

    // ── Serialisation ─────────────────────────────────────────────────────
    public static C3Ptcl Load(BinaryReader br)
    {
        var p = new C3Ptcl();

        uint nameLen = br.ReadUInt32();
        p.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

        uint texLen = br.ReadUInt32();
        p.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');

        p.TexRow = (int)br.ReadUInt32();
        p.MaxCount = (int)br.ReadUInt32();

        // Allocate CPU buffers once — sized to the maximum particle count.
        p._vb = new VertexPositionColorTexture[p.MaxCount * 4];
        p._ib = new short[p.MaxCount * 6];

        uint frameCount = br.ReadUInt32();
        p.Frames = new PtclFrame[frameCount];

        for (int n = 0; n < (int)frameCount; n++)
        {
            var frame = new PtclFrame();
            uint count = br.ReadUInt32();
            if (count > 0)
            {
                frame.Positions = new Vector3[count];
                for (int i = 0; i < (int)count; i++)
                    frame.Positions[i] = new Vector3(
                        br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                frame.Ages = new float[count];
                for (int i = 0; i < (int)count; i++) frame.Ages[i] = br.ReadSingle();

                frame.Sizes = new float[count];
                for (int i = 0; i < (int)count; i++) frame.Sizes[i] = br.ReadSingle();

                frame.FrameMatrix = C3Motion.ReadMatrix(br);
            }
            p.Frames[n] = frame;
        }
        return p;
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    /// <summary>
    /// Draws the current particle frame as view-aligned billboards.
    /// Blend state is resolved from <see cref="BlendAsb"/>/<see cref="BlendAdb"/>
    /// unless <paramref name="blendOverride"/> is provided.
    /// </summary>
    public void Draw(GraphicsDevice gd, AlphaTestEffect effect,
                     Matrix view, Matrix projection,
                     BlendState? blendOverride = null)
    {
        if (Frames == null || _vb == null || _ib == null) return;

        var frame = Frames[CurrentFrame];
        if (frame.Count == 0) return;

        var tex = C3Texture.Get(TexIndex)?.Texture;
        int segCount = TexRow * TexRow;
        float segSize = 1f / TexRow;

        // Transform: frame local → world (LocalMatrix) → camera view.
        // Equivalent to C++: inv = frame->matrix * lpPtcl->matrix * g_ViewMatrix.
        Matrix xform = frame.FrameMatrix * LocalMatrix * view;

        for (int n = 0; n < frame.Count; n++)
        {
            // Clamp tile index so Ages slightly above 1.0 don't index OOB.
            // (C++ casts to DWORD without clamping — this is a safe improvement.)
            int tileIdx = Math.Clamp((int)(frame.Ages![n] * segCount), 0, segCount - 1);
            float u = (tileIdx % TexRow) * segSize;
            float v = (tileIdx / TexRow) * segSize;

            Vector3 vpos = Vector3.Transform(frame.Positions![n], xform);
            float s = frame.Sizes![n];

            // Billboard quad — same vertex layout as C++ PtclVertex array.
            // vb[n*4+0] = bottom-left,  vb[n*4+1] = bottom-right
            // vb[n*4+2] = top-left,     vb[n*4+3] = top-right
            _vb[n * 4 + 0] = new VertexPositionColorTexture(
                new Vector3(vpos.X - s, vpos.Y - s, vpos.Z), Color.White,
                new Vector2(u, v + segSize));
            _vb[n * 4 + 1] = new VertexPositionColorTexture(
                new Vector3(vpos.X + s, vpos.Y - s, vpos.Z), Color.White,
                new Vector2(u + segSize, v + segSize));
            _vb[n * 4 + 2] = new VertexPositionColorTexture(
                new Vector3(vpos.X - s, vpos.Y + s, vpos.Z), Color.White,
                new Vector2(u, v));
            _vb[n * 4 + 3] = new VertexPositionColorTexture(
                new Vector3(vpos.X + s, vpos.Y + s, vpos.Z), Color.White,
                new Vector2(u + segSize, v));

            // Two triangles per quad: (0,1,2) and (2,1,3)
            _ib[n * 6 + 0] = (short)(n * 4);
            _ib[n * 6 + 1] = (short)(n * 4 + 1);
            _ib[n * 6 + 2] = (short)(n * 4 + 2);
            _ib[n * 6 + 3] = (short)(n * 4 + 2);
            _ib[n * 6 + 4] = (short)(n * 4 + 1);
            _ib[n * 6 + 5] = (short)(n * 4 + 3);
        }

        // The C++ billboard trick:  set World = invView while the D3D View matrix
        // is still active → invView × View = Identity, so the GPU just projects
        // the pre-view-space positions directly.
        //
        // In MonoGame we override effect.View = Identity, so there is NO View
        // matrix in the pipeline to cancel against.  Setting World = invView
        // would apply an extra invView pass, corrupting every position.
        // The correct equivalent is simply World = Identity:
        //   GPU transform = Identity × Identity × Projection × view_space_pos
        //                 = Projection × view_space_pos   ✓
        //
        // (The old code set World = invView, which accidentally showed particles
        // when LocalMatrix was Identity but broke them once WorldCorrection was
        // applied — because invView × Projection × WorldCorrection ≠ anything sane.)
        gd.BlendState = blendOverride ?? ResolveBlendState(BlendAsb, BlendAdb);
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        effect.View = Matrix.Identity;
        effect.Projection = projection;
        effect.World = Matrix.Identity;   // ← was Matrix.Invert(view), which is wrong in MonoGame
        effect.Texture = tex;
        effect.VertexColorEnabled = true;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vb, 0, frame.Count * 4,
                _ib, 0, frame.Count * 2);
        }
    }

    // ── Animation control ─────────────────────────────────────────────────
    public void NextFrame(int step = 1)
    {
        if (Frames != null && Frames.Length > 0)
            CurrentFrame = (CurrentFrame + step) % Frames.Length;
    }

    public void SetFrame(int frame)
    {
        if (Frames != null && Frames.Length > 0)
            CurrentFrame = frame % Frames.Length;
    }

    // ── Blend helpers ─────────────────────────────────────────────────────
    /// <summary>
    /// Maps a pair of D3DBLEND_* integer constants to a (cached) MonoGame BlendState.
    /// Mirrors C3Renderer.ResolveBlendState; kept here so C3Ptcl can be self-contained.
    /// D3DBLEND values: 1=ZERO 2=ONE 5=SRCALPHA 6=INVSRCALPHA (most common).
    /// </summary>
    private static BlendState ResolveBlendState(int asb, int adb)
    {
        var key = (asb, adb);
        if (_blendCache.TryGetValue(key, out var cached)) return cached;

        var src = D3dBlend(asb);
        var dst = D3dBlend(adb);

        // Reuse the built-in singleton for the most common case.
        if (src == Blend.SourceAlpha && dst == Blend.InverseSourceAlpha)
            return _blendCache[key] = BlendState.AlphaBlend;

        var bs = new BlendState
        {
            ColorSourceBlend = src,
            ColorDestinationBlend = dst,
            AlphaSourceBlend = src,
            AlphaDestinationBlend = dst,
        };
        return _blendCache[key] = bs;
    }

    private static Blend D3dBlend(int d3d) => d3d switch
    {
        1 => Blend.Zero,
        2 => Blend.One,
        3 => Blend.SourceColor,
        4 => Blend.InverseSourceColor,
        5 => Blend.SourceAlpha,
        6 => Blend.InverseSourceAlpha,
        7 => Blend.DestinationAlpha,
        8 => Blend.InverseDestinationAlpha,
        9 => Blend.DestinationColor,
        10 => Blend.InverseDestinationColor,
        11 => Blend.SourceAlphaSaturation,
        _ => Blend.One,   // unknown → safe fallback (matches C++ default)
    };

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        if (TexIndex != -1)
        {
            C3Texture.Texture_Unload(TexIndex);
            TexIndex = -1;
        }
    }
}