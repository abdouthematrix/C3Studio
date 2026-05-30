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
///
/// Each instance owns an <see cref="AlphaTestEffect"/> created on the first
/// <see cref="Draw"/> call (lazy, because Load() has no GraphicsDevice).
/// Blend mode is driven by <see cref="BlendAsb"/>/<see cref="BlendAdb"/>
/// via <see cref="C3BlendHelper.Resolve"/> (D3D9 D3DBLEND_* constants).
/// </summary>
public class C3Ptcl : IDisposable
{
    public int PartIndex = -1;

    // D3D blend factors: 5=SrcAlpha, 6=InvSrcAlpha (standard AlphaBlend).
    // Defaulting to 5/2 (Soft Additive) for particles since Conquer effects
    // heavily rely on additive glow that hides black-background textures.
    public int BlendAsb { get; set; } = 5;
    public int BlendAdb { get; set; } = 2;

    public string Name { get; set; } = string.Empty;
    public string TexName { get; set; } = string.Empty;
    public int TexIndex { get; set; } = -1;
    public int TexRow { get; set; } = 1;
    public int MaxCount { get; set; }

    public PtclFrame[]? Frames { get; set; }
    public int CurrentFrame { get; set; }
    public Matrix LocalMatrix { get; set; } = Matrix.Identity;

