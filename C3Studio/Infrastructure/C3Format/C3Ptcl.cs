using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public class PtclFrame
{
    public Vector3[]? Positions;
    public float[]?   Ages;
    public float[]?   Sizes;
    public Matrix     FrameMatrix;
    public int Count => Positions?.Length ?? 0;
}

/// <summary>Pre-baked particle system (PTCL chunk). Renders view-aligned billboards.</summary>
public class C3Ptcl : IDisposable
{
    public string Name     { get; set; } = string.Empty;
    public string TexName  { get; set; } = string.Empty;
    public int    TexIndex { get; set; } = -1;
    public int    TexRow   { get; set; } = 1;
    public int    MaxCount { get; set; }

    public PtclFrame[]? Frames       { get; set; }
    public int          CurrentFrame { get; set; }
    public Matrix       LocalMatrix  { get; set; } = Matrix.Identity;

    private VertexPositionColorTexture[]? _vb;
    private short[]?                      _ib;

    public static C3Ptcl Load(BinaryReader br, bool loadTexture = true)
    {
        var p = new C3Ptcl();
        uint nameLen = br.ReadUInt32();
        p.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');
        uint texLen = br.ReadUInt32();
        p.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');
        if (loadTexture) p.TexIndex = C3Texture.Texture_Load(p.TexName);
        p.TexRow   = (int)br.ReadUInt32();
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
                frame.Positions = new Vector3[count];
                for (int i = 0; i < (int)count; i++)
                    frame.Positions[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                frame.Ages  = new float[count]; for (int i=0;i<(int)count;i++) frame.Ages[i]  = br.ReadSingle();
                frame.Sizes = new float[count]; for (int i=0;i<(int)count;i++) frame.Sizes[i] = br.ReadSingle();
                frame.FrameMatrix = C3Motion.ReadMatrix(br);
            }
            p.Frames[n] = frame;
        }
        return p;
    }

    public void Draw(GraphicsDevice gd, BasicEffect effect,
                     Matrix view, Matrix projection, BlendState? blend = null)
    {
        if (Frames == null || _vb == null || _ib == null) return;
        var frame = Frames[CurrentFrame];
        if (frame.Count == 0) return;
        var tex       = C3Texture.Get(TexIndex)?.Texture;
        int segCount  = TexRow * TexRow;
        float segSize = 1f / TexRow;
        Matrix xform  = frame.FrameMatrix * LocalMatrix * view;
        for (int n = 0; n < frame.Count; n++)
        {
            int tileIdx = Math.Clamp((int)(frame.Ages![n] * segCount), 0, segCount - 1);
            float u = (tileIdx % TexRow) * segSize, v = (tileIdx / TexRow) * segSize;
            Vector3 vpos = Vector3.Transform(frame.Positions![n], xform);
            float s = frame.Sizes![n];
            _vb[n*4+0]=new VertexPositionColorTexture(new Vector3(vpos.X-s,vpos.Y-s,vpos.Z),Color.White,new Vector2(u,v+segSize));
            _vb[n*4+1]=new VertexPositionColorTexture(new Vector3(vpos.X+s,vpos.Y-s,vpos.Z),Color.White,new Vector2(u+segSize,v+segSize));
            _vb[n*4+2]=new VertexPositionColorTexture(new Vector3(vpos.X-s,vpos.Y+s,vpos.Z),Color.White,new Vector2(u,v));
            _vb[n*4+3]=new VertexPositionColorTexture(new Vector3(vpos.X+s,vpos.Y+s,vpos.Z),Color.White,new Vector2(u+segSize,v));
            _ib[n*6+0]=(short)(n*4);   _ib[n*6+1]=(short)(n*4+1);
            _ib[n*6+2]=(short)(n*4+2); _ib[n*6+3]=(short)(n*4+2);
            _ib[n*6+4]=(short)(n*4+1); _ib[n*6+5]=(short)(n*4+3);
        }
        Matrix invView = Matrix.Invert(view);
        gd.BlendState=blend??BlendState.AlphaBlend;
        gd.DepthStencilState=DepthStencilState.DepthRead;
        gd.RasterizerState=RasterizerState.CullNone;
        gd.SamplerStates[0]=SamplerState.LinearWrap;
        effect.View=Matrix.Identity; effect.Projection=projection; effect.World=invView;
        effect.TextureEnabled=tex!=null; effect.Texture=tex;
        effect.VertexColorEnabled=true; effect.LightingEnabled=false;
        foreach (var pass in effect.CurrentTechnique.Passes)
        { pass.Apply(); gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,_vb,0,frame.Count*4,_ib,0,frame.Count*2); }
    }

    public void NextFrame(int step=1) { if(Frames!=null&&Frames.Length>0) CurrentFrame=(CurrentFrame+step)%Frames.Length; }
    public void SetFrame(int frame)   { if(Frames!=null&&Frames.Length>0) CurrentFrame=frame%Frames.Length; }

    public void Dispose()
    { if(TexIndex!=-1){C3Texture.Texture_Unload(TexIndex);TexIndex=-1;} }
}
