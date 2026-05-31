using C3Studio.Infrastructure.C3Format;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;

namespace C3Studio.Infrastructure.Rendering;

public sealed class C3Role : IDisposable
{
    // ── Equipment slots ───────────────────────────────────────────────────
    public C3RolePart? Body { get; set; }
    public C3RolePart? Armet { get; set; }
    public C3RolePart? RWeapon { get; set; }
    public C3RolePart? LWeapon { get; set; }
    public C3RolePart? Mount { get; set; }
    public C3RolePart? Mantle { get; set; }
    public C3RolePart? Cape { get; set; }
    public C3RolePart? Misc { get; set; }
    public C3RolePart? Pelvis { get; set; }
    public C3RolePart? Spirit { get; set; }
    // ── Frame state (delegated to Body, then first BodyExtra) ─────────────
    public int MaxFrameCount => Body?.MaxFrameCount ?? 0;
    public int CurrentFrame => Body?.CurrentFrame ?? 0;

    // ── Per-frame update ──────────────────────────────────────────────────
    public void AdvanceFrame(int step)
    {
        foreach (var p in AllParts()) p.AdvanceFrame(step);
    }
    public void SetFrame(int frame)
    {
        foreach (var p in AllParts()) p.SetFrame(frame);
    }
    public void Calculate()
    {
        foreach (var p in AllParts()) p.Calculate();
    }
    public void UpdateShapes()
    {
        foreach (var p in AllParts()) p.UpdateShapes();
    }
    public void Update()
    {
        foreach (var p in AllParts()) p.Update();
    }
    public void UploadAllVertices()
    {
        foreach (var p in AllParts()) p.UploadVertices();
    }
    public void ChangeMotion(Stream stream, string partname = "BODY")
    {
        var part = GetSlot(partname);
        if (part == null) return;
        part.ChangeMotion(stream);       
    }
    public void Initialize(GraphicsDevice gd)
    {
        foreach (var p in AllParts()) p.Initialize(gd);
    }
    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection)
    {
        foreach (var p in AllParts()) p.Draw(gd, view, projection);
    }
    public IEnumerable<string> GetPhyNames()
    {
        foreach (var p in AllParts())
            foreach (var n in p.GetPhyNames())
                yield return n;
    }
    public bool GetPhyVisibility(string name)
    {
        foreach (var p in AllParts())
            if (p.GetPhyNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                return p.GetPhyVisibility(name);
        return true;
    }
    public void SetPhyVisibility(string name, bool visible)
    {
        foreach (var p in AllParts())
            p.SetPhyVisibility(name, visible);
    }

    // ── Slot assignment helper ────────────────────────────────────────────
    public C3RolePart GetSlot(string partname)
    {
        switch (partname)
        {
            case "BODY": return Body;
            case "ARMET": return Armet; 
            case "RWEAPON": return RWeapon; 
            case "LWEAPON": return LWeapon; 
            case "MOUNT": return Mount; 
            case "MANTLE": return Mantle; 
            case "CAPE": return Cape;
            case "MISC": return Misc;
            case "PELVIS": return Pelvis; 
            case "SPIRIT": return Spirit;
        }
        return Body;
    }
    public void AssignSlot(C3RolePart part)
    {
        switch (part.SlotName.ToUpperInvariant())
        {
            case "BODY": Body ??= part; break;
            case "ARMET": Armet ??= part; break;
            case "RWEAPON": RWeapon ??= part; break;
            case "LWEAPON": LWeapon ??= part; break;
            case "MOUNT": Mount ??= part; break;
            case "MANTLE": Mantle ??= part; break;
            case "CAPE": Cape ??= part; break;
            case "MISC": Misc ??= part; break;
            case "PELVIS": Pelvis ??= part; break;
            case "SPIRIT": Spirit ??= part; break;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public void Dispose()
    {
        foreach (var p in AllParts()) p.Dispose();
    }

    // ── Iteration ─────────────────────────────────────────────────────────    
    public IEnumerable<C3RolePart> AllParts()
    {
        if (Armet != null) yield return Armet;
        if (Body != null) yield return Body;
        if (RWeapon != null) yield return RWeapon;
        if (LWeapon != null) yield return LWeapon;
        if (Mount != null) yield return Mount;
        if (Mantle != null) yield return Mantle;
        if (Cape != null) yield return Cape;
        if (Misc != null) yield return Misc;
        if (Pelvis != null) yield return Pelvis;
        if (Spirit != null) yield return Spirit;
    }

    // ── Private ───────────────────────────────────────────────────────────   
    private void BindRolePart(C3RolePart? part, string socketName)
    {
        if (part == null) return;
        part.SetVirtualMotion(Body?.GetVirtualMotion(socketName));
    }
}