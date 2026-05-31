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

    // ── Frame state (delegated to Body) ───────────────────────────────────
    public int MaxFrameCount => Body?.MaxFrameCount ?? 0;
    public int CurrentFrame => Body?.CurrentFrame ?? 0;

    private static readonly IReadOnlyDictionary<string, string> SlotSocketMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ARMET",   "v_armet"    },
            { "RWEAPON", "v_r_weapon" },
            { "LWEAPON", "v_l_weapon" },
            { "MOUNT",   "v_mount"    },
            { "MANTLE",  "v_mantle"   },
            { "CAPE",    "v_back"     },
            { "MISC",    "v_misc"     },
            { "PELVIS",  "v_pelvis"   },
            { "SPIRIT",  "v_rootloc"  },
        };

    // ── Per-frame update ──────────────────────────────────────────────────
    public void AdvanceFrame(int step)
    {
        Body?.AdvanceFrame(step);
        foreach (var p in AllParts()) p.AdvanceFrame(step);
    }

    public void SetFrame(int frame)
    {
        Body?.SetFrame(frame);
        foreach (var p in AllParts()) p.SetFrame(frame);
    }

    public void Calculate()
    {
        // 1. Calculate main skeleton first
        Body?.Calculate();

        // 2. Bind attachments to current frame
        BindAllParts();

        // 3. Calculate attachments
        foreach (var p in AllParts()) p.Calculate();
    }

    public void UpdateShapes()
    {
        Body?.UpdateShapes();
        foreach (var p in AllParts()) p.UpdateShapes();
    }

    public void Update()
    {
        Body?.Update();
        foreach (var p in AllParts()) p.Update();
    }

    public void UploadAllVertices()
    {
        Body?.UploadVertices();
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
        Body?.Initialize(gd);
        foreach (var p in AllParts()) p.Initialize(gd);
    }

    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection)
    {
        Body?.Draw(gd, view, projection);
        foreach (var p in AllParts()) p.Draw(gd, view, projection);
    }

    public IEnumerable<string> GetPhyNames()
    {
        if (Body != null) foreach (var n in Body.GetPhyNames()) yield return n;
        foreach (var p in AllParts()) foreach (var n in p.GetPhyNames()) yield return n;
    }

    public bool GetPhyVisibility(string name)
    {
        if (Body != null && Body.GetPhyNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            return Body.GetPhyVisibility(name);

        foreach (var p in AllParts())
            if (p.GetPhyNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                return p.GetPhyVisibility(name);

        return true;
    }

    public void SetPhyVisibility(string name, bool visible)
    {
        Body?.SetPhyVisibility(name, visible);
        foreach (var p in AllParts()) p.SetPhyVisibility(name, visible);
    }

    // ── Slot assignment helpers ───────────────────────────────────────────
    public C3RolePart? GetSlot(string partname)
    {
        return partname.ToUpperInvariant() switch
        {
            "BODY" or "ARMOR" => Body,  // <-- Added ARMOR alias
            "ARMET" => Armet,
            "RWEAPON" => RWeapon,
            "LWEAPON" => LWeapon,
            "MOUNT" => Mount,
            "MANTLE" => Mantle,
            "CAPE" => Cape,
            "MISC" => Misc,
            "PELVIS" => Pelvis,
            "SPIRIT" => Spirit,
            _ => null,
        };
    }

    public void AssignSlot(C3RolePart part)
    {
        switch (part.SlotName.ToUpperInvariant())
        {
            case "BODY":
            case "ARMOR": Body = part; break; // <-- Added ARMOR alias
            case "ARMET": Armet = part; break;
            case "RWEAPON": RWeapon = part; break;
            case "LWEAPON": LWeapon = part; break;
            case "MOUNT": Mount = part; break;
            case "MANTLE": Mantle = part; break;
            case "CAPE": Cape = part; break;
            case "MISC": Misc = part; break;
            case "PELVIS": Pelvis = part; break;
            case "SPIRIT": Spirit = part; break;
        }
    }

    public void ClearSlot(string partname)
    {
        switch (partname.ToUpperInvariant())
        {
            case "BODY":
            case "ARMOR": Body = null; break; // <-- Added ARMOR alias
            case "ARMET": Armet = null; break;
            case "RWEAPON": RWeapon = null; break;
            case "LWEAPON": LWeapon = null; break;
            case "MOUNT": Mount = null; break;
            case "MANTLE": Mantle = null; break;
            case "CAPE": Cape = null; break;
            case "MISC": Misc = null; break;
            case "PELVIS": Pelvis = null; break;
            case "SPIRIT": Spirit = null; break;
        }
    }

    // ── Socket binding ────────────────────────────────────────────────────
    public void BindRolePart(C3RolePart? part, string socketName)
    {
        if (part == null) return;
        part.SetVirtualMotion(Body?.GetVirtualMotion(socketName));
    }

    public void BindAllParts()
    {
        BindRolePart(Armet, "v_armet");
        BindRolePart(RWeapon, "v_r_weapon");
        BindRolePart(LWeapon, "v_l_weapon");
        BindRolePart(Mount, "v_mount");
        BindRolePart(Mantle, "v_mantle");
        BindRolePart(Cape, "v_back");
        BindRolePart(Misc, "v_misc");
        BindRolePart(Pelvis, "v_pelvis");
        BindRolePart(Spirit, "v_rootloc");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public void Dispose()
    {
        Body?.Dispose();
        foreach (var p in AllParts()) p.Dispose();
    }

    // ── Iteration ─────────────────────────────────────────────────────────
    public IEnumerable<C3RolePart> AllParts()
    {
        if (Armet != null) yield return Armet;
        if (RWeapon != null) yield return RWeapon;
        if (LWeapon != null) yield return LWeapon;
        if (Mount != null) yield return Mount;
        if (Mantle != null) yield return Mantle;
        if (Cape != null) yield return Cape;
        if (Misc != null) yield return Misc;
        if (Pelvis != null) yield return Pelvis;
        if (Spirit != null) yield return Spirit;
    }
}