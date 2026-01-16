using UnityEngine;

/// <summary>
/// ScriptableObject preset for tee height configuration.
/// Create presets via Assets > Create > Golf > Tee Height Preset
/// </summary>
[CreateAssetMenu(fileName = "TeeHeight", menuName = "Golf/Tee Height Preset", order = 1)]
public class TeeHeightPreset : ScriptableObject
{
    [Header("Tee Configuration")]
    [Tooltip("Display name for this tee height")]
    public string displayName = "Ground";

    [Tooltip("Ball center Y position (Ground = ball radius 0.02135)")]
    public float ballY = 0.02135f;

    [Tooltip("Club Z offset to align swing arc with ball position")]
    public float clubZOffset = 0f;

    [Header("Visual (Optional)")]
    [Tooltip("Prefab for the tee visual (null for ground)")]
    public GameObject teePrefab;
}
