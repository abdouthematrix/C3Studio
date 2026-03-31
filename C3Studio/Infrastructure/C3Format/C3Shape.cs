using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public class C3SMotion
{
    public Matrix[]? Frames       { get; set; }
    public int       CurrentFrame { get; set; }
    public Matrix    LocalMatrix  { get; set; } = Matrix.Identity;
    public int FrameCount => Frames?.Length ?? 0;

    public static C3SMotion Load(BinaryReader br)
    {
        var m = new C3SMotion();
        int count = (int)br.ReadUInt32();
        m.Frames = new Matrix[count];
        for (int i = 0; i < count; i++) m.Frames[i] = C3Motion.ReadMatrix(br);
        return m;
    }

    public void NextFrame(int step=1) { if(FrameCount>0) CurrentFrame=(CurrentFrame+step)%FrameCount; }
    public void SetFrame(int frame)   { CurrentFrame=frame; }
    public void ClearMatrix()         { LocalMatrix=Matrix.Identity; }
    public void Multiply(Matrix m)    { LocalMatrix=LocalMatrix*m; }

    public Matrix GetWorldMatrix(bool applyLocal=true)
    {
        if (Frames==null||Frames.Length==0) return Matrix.Identity;
        Matrix fm=Frames[Math.Clamp(CurrentFrame,0,Frames.Length-1)];
        return applyLocal?fm*LocalMatrix:fm;
    }
}

public class C3Line { public Vector3[]? Points { get; set; } }

public struct ShapeOutVertex
{
    public Vector3 Position;
    public Color   Color;
    public Vector2 UV;
}

/// <summary>Animated ribbon/blade trail (SHAP chunk). Ring-buffer of interpolated quad segments.</summary>
public class C3Shape : IDisposable
{
    public string   Name     { get; set; } = string.Empty;
    public string   TexName  { get; set; } = string.Empty;
    public int      TexIndex { get; set; } = -1;
    public C3Line[]?  Lines  { get; set; }
    public C3SMotion? Motion { get; set; }

    private ShapeOutVertex[]? _vb;
    private int  _segCount;
    private int  _segCur;
    private bool _isFirst = true;
    private const int SMOOTH = 10;
    private Vector3 _lastA, _lastB;

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

    public void SetSegment(int seg)
    {
        _segCount = seg * (SMOOTH + 1);
        _segCur   = 0;
        _vb       = new ShapeOutVertex[_segCount * 6];
    }

    public void Update(bool bLocal = false)
    {
        if (Motion==null||Lines==null||Lines.Length==0||_vb==null) return;
        Matrix mm = Motion.GetWorldMatrix(!bLocal);
        Vector3 vecA = Vector3.Transform(Lines[0].Points![0], mm);
        Vector3 vecB = Vector3.Transform(Lines[0].Points!.Length > 1 ? Lines[0].Points[1] : Lines[0].Points[0], mm);
        if (_isFirst) { Array.Clear(_vb,0,_vb.Length); _isFirst=false; }
        else
        {
            float len  = Vector3.Distance(vecA, vecB);
            Vector3 prevA=_lastA, prevB=_lastB;
            for (int nn=0;nn<SMOOTH;nn++)
            {
                float t=(nn+1f)/(SMOOTH+1f);
                Vector3 sA=Vector3.Lerp(_lastA,vecA,t), sB=Vector3.Lerp(_lastB,vecB,t);
                float lnow=Vector3.Distance(sA,sB);
                if (lnow>0.0001f) sA=Vector3.Lerp(sB,sA,len/lnow);
                WriteSegment(sA,sB,prevA,prevB); prevA=sA; prevB=sB;
            }
            WriteSegment(vecA,vecB,prevA,prevB);
            UpdateUVs();
        }
        _lastA=vecA; _lastB=vecB;
    }

    public void Draw(GraphicsDevice gd, BasicEffect effect,
                     Matrix view, Matrix projection, bool bLocal=false)
    {
        if (_vb==null||_segCount==0) return;
        var tex = C3Texture.Get(TexIndex)?.Texture;
        gd.BlendState=BlendState.AlphaBlend;
        gd.DepthStencilState=DepthStencilState.DepthRead;
        gd.RasterizerState=RasterizerState.CullNone;
        gd.SamplerStates[0]=SamplerState.LinearWrap;
        effect.View=view; effect.Projection=projection;
        effect.World=bLocal?(Motion?.LocalMatrix??Matrix.Identity):Matrix.Identity;
        effect.TextureEnabled=tex!=null; effect.Texture=tex;
        effect.VertexColorEnabled=true; effect.LightingEnabled=false;
        int total=_segCount*6;
        var gpu=new VertexPositionColorTexture[total];
        for (int i=0;i<total;i++)
            gpu[i]=new VertexPositionColorTexture(_vb[i].Position,_vb[i].Color,_vb[i].UV);
        foreach (var pass in effect.CurrentTechnique.Passes)
        { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleList,gpu,0,_segCount*2); }
    }

    public void NextFrame(int step=1) => Motion?.NextFrame(step);
    public void SetFrame(int frame)   => Motion?.SetFrame(frame);

    private void WriteSegment(Vector3 a, Vector3 b, Vector3 prevA, Vector3 prevB)
    {
        if (_vb==null) return;
        int cur=_segCur*6;
        _vb[cur+0]=new ShapeOutVertex{Position=a,    Color=Color.White};
        _vb[cur+1]=new ShapeOutVertex{Position=b,    Color=Color.White};
        _vb[cur+2]=new ShapeOutVertex{Position=prevB,Color=Color.White};
        _vb[cur+3]=new ShapeOutVertex{Position=prevA,Color=Color.White};
        _vb[cur+4]=new ShapeOutVertex{Position=prevB,Color=Color.White};
        _vb[cur+5]=new ShapeOutVertex{Position=a,    Color=Color.White};
        _segCur=(_segCur+1)%_segCount;
    }

    private void UpdateUVs()
    {
        if (_vb==null) return;
        float uvStep=0.9f/_segCount, u=(float)_segCount*uvStep+0.05f;
        for (int n=_segCur-1;n>=0;n--) { SetSegmentUV(n,u,uvStep); u-=uvStep; }
        for (int n=_segCount-1;n>_segCur;n--) { SetSegmentUV(n,u,uvStep); u-=uvStep; }
    }

    private void SetSegmentUV(int seg, float u, float step)
    {
        if (_vb==null) return;
        int b=seg*6;
        _vb[b+0].UV=new Vector2(u,0); _vb[b+1].UV=new Vector2(u,1); _vb[b+5].UV=new Vector2(u,0);
        u-=step;
        _vb[b+2].UV=new Vector2(u,1); _vb[b+3].UV=new Vector2(u,0); _vb[b+4].UV=new Vector2(u,1);
    }

    public void Dispose()
    { if(TexIndex!=-1){C3Texture.Texture_Unload(TexIndex);TexIndex=-1;} }
}
