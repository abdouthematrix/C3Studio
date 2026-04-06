using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public class C3SMotion
{
    public Matrix[]? Frames { get; set; }
    public int CurrentFrame { get; set; }
    public Matrix LocalMatrix { get; set; } = Matrix.Identity;
    public int FrameCount => Frames?.Length ?? 0;

    public static C3SMotion Load(BinaryReader br)
    {
        var m = new C3SMotion();
        int count = (int)br.ReadUInt32();
        m.Frames = new Matrix[count];
        for (int i = 0; i < count; i++) m.Frames[i] = C3Motion.ReadMatrix(br);
        return m;
    }

    public void NextFrame(int step = 1)
    {
        if (FrameCount > 0) CurrentFrame = (CurrentFrame + step) % FrameCount;
    }

    public void SetFrame(int frame) { CurrentFrame = frame; }
    public void ClearMatrix() { LocalMatrix = Matrix.Identity; }
    public void Multiply(Matrix m) { LocalMatrix = LocalMatrix * m; }

    /// <summary>
    /// Returns the world-space matrix for the current frame.
    /// When <paramref name="applyLocal"/> is true, LocalMatrix is post-multiplied
    /// (C++ equivalent: frame * lpSMotion->matrix).
    /// </summary>
    public Matrix GetWorldMatrix(bool applyLocal = true)
    {
        if (Frames == null || Frames.Length == 0) return Matrix.Identity;
        Matrix fm = Frames[Math.Clamp(CurrentFrame, 0, Frames.Length - 1)];
        return applyLocal ? fm * LocalMatrix : fm;
    }
}

public class C3Line { public Vector3[]? Points { get; set; } }

public struct ShapeOutVertex
{
    public Vector3 Position;
    public Color Color;
    public Vector2 UV;
}

/// <summary>
/// Animated ribbon/blade trail (SHAP chunk). Ring-buffer of interpolated quad segments.
///
/// Blend mode is driven by <see cref="BlendAsb"/>/<see cref="BlendAdb"/>
/// (D3D9 D3DBLEND_* constants), matching C++ Shape_Draw(nAsb, nAdb).
/// The smooth sub-step count is hardcoded to 10, mirroring the C++ constant
/// override inside Shape_SetSegment (the parameter there is silently ignored).
/// </summary>
public class C3Shape : IDisposable
{
    public int PartIndex = -1;

    // D3D blend factors: 5=SrcAlpha, 6=InvSrcAlpha (standard AlphaBlend).
    public int BlendAsb { get; set; } = 5;
    public int BlendAdb { get; set; } = 6;

    public string Name { get; set; } = string.Empty;
    public string TexName { get; set; } = string.Empty;
    public int TexIndex { get; set; } = -1;
    public C3Line[]? Lines { get; set; }
    public C3SMotion? Motion { get; set; }

    // ── Ring-buffer state ─────────────────────────────────────────────────
    private ShapeOutVertex[]? _vb;
    private int _segCount;   // total slot count = rawSegments * (SMOOTH + 1)
    private int _segCur;     // next-write ring position
    private bool _isFirst = true;

    // Mirrors C++ dwSmooth — hardcoded to 10 (Shape_SetSegment ignores its param).
    private const int SMOOTH = 10;

    private Vector3 _lastA, _lastB;

    // ── Blend-state cache shared across all instances ─────────────────────
    private static readonly Dictionary<(int, int), BlendState> _blendCache = new();

