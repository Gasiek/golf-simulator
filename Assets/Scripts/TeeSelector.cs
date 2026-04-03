using System;
using UnityEngine;

/// <summary>
/// Runtime tee selector for switching between tee height presets.
/// Use this in VR to let players choose their tee height.
/// </summary>
public class TeeSelector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The ball to reposition when tee changes")]
    public Transform ball;

    [Tooltip("The club driver to adjust Z offset")]
    public ClubDriver3D clubDriver;

    [Header("Available Tees")]
    [Tooltip("All available tee presets (assign in order: Ground, Low, Medium, High)")]
    public TeeHeightPreset[] availableTees;

    [Header("Current Selection")]
    [SerializeField]
    private int currentTeeIndex = 0;

    [Header("Tee Visual")]
    [Tooltip("Parent transform for spawned tee visuals")]
    public Transform teeVisualParent;

    private GameObject currentTeeVisual;

    /// <summary>
    /// Currently selected tee preset
    /// </summary>
    public TeeHeightPreset CurrentTee =>
        availableTees != null && availableTees.Length > 0
            ? availableTees[Mathf.Clamp(currentTeeIndex, 0, availableTees.Length - 1)]
            : null;

    /// <summary>
    /// Current tee index (0-based)
    /// </summary>
    public int CurrentIndex => currentTeeIndex;

    /// <summary>
    /// Number of available tees
    /// </summary>
    public int TeeCount => availableTees?.Length ?? 0;

    /// <summary>
    /// Event fired when tee selection changes
    /// </summary>
    public event Action<TeeHeightPreset> OnTeeChanged;

    void Start()
    {
        if (availableTees == null || availableTees.Length == 0)
        {
            Debug.LogWarning("TeeSelector: No tee presets assigned!");
            return;
        }

        ApplyCurrentTee();
    }

    /// <summary>
    /// Select a specific tee by index
    /// </summary>
    public void SelectTee(int index)
    {
        if (availableTees == null || availableTees.Length == 0)
            return;

        currentTeeIndex = Mathf.Clamp(index, 0, availableTees.Length - 1);
        ApplyCurrentTee();
    }

    /// <summary>
    /// Select a specific tee preset
    /// </summary>
    public void SelectTee(TeeHeightPreset preset)
    {
        if (availableTees == null || preset == null)
            return;

        for (int i = 0; i < availableTees.Length; i++)
        {
            if (availableTees[i] == preset)
            {
                SelectTee(i);
                return;
            }
        }

        Debug.LogWarning($"TeeSelector: Preset '{preset.displayName}' not in available tees!");
    }

    /// <summary>
    /// Cycle to the next tee
    /// </summary>
    public void NextTee()
    {
        if (availableTees == null || availableTees.Length == 0)
            return;

        currentTeeIndex = (currentTeeIndex + 1) % availableTees.Length;
        ApplyCurrentTee();
    }

    /// <summary>
    /// Cycle to the previous tee
    /// </summary>
    public void PreviousTee()
    {
        if (availableTees == null || availableTees.Length == 0)
            return;

        currentTeeIndex = (currentTeeIndex - 1 + availableTees.Length) % availableTees.Length;
        ApplyCurrentTee();
    }

    /// <summary>
    /// Apply the current tee settings to ball and club
    /// </summary>
    private void ApplyCurrentTee()
    {
        var tee = CurrentTee;
        if (tee == null)
            return;

        // Position ball
        if (ball != null)
        {
            Vector3 pos = ball.position;
            pos.y = tee.ballY;
            ball.position = pos;
        }

        // Adjust club offset
        if (clubDriver != null)
        {
            clubDriver.impactPlaneY = tee.ballY;
            clubDriver.clubZOffset = tee.clubZOffset;
        }

        // Update visual
        UpdateTeeVisual(tee);

        Debug.Log(
            $"[TeeSelector] Selected: {tee.displayName} (Y: {tee.ballY}, Z: {tee.clubZOffset})"
        );

        OnTeeChanged?.Invoke(tee);
    }

    /// <summary>
    /// Spawn/update the tee visual prefab
    /// </summary>
    private void UpdateTeeVisual(TeeHeightPreset tee)
    {
        // Destroy existing visual
        if (currentTeeVisual != null)
        {
            Destroy(currentTeeVisual);
            currentTeeVisual = null;
        }

        // Spawn new visual if preset has one
        if (tee.teePrefab != null)
        {
            Transform parent = teeVisualParent != null ? teeVisualParent : transform;
            currentTeeVisual = Instantiate(tee.teePrefab, parent);
            currentTeeVisual.transform.localPosition = Vector3.zero;
        }
    }

    /// <summary>
    /// Get tee preset by index
    /// </summary>
    public TeeHeightPreset GetTee(int index)
    {
        if (availableTees == null || index < 0 || index >= availableTees.Length)
            return null;

        return availableTees[index];
    }
}
