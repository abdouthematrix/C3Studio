using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>Point-light definition stored in a OMNI chunk.</summary>
public class C3Omni
{
    public string  Name        { get; set; } = string.Empty;
    public Vector3 Position    { get; set; }
    public Vector3 Color       { get; set; }
    public float   Radius      { get; set; } = 10f;
    public float   Attenuation { get; set; } = 1f;
}