    // ── Serialisation ─────────────────────────────────────────────────────
    public static C3Shape Load(BinaryReader br)
    {
        var s = new C3Shape();

        uint nameLen = br.ReadUInt32();
        s.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

        uint lineCount = br.ReadUInt32();
        s.Lines = new C3Line[lineCount];
        for (int n = 0; n < (int)lineCount; n++)
        {
            uint vecCount = br.ReadUInt32();
            var line = new C3Line { Points = new Vector3[vecCount] };
            for (int v = 0; v < (int)vecCount; v++)
                line.Points[v] = new Vector3(
                    br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            s.Lines[n] = line;
        }

        uint texLen = br.ReadUInt32();
        s.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');

        uint seg = br.ReadUInt32();
        s.SetSegment((int)seg);
        return s;
    }

    // ── Segment buffer ────────────────────────────────────────────────────
    /// <summary>
    /// (Re-)allocates the ring buffer.
    /// Expands <paramref name="seg"/> by <c>(SMOOTH + 1)</c>, mirroring
    /// C++ <c>Shape_SetSegment</c> which also ignores the smooth parameter
    /// and hard-overrides it to 10.
    /// Resets the ring cursor and the first-frame flag so stale data from
    /// a previous buffer is never visible.
    /// </summary>
    public void SetSegment(int seg)
    {
        _segCount = seg * (SMOOTH + 1);
        _segCur = 0;
        // FIX: always reset _isFirst so the new (zero-filled) buffer gets a
        // full Array.Clear on the very next Update() call, even if SetSegment
        // is called after the shape has already been used.
        _isFirst = true;
        _vb = new ShapeOutVertex[_segCount * 6];
        // Array is zero-initialised by the CLR; no extra ZeroMemory needed.
    }

    // ── Per-frame update ──────────────────────────────────────────────────
    /// <summary>
    /// Advances the ribbon by one frame.
    /// Matches C++ <c>Shape_Draw</c> logic (update portion):
    ///   • bLocal=false → GetWorldMatrix(applyLocal:true)  = frame * LocalMatrix
    ///   • bLocal=true  → GetWorldMatrix(applyLocal:false) = frame only
    /// </summary>
    public void Update(bool bLocal = false)
    {
        if (Motion == null || Lines == null || Lines.Length == 0 || _vb == null)
            return;

        // Resolve the two blade endpoints in world space.
        Matrix mm = Motion.GetWorldMatrix(applyLocal: !bLocal);
        Vector3 vecA = Vector3.Transform(Lines[0].Points![0], mm);
        // Guard for single-point lines (C++ would read index 1 without checking).
        Vector3 vecB = Vector3.Transform(
            Lines[0].Points!.Length > 1 ? Lines[0].Points[1] : Lines[0].Points[0], mm);

        if (_isFirst)
        {
            // First valid frame: zero the entire buffer and seed the 'last' positions
            // so the first real segment doesn't interpolate from Vector3.Zero.
            Array.Clear(_vb, 0, _vb.Length);
            _isFirst = false;
        }
        else
        {
            // Compute the reference blade length (used to preserve width during lerp).
            float len = Vector3.Distance(vecA, vecB);

            Vector3 prevA = _lastA, prevB = _lastB;

            // SMOOTH sub-steps between the previous and current frame positions,
            // matching C++ loop: nn = 0 .. dwSmooth-1, t = (nn+1)/(dwSmooth+1).
            for (int nn = 0; nn < SMOOTH; nn++)
            {
                float t = (nn + 1f) / (SMOOTH + 1f);
                Vector3 sA = Vector3.Lerp(_lastA, vecA, t);
                Vector3 sB = Vector3.Lerp(_lastB, vecB, t);

                // Preserve blade width: rescale the A endpoint so |sA-sB| == len.
                // Guard against degenerate zero-length segments.
                float lnow = Vector3.Distance(sA, sB);
                if (lnow > 0.0001f)
                    sA = Vector3.Lerp(sB, sA, len / lnow);

                WriteSegment(sA, sB, prevA, prevB);
                prevA = sA;
                prevB = sB;
            }

            // Final segment uses the true current-frame positions.
            WriteSegment(vecA, vecB, prevA, prevB);
            UpdateUVs();
        }

        _lastA = vecA;
        _lastB = vecB;
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    /// <summary>
    /// Draws the accumulated ribbon geometry.
    /// Blend state is resolved from <see cref="BlendAsb"/>/<see cref="BlendAdb"/>.
    /// Matches C++ Shape_Draw blend setup:
    ///   SetRenderState(D3DRS_SRCBLEND, nAsb); SetRenderState(D3DRS_DESTBLEND, nAdb).
    /// </summary>
    public void Draw(GraphicsDevice gd, AlphaTestEffect effect,
                     Matrix view, Matrix projection, bool bLocal = false)
    {
        if (_vb == null || _segCount == 0) return;

        var tex = C3Texture.Get(TexIndex)?.Texture;

        // FIX: resolve blend from own D3D factors instead of always using AlphaBlend.
        gd.BlendState = ResolveBlendState(BlendAsb, BlendAdb);
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        // World matrix: identity for world-space vertices (bLocal=false),
        // LocalMatrix only when bLocal=true (pre-transform was skipped in Update).
        effect.View = view;
        effect.Projection = projection;
        effect.World = bLocal ? (Motion?.LocalMatrix ?? Matrix.Identity) : Matrix.Identity;
        effect.Texture = tex;
        effect.VertexColorEnabled = true;

        int total = _segCount * 6;
        var gpu = new VertexPositionColorTexture[total];
        for (int i = 0; i < total; i++)
            gpu[i] = new VertexPositionColorTexture(
                _vb[i].Position, _vb[i].Color, _vb[i].UV);

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, gpu, 0, _segCount * 2);
        }
    }

    // ── Animation control ─────────────────────────────────────────────────
    public void NextFrame(int step = 1) => Motion?.NextFrame(step);
    public void SetFrame(int frame) => Motion?.SetFrame(frame);

    // ── Private geometry helpers ──────────────────────────────────────────
    /// <summary>
    /// Writes one quad segment (6 vertices) into the ring buffer at <c>_segCur</c>
    /// then advances the cursor, wrapping at <c>_segCount</c>.
    ///
    /// Vertex layout mirrors C++ exactly:
    ///   [0] current A   [1] current B
    ///   [2] previous B  [3] previous A
    ///   [4] previous B  [5] current A   (second triangle shares two edges)
    /// </summary>
    private void WriteSegment(Vector3 a, Vector3 b, Vector3 prevA, Vector3 prevB)
    {
        if (_vb == null) return;
        int cur = _segCur * 6;
        _vb[cur + 0] = new ShapeOutVertex { Position = a, Color = Color.White };
        _vb[cur + 1] = new ShapeOutVertex { Position = b, Color = Color.White };
        _vb[cur + 2] = new ShapeOutVertex { Position = prevB, Color = Color.White };
        _vb[cur + 3] = new ShapeOutVertex { Position = prevA, Color = Color.White };
        _vb[cur + 4] = new ShapeOutVertex { Position = prevB, Color = Color.White };
        _vb[cur + 5] = new ShapeOutVertex { Position = a, Color = Color.White };
        _segCur = (_segCur + 1) % _segCount;
    }

    /// <summary>
    /// Assigns UV coordinates to every segment in the ring, starting from the
    /// most-recently-written slot and walking backwards through the ring.
    /// U runs from 0.95 (newest) down to near 0 (oldest), giving a
    /// fade-along-the-trail effect via an alpha-gradient texture.
    ///
    /// Matches C++ UV loop exactly:
    ///   add = dwSegment; uvstep = 0.9f/add;
    ///   u = add*uvstep + 0.05  (= 0.95 for the newest segment).
    /// </summary>
    private void UpdateUVs()
    {
        if (_vb == null) return;
        float uvStep = 0.9f / _segCount;
        float u = (float)_segCount * uvStep + 0.05f;   // starts at 0.95

        // Walk backwards from the just-written slot (_segCur was already advanced).
        for (int n = _segCur - 1; n >= 0; n--)
        { SetSegmentUV(n, u, uvStep); u -= uvStep; }

        for (int n = _segCount - 1; n > _segCur; n--)
        { SetSegmentUV(n, u, uvStep); u -= uvStep; }
    }

    /// <summary>
    /// Assigns the (u, u-step) UV pair to one segment's six vertices.
    /// Vertex UV mapping:
    ///   [0][1][5] → (u,   0/1/0)   current edge
    ///   [2][3][4] → (u-step, 1/0/1) previous edge
    /// </summary>
    private void SetSegmentUV(int seg, float u, float step)
    {
        if (_vb == null) return;
        int b = seg * 6;
        _vb[b + 0].UV = new Vector2(u, 0);
        _vb[b + 1].UV = new Vector2(u, 1);
        _vb[b + 5].UV = new Vector2(u, 0);
        u -= step;
        _vb[b + 2].UV = new Vector2(u, 1);
        _vb[b + 3].UV = new Vector2(u, 0);
        _vb[b + 4].UV = new Vector2(u, 1);
    }

    // ── Blend helpers ─────────────────────────────────────────────────────
    private static BlendState ResolveBlendState(int asb, int adb)
    {
        var key = (asb, adb);
        if (_blendCache.TryGetValue(key, out var cached)) return cached;

        var src = D3dBlend(asb);
        var dst = D3dBlend(adb);

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
        _ => Blend.One,
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