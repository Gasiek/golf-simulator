using System;
using UnityEngine;

public class BallImpactSolver : MonoBehaviour
{
    [Header("References")]
    public ClubDriver clubDriver;

    [Header("Ball Properties")]
    public float ballMass = 0.045f;

    [Header("Club Properties")]
    public float clubMass = 0.20f;

    [Range(0f, 1f)]
    public float COR = 0.83f;
    public float loftDegrees = 10.5f;

    [Header("Aerodynamics")]
    public bool enableDrag = true;
    public bool enableLift = true;
    public float dragCoefficient = 0.003f;
    public float liftCoefficient = 0.0004f;
    public float spinEfficiency = 0.8f;

    [Header("Debug")]
    public bool debugLogs = true;

    // Motion
    private Vector3 velocity;
    private Vector3 spin;
    private bool isMoving;

    // Shot stats
    private Vector3 launchPosition;
    private float maxHeight;
    private float flightTime;
    private bool landed;

    public event Action OnBallLaunched;

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

        float dt = Time.deltaTime;
        flightTime += dt;

        // Gravity
        velocity += Physics.gravity * dt;

        // Drag
        if (enableDrag)
            velocity += -velocity.normalized * dragCoefficient * velocity.sqrMagnitude * dt;

        // Lift (Magnus)
        if (enableLift && spin.sqrMagnitude > 0f)
        {
            Vector3 liftDir = Vector3.Cross(spin, velocity).normalized;
            velocity += liftDir * liftCoefficient * spin.magnitude * dt;
        }

        transform.position += velocity * dt;

        if (transform.position.y > maxHeight)
            maxHeight = transform.position.y;

        // Landing detection
        if (!landed && transform.position.y <= 0f)
        {
            landed = true;
            LogShotResult();
        }
    }

    private void HandleImpact(Vector3 impactPos, Vector3 clubVelocity, Vector3 faceNormal)
    {
        transform.position = impactPos + Vector3.up * 0.01f;
        launchPosition = transform.position;

        Vector3 normal = faceNormal.normalized;
        float vNormal = Vector3.Dot(clubVelocity, normal);
        if (vNormal <= 0f)
            return;

        // Ball speed
        float massRatio = clubMass / (clubMass + ballMass);
        float ballSpeed = vNormal * (1f + COR) * massRatio;

        // Launch direction
        Vector3 loftAxis = Vector3.Cross(Vector3.up, normal).normalized;
        Quaternion loftRotation = Quaternion.AngleAxis(-loftDegrees, loftAxis);
        Vector3 launchDir = loftRotation * normal;
        velocity = launchDir.normalized * ballSpeed;

        // Spin
        float spinRPM = ballSpeed * loftDegrees * spinEfficiency * 30f;
        Vector3 spinAxis = Vector3.Cross(launchDir, Vector3.up).normalized;
        spin = spinAxis * (spinRPM * Mathf.Deg2Rad / 60f); // rad/s

        // Reset stats
        isMoving = true;
        landed = false;
        flightTime = 0f;
        maxHeight = launchPosition.y;

        OnBallLaunched?.Invoke();

        if (debugLogs)
            LogLaunch(clubVelocity, ballSpeed, launchDir, spinRPM);
    }

    private void LogLaunch(Vector3 clubVelocity, float ballSpeed, Vector3 launchDir, float spinRPM)
    {
        float launchAngle = Vector3.Angle(launchDir, Vector3.ProjectOnPlane(launchDir, Vector3.up));
        float sideAngle = Vector3.SignedAngle(Vector3.forward, launchDir, Vector3.up);

        string config =
            $"Loft: {loftDegrees:F1}°, Drag: {enableDrag}, Lift: {enableLift}, "
            + $"Path Angle: {clubDriver?.swingPathAngle:F1}°, Face Angle: {clubDriver?.faceAngle:F1}°";

        Debug.Log(
            $"=== Shot Configuration ===\n{config}\n"
                + $"--- Ball Launch ---\n"
                + $"Club Speed: {clubVelocity.magnitude:F2} m/s\n"
                + $"Ball Speed: {ballSpeed:F2} m/s\n"
                + $"Launch Angle: {launchAngle:F1}°\n"
                + $"Side Angle: {sideAngle:F1}°\n"
                + $"Spin: {spinRPM:F0} rpm"
        );
    }

    private void LogShotResult()
    {
        float carryDistance = Vector3.Distance(
            new Vector3(launchPosition.x, 0f, launchPosition.z),
            new Vector3(transform.position.x, 0f, transform.position.z)
        );

        if (debugLogs)
        {
            Debug.Log(
                $"--- Shot Result ---\n"
                    + $"Carry: {carryDistance:F1} m\n"
                    + $"Apex: {maxHeight:F1} m\n"
                    + $"Flight Time: {flightTime:F2} s\n"
                    + $"Final Position: {transform.position}"
            );
        }
    }

    public bool IsMoving() => isMoving;

    [ContextMenu("Reset Ball")]
    public void ResetBall()
    {
        velocity = Vector3.zero;
        spin = Vector3.zero;
        isMoving = false;
        landed = false;
        flightTime = 0f;
        maxHeight = transform.position.y;
        transform.position = launchPosition;
    }
}
