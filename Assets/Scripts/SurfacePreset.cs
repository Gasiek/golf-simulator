using UnityEngine;

/// <summary>
/// ScriptableObject preset for ground surface properties.
/// Create presets via Assets > Create > Golf > Surface Preset
/// </summary>
[CreateAssetMenu(fileName = "Surface", menuName = "Golf/Surface Preset", order = 2)]
public class SurfacePreset : ScriptableObject
{
    [Header("Surface Identity")]
    [Tooltip("Display name for this surface type")]
    public string displayName = "Fairway";

    [Header("Bounce Properties")]
    [Range(0f, 1f)]
    [Tooltip(
        "Coefficient of restitution - how much energy is retained on bounce (0 = dead, 1 = perfect bounce)"
    )]
    public float bounceCOR = 0.5f;

    [Range(0f, 1f)]
    [Tooltip(
        "How much horizontal speed is retained on bounce (0 = stops horizontal, 1 = full skip)"
    )]
    public float bounceHorizontalRetention = 0.6f;

    [Range(0f, 1f)]
    [Tooltip("How much spin is retained per bounce (0 = kills spin, 1 = keeps spin)")]
    public float bounceSpinRetention = 0.5f;

    [Header("Roll Properties")]
    [Min(0f)]
    [Tooltip("Friction deceleration in m/s² (higher = stops faster)")]
    public float rollFriction = 1.5f;

    [Range(0f, 2f)]
    [Tooltip("Roll speed multiplier (1 = normal, <1 = slower like rough, >1 = faster like wet)")]
    public float rollSpeedMultiplier = 1.0f;

    [Min(0.01f)]
    [Tooltip("Minimum speed before ball stops (m/s)")]
    public float stopThreshold = 0.05f;

    [Header("Special Behavior")]
    [Tooltip("Ball stops immediately (like water hazard)")]
    public bool instantStop = false;

    [Tooltip("Ball is out of bounds")]
    public bool outOfBounds = false;
}
