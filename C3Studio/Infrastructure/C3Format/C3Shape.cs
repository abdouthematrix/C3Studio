using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public class C3SMotion
{
    
    public Matrix[]? Frames { get; set; }
    public int CurrentFrame { get; set; }
    public Matrix Matrix { get; set; } = Matrix.Identity;
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
    { if (FrameCount > 0) CurrentFrame = (CurrentFrame + step) % FrameCount; }

    public void SetFrame(int frame) { CurrentFrame = frame; }
    public void ClearMatrix() { Matrix = Matrix.Identity; }
    public void Multiply(Matrix m) { Matrix = Matrix * m; }

    /// <summary>
    /// Returns the world-space matrix for the current frame.
    /// When <paramref name="applyLocal"/> is true, LocalMatrix is post-multiplied
    /// (C++ equivalent: frame * lpSMotion->matrix).
    /// </summary>
    public Matrix GetWorldMatrix(bool applyLocal = true)
    {
        if (Frames == null || Frames.Length == 0) return Matrix.Identity;
        Matrix fm = Frames[Math.Clamp(CurrentFrame, 0, Frames.Length - 1)];
        return applyLocal ? fm * Matrix : fm;
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
/// Each instance owns an <see cref="AlphaTestEffect"/> created lazily on the first
/// <see cref="Draw"/> call.  Blend mode is driven by <see cref="BlendAsb"/> /
/// <see cref="BlendAdb"/> via <see cref="C3BlendHelper.Resolve"/>.
///
/// The smooth sub-step count is hardcoded to 10, mirroring the C++ constant
/// override inside Shape_SetSegment (the parameter there is silently ignored).
/// </summary>
public class C3Shape : IDisposable
{
    

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
    private int _segCount;
    private int _segCur;
    private bool _isFirst = true;

    // Mirrors C++ dwSmooth — hardcoded to 10 (Shape_SetSegment ignores its param).
    private const int SMOOTH = 10;

    private Vector3 _lastA, _lastB;

    // Per-instance effect — created lazily on first Draw.
    private AlphaTestEffect? _effect;

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
                line.Points[v] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
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
    /// Expands <paramref name="seg"/> by <c>(SMOOTH + 1)</c>, mirroring C++
    /// Shape_SetSegment which also ignores the smooth parameter and hard-overrides it to 10.
    /// </summary>
    public void SetSegment(int seg)
    {
        _segCount = seg * (SMOOTH + 1);
        _segCur = 0;
        _isFirst = true;
        _vb = new ShapeOutVertex[_segCount * 6];
    }

    // ── Per-frame update ──────────────────────────────────────────────────
    /// <summary>
    /// Advances the ribbon by one frame.
    /// Matches C++ Shape_Draw logic (update portion):
    ///   bLocal=false → GetWorldMatrix(applyLocal:true)  = frame * LocalMatrix
    ///   bLocal=true  → GetWorldMatrix(applyLocal:false) = frame only
    /// </summary>
    public void Update(bool bLocal = false)
    {
        if (Motion == null || Lines == null || Lines.Length == 0 || _vb == null) return;

        Matrix mm = Motion.GetWorldMatrix(applyLocal: !bLocal);
        Vector3 vecA = Vector3.Transform(Lines[0].Points![0], mm);
        Vector3 vecB = Vector3.Transform(
            Lines[0].Points!.Length > 1 ? Lines[0].Points[1] : Lines[0].Points[0], mm);

        if (_isFirst)
        {
            Array.Clear(_vb, 0, _vb.Length);
            _isFirst = false;
        }
        else
        {
            float len = Vector3.Distance(vecA, vecB);
            Vector3 prevA = _lastA, prevB = _lastB;

            for (int nn = 0; nn < SMOOTH; nn++)
            {
                float t = (nn + 1f) / (SMOOTH + 1f);
                Vector3 sA = Vector3.Lerp(_lastA, vecA, t);
                Vector3 sB = Vector3.Lerp(_lastB, vecB, t);

                float lnow = Vector3.Distance(sA, sB);
                if (lnow > 0.0001f) sA = Vector3.Lerp(sB, sA, len / lnow);

                WriteSegment(sA, sB, prevA, prevB);
                prevA = sA;
                prevB = sB;
            }

            WriteSegment(vecA, vecB, prevA, prevB);
            UpdateUVs();
        }

        _lastA = vecA;
        _lastB = vecB;
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    /// <summary>
    /// Draws the accumulated ribbon geometry using the per-instance
    /// <see cref="AlphaTestEffect"/> (created lazily on the first call).
    /// Blend state from <see cref="C3BlendHelper.Resolve"/> using own D3D factors.
    /// </summary>
    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection, Matrix world, bool bLocal = false)
    {
        if (_vb == null || _segCount == 0) return;

        // Lazy effect creation — no GraphicsDevice available at Load() time.
        _effect ??= new AlphaTestEffect(gd)
        {
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 8,
        };

        var tex = C3Texture.Get(TexIndex)?.Texture;

        gd.BlendState = C3BlendHelper.Resolve(BlendAsb, BlendAdb);
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = bLocal ? (Motion?.Matrix ?? world) : world;
        _effect.Texture = tex;
        _effect.VertexColorEnabled = true;

        int total = _segCount * 6;
        var gpu = new VertexPositionColorTexture[total];
        for (int i = 0; i < total; i++)
            gpu[i] = new VertexPositionColorTexture(_vb[i].Position, _vb[i].Color, _vb[i].UV);

        foreach (var pass in _effect.CurrentTechnique.Passes)
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
    /// Writes one quad (6 vertices) into the ring buffer at <c>_segCur</c>.
    /// Vertex layout mirrors C++ exactly:
    ///   [0] current A   [1] current B
    ///   [2] previous B  [3] previous A
    ///   [4] previous B  [5] current A
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
    /// Assigns UV coordinates to every segment in the ring, newest → oldest,
    /// U running from 0.95 down to ~0.05.  Matches C++ UV loop exactly.
    /// </summary>
    private void UpdateUVs()
    {
        if (_vb == null) return;
        float uvStep = 0.9f / _segCount;
        float u = _segCount * uvStep + 0.05f;   // starts at 0.95

        for (int n = _segCur - 1; n >= 0; n--) { SetSegmentUV(n, u, uvStep); u -= uvStep; }
        for (int n = _segCount - 1; n > _segCur; n--) { SetSegmentUV(n, u, uvStep); u -= uvStep; }
    }

    /// <summary>
    /// Assigns one UV pair to a segment's six vertices.
    /// Vertex mapping:
    ///   [0][1][5] → (u, 0/1/0) current edge
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

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;

        if (TexIndex != -1) { C3Texture.Texture_Unload(TexIndex); TexIndex = -1; }
    }
}