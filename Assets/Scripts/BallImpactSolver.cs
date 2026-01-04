using System;
using UnityEngine;

public class BallImpactSolver : MonoBehaviour
{
    [Header("References")]
    public ClubDriver clubDriver;

    [Header("Ball Properties")]
    public float ballMass = 0.045f; // kg (real golf ball ~45g)

    [Header("Club Properties")]
    public float clubMass = 0.20f; // kg (driver head)

    [Range(0f, 1f)]
    public float COR = 0.83f; // Coefficient of Restitution
    public float loftDegrees = 10.5f; // Club loft in degrees

    [Header("Simulation Options")]
    public bool debugLogs = true;
    public bool ignoreGravity = false; // Useful for testing initial trajectory

    [Header("Debug Values (ReadOnly)")]
    public float lastClubSpeed;
    public float lastVNormal;
    public float lastBallSpeed;
    public Vector3 lastLaunchDir;

    // Internal state
    private Vector3 velocity;
    private Vector3 initialPosition;
    private bool isMoving = false;

    // Event for tracers or other systems
    public event Action OnBallLaunched;

    void Awake()
    {
        initialPosition = transform.position;
    }

    void OnEnable()
    {
        if (clubDriver != null)
            clubDriver.OnImpact += HandleImpact;
    }

    void OnDisable()
    {
        if (clubDriver != null)
            clubDriver.OnImpact -= HandleImpact;
    }

    void Update()
    {
        if (!isMoving)
            return;

        if (!ignoreGravity)
            velocity += Physics.gravity * Time.deltaTime;

        transform.position += velocity * Time.deltaTime;
    }

    private void HandleImpact(Vector3 impactPos, Vector3 clubVelocity, Vector3 faceNormal)
    {
        // Place ball slightly above impact to avoid clipping
        transform.position = impactPos + Vector3.up * 0.01f;
        Vector3 normal = faceNormal.normalized;

        // Component of club velocity along the face normal
        float vNormal = Vector3.Dot(clubVelocity, normal);

        if (vNormal <= 0f)
        {
            if (debugLogs)
                Debug.Log("Impact ignored: club moving away from ball.");
            return;
        }

        // Ball speed using COR and mass ratio
        float massRatio = clubMass / (clubMass + ballMass);
        float ballSpeed = vNormal * (1f + COR) * massRatio;

        // Apply loft
        Vector3 loftAxis = Vector3.Cross(Vector3.up, normal).normalized;
        Quaternion loftRotation = Quaternion.AngleAxis(-loftDegrees, loftAxis); // negative = upward
        Vector3 launchDir = loftRotation * normal;

        // Set velocity
        velocity = launchDir.normalized * ballSpeed;
        isMoving = true;

        // Store debug values
        lastClubSpeed = clubVelocity.magnitude;
        lastVNormal = vNormal;
        lastBallSpeed = ballSpeed;
        lastLaunchDir = launchDir;

        // Debug output
        if (debugLogs)
        {
            Debug.Log(
                $"--- Ball Launch ---\n"
                    + $"Club velocity: {clubVelocity.magnitude:F2} m/s\n"
                    + $"Normal component (vNormal): {vNormal:F2} m/s\n"
                    + $"Ball speed: {ballSpeed:F2} m/s\n"
                    + $"Launch direction: {launchDir}\n"
                    + $"Position: {transform.position}"
            );
            Debug.DrawRay(transform.position, launchDir * 2f, Color.green, 2f);
        }

        // Fire event for tracers or other listeners
        OnBallLaunched?.Invoke();
    }

    [ContextMenu("Reset Ball")]
    public void ResetBall()
    {
        velocity = Vector3.zero;
        isMoving = false;
        transform.position = initialPosition;
    }

    public bool IsMoving() => isMoving;
}
