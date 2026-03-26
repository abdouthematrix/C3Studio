using Microsoft.Xna.Framework;
using System;

namespace C3Studio.Rendering;

/// <summary>
/// Simple spherical-coordinate orbit camera.
/// Yaw + Pitch → target position, configurable radius, perspective projection.
/// </summary>
public sealed class OrbitCamera
{
    // Orbit state
    private float _yaw = MathHelper.Pi;   // start facing -Z
    private float _pitch = -0.4f;            // slight downward tilt
    private float _radius = 150f;

    // Pan offset
    private Vector3 _target = Vector3.Zero;

    // Limits
    private const float MinRadius = 20f;
    private const float MaxRadius = 1200f;
    private const float MinPitch = -MathHelper.PiOver2 + 0.05f;
    private const float MaxPitch = MathHelper.PiOver2 - 0.05f;

    public float FieldOfView { get; set; } = MathHelper.ToRadians(45f);
    public float NearPlane { get; set; } = 1f;
    public float FarPlane { get; set; } = 10000f;

    // ── Input deltas (called each frame from WpfGame.Update) ─────────────
    public void Orbit(float deltaYaw, float deltaPitch)
    {
        _yaw += deltaYaw;
        _pitch = Math.Clamp(_pitch + deltaPitch, MinPitch, MaxPitch);
    }

    public void Pan(float deltaX, float deltaY)
    {
        // Move target in camera-right and camera-up directions
        var right = Vector3.Cross(Forward, Vector3.Up);
        right.Normalize();
        var up = Vector3.Cross(right, Forward);
        up.Normalize();
        _target += right * (-deltaX * _radius * 0.001f)
                 + up * (deltaY * _radius * 0.001f);
    }

    public void Zoom(float delta) =>
        _radius = Math.Clamp(_radius - delta * _radius * 0.1f, MinRadius, MaxRadius);

    public void Reset(float modelRadius = 100f)
    {
        _yaw = MathHelper.Pi;
        _pitch = -0.35f;
        _radius = modelRadius * 2.5f;
        _target = Vector3.Zero;
    }

    // ── Derived matrices ──────────────────────────────────────────────────
    private Vector3 Forward =>
        new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw));

    public Vector3 Position => _target - Forward * _radius;

    public Matrix View =>
        Matrix.CreateLookAt(Position, _target, Vector3.Up);

    public Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(
            FieldOfView, aspectRatio, NearPlane, FarPlane);

    // Expose radius so ViewerGame can auto-fit the model
    public float Radius { get => _radius; set => _radius = Math.Clamp(value, MinRadius, MaxRadius); }
    public Vector3 Target { get => _target; set => _target = value; }
}