    // PTC3 specific fields
    public bool IsPTC3 { get; set; }
    public float FadeStartAge { get; set; }
    public float FadeEndAge { get; set; }
    public float TotalLifetime { get; set; }
    public float MaxAlpha { get; set; }
    public float MinAlpha { get; set; }
    public float GlobalAlpha { get; set; } = 1f;
    public float InitialAlpha { get; set; }
    public float RotationSpeed { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float ScaleZ { get; set; } = 1f;

    // CPU buffers — allocated once in Load(), sized to MaxCount.
    private VertexPositionColorTexture[]? _vb;
    private short[]? _ib;

    // Per-instance effect — created lazily on first Draw.
    private AlphaTestEffect? _effect;

    // ── Serialisation ─────────────────────────────────────────────────────
    public static C3Ptcl Load(BinaryReader br, string tag = "PTCL")
    {
        var p = new C3Ptcl();
        p.IsPTC3 = (tag == "PTC3" || tag == "PTCL3" || tag == "PTCX");

        uint nameLen = br.ReadUInt32();
        p.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

        uint texLen = br.ReadUInt32();
        p.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');

        p.TexRow = (int)br.ReadUInt32();

        if (p.IsPTC3)
        {
            br.ReadByte();    // field_68
            br.ReadByte();    // field_69
            br.ReadUInt32();  // field_6C
            br.ReadUInt32();  // field_70
            p.ScaleX = br.ReadSingle();                          // field_74
            p.ScaleY = br.ReadSingle();                          // field_78
            p.ScaleZ = br.ReadSingle();                          // field_7C
            br.ReadUInt32();  // field_80
            br.ReadUInt32();  // field_84
            p.RotationSpeed = br.ReadSingle() * 180f / MathF.PI;       // field_88 → degrees
            p.MaxAlpha = br.ReadSingle();                          // field_8C
            p.MinAlpha = br.ReadSingle();                          // field_90
            p.TotalLifetime = br.ReadSingle();                          // field_94
            p.FadeStartAge = br.ReadSingle();                          // field_98
            p.FadeEndAge = p.TotalLifetime * 0.8f;
        }

        p.MaxCount = (int)br.ReadUInt32();

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
                if (p.IsPTC3) br.ReadBytes((int)count * 2); // skip lpIndices

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
    /// The per-instance <see cref="AlphaTestEffect"/> is created on the first call.
    /// Blend state is resolved via <see cref="C3BlendHelper"/> unless
    /// <paramref name="blendOverride"/> is provided.
    /// </summary>
    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection, Matrix world,
                     BlendState? blendOverride = null)
    {
        if (Frames == null || _vb == null || _ib == null) return;

        var frame = Frames[CurrentFrame];
        if (frame.Count == 0) return;

        // Lazy effect creation — no GraphicsDevice available at Load() time.
        _effect ??= new AlphaTestEffect(gd)
        {
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 8,
        };

        var tex = C3Texture.Get(TexIndex)?.Texture;
        int segCount = TexRow * TexRow;
        float segSize = 1f / TexRow;

        // frame local → world (LocalMatrix) → camera view
        Matrix xform = frame.FrameMatrix * LocalMatrix * view;

        for (int n = 0; n < frame.Count; n++)
        {
            float age = frame.Ages![n];
            int tileIdx = Math.Clamp((int)(age * segCount), 0, segCount - 1);
            float u = (tileIdx % TexRow) * segSize;
            float v = (tileIdx / TexRow) * segSize;

            Vector3 vpos = Vector3.Transform(frame.Positions![n], xform);
            float s = frame.Sizes![n];

            Vector3 rotatedRight = new Vector3(s, 0, 0);
            Vector3 rotatedUp = new Vector3(0, s, 0);
            Color color = Color.White;

            if (IsPTC3)
            {
                float rx = s * ScaleX;
                float ry = s * ScaleY;

                float angle = age * RotationSpeed * MathF.PI / 180f;
                float cosRot = MathF.Cos(angle);
                float sinRot = MathF.Sin(angle);

                rotatedRight = new Vector3(rx * cosRot, rx * sinRot, 0);
                rotatedUp = new Vector3(-ry * sinRot, ry * cosRot, 0);

                float alpha;
                if (age >= FadeStartAge)
                {
                    if (age < FadeEndAge)
                    {
                        alpha = MaxAlpha;
                    }
                    else
                    {
                        float range = TotalLifetime - FadeEndAge;
                        alpha = range > 0f
                            ? (1f - (age - FadeEndAge) / range) * MaxAlpha + ((age - FadeEndAge) / range) * MinAlpha
                            : MinAlpha;
                    }
                }
                else
                {
                    alpha = FadeStartAge > 0f
                        ? (1f - age / FadeStartAge) * InitialAlpha + (age / FadeStartAge) * MaxAlpha
                        : MaxAlpha;
                }

                alpha = Math.Clamp(alpha * GlobalAlpha, 0f, 1f);
                color = new Color(255, 255, 255, (int)(alpha * 255));
            }

            // Billboard quad — same layout as the C++ PtclVertex array.
            _vb[n * 4 + 0] = new VertexPositionColorTexture(
                new Vector3(vpos.X - rotatedRight.X - rotatedUp.X, vpos.Y - rotatedRight.Y - rotatedUp.Y, vpos.Z),
                color, new Vector2(u, v + segSize));
            _vb[n * 4 + 1] = new VertexPositionColorTexture(
                new Vector3(vpos.X + rotatedRight.X - rotatedUp.X, vpos.Y + rotatedRight.Y - rotatedUp.Y, vpos.Z),
                color, new Vector2(u + segSize, v + segSize));
            _vb[n * 4 + 2] = new VertexPositionColorTexture(
                new Vector3(vpos.X - rotatedRight.X + rotatedUp.X, vpos.Y - rotatedRight.Y + rotatedUp.Y, vpos.Z),
                color, new Vector2(u, v));
            _vb[n * 4 + 3] = new VertexPositionColorTexture(
                new Vector3(vpos.X + rotatedRight.X + rotatedUp.X, vpos.Y + rotatedRight.Y + rotatedUp.Y, vpos.Z),
                color, new Vector2(u + segSize, v));

            _ib[n * 6 + 0] = (short)(n * 4);
            _ib[n * 6 + 1] = (short)(n * 4 + 1);
            _ib[n * 6 + 2] = (short)(n * 4 + 2);
            _ib[n * 6 + 3] = (short)(n * 4 + 2);
            _ib[n * 6 + 4] = (short)(n * 4 + 1);
            _ib[n * 6 + 5] = (short)(n * 4 + 3);
        }

        // Particles are already in view space — World = Identity, no extra View pass.
        gd.BlendState = blendOverride ?? C3BlendHelper.Resolve(BlendAsb, BlendAdb);
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        _effect.View = Matrix.Identity;
        _effect.Projection = projection;
        _effect.World = world;
        _effect.Texture = tex;
        _effect.VertexColorEnabled = true;

        foreach (var pass in _effect.CurrentTechnique.Passes)
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

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;

        if (TexIndex != -1) { C3Texture.Texture_Unload(TexIndex); TexIndex = -1; }
    }
}