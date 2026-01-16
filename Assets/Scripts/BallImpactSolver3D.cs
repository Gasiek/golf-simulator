using System;
using UnityEngine;

/// <summary>
/// D-Plane golf ball physics solver.
/// Implements TrackMan-style ball flight laws with loft-dependent launch characteristics.
/// </summary>
public class BallImpactSolver3D : MonoBehaviour
{
    [Header("References")]
    public ClubDriver3D clubDriver;

    [Header("Ball Properties")]
    [Tooltip("Golf ball mass in kg (regulation: 0.04593 kg / 1.62 oz)")]
    public float ballMass = 0.04593f;

    [Tooltip("Golf ball radius in meters (regulation diameter: 42.67mm)")]
    public float ballRadius = 0.02135f;

    [Header("Club Properties")]
    [Tooltip("Club head mass in kg")]
    public float clubMass = 0.20f;

    [Range(0f, 1f)]
    [Tooltip("Coefficient of Restitution (driver: 0.83, irons: 0.78-0.81)")]
    public float COR = 0.86f;

    [Header("Aerodynamics")]
    public bool enableDrag = true;
    public bool enableLift = true;

    [Tooltip("Air density in kg/m³ (sea level at 15°C: 1.225)")]
    public float airDensity = 1.225f;

    [Tooltip("Base drag coefficient (golf ball with dimples: 0.25-0.35)")]
    public float Cd = 0.24f;

    [Tooltip("Magnus lift coefficient multiplier")]
    public float ClMagnusMultiplier = 0.25f;

    [Header("Debug")]
    public bool debugLogs = true;

    // Internal state
    private Vector3 velocity;
    private Vector3 spinVector; // rad/s, direction is spin axis
    private bool isMoving;
    private bool landed;

    private Vector3 launchPosition;
    private float maxHeight;
    private float flightTime;
    private float groundY = 0f;
    private float ballCrossSectionArea;

    // ============================================
    // TrackMan-style Club Delivery Parameters
    // ============================================

    /// <summary>Club head speed at impact (m/s)</summary>
    public float ClubSpeed { get; private set; }

    /// <summary>Vertical angle of club path. Negative = descending blow (degrees)</summary>
    public float AttackAngle { get; private set; }

    /// <summary>Horizontal club path direction. Positive = in-to-out for RH golfer (degrees)</summary>
    public float ClubPath { get; private set; }

    /// <summary>Horizontal face aim at impact. Positive = open/right for RH golfer (degrees)</summary>
    public float FaceAngle { get; private set; }

    /// <summary>Loft presented at impact including shaft lean (degrees)</summary>
    public float DynamicLoft { get; private set; }

    /// <summary>3D angle between face and club path - determines spin rate (degrees)</summary>
    public float SpinLoft { get; private set; }

    /// <summary>Face angle minus club path - determines curve direction (degrees)</summary>
    public float FaceToPath { get; private set; }

    // ============================================
    // TrackMan-style Ball Launch Parameters
    // ============================================

    /// <summary>Ball speed immediately after impact (m/s)</summary>
    public float BallSpeed { get; private set; }

    /// <summary>Smash factor: ball speed / club speed (driver optimal: 1.48-1.50)</summary>
    public float SmashFactor { get; private set; }

    /// <summary>Vertical launch angle (degrees)</summary>
    public float LaunchAngle { get; private set; }

    /// <summary>Horizontal launch direction. Positive = right for RH golfer (degrees)</summary>
    public float LaunchDirection { get; private set; }

    /// <summary>Total spin rate (RPM)</summary>
    public float SpinRate { get; private set; }

    /// <summary>Spin axis tilt from horizontal. Positive = tilted right = fade spin for RH (degrees)</summary>
    public float SpinAxisTilt { get; private set; }

    // ============================================
    // Spin Vector Access
    // ============================================

    /// <summary>Spin vector in rad/s (magnitude = spin rate, direction = spin axis)</summary>
    public Vector3 SpinVector => spinVector;

    /// <summary>Normalized spin axis</summary>
    public Vector3 SpinAxis => spinVector.sqrMagnitude > 0f ? spinVector.normalized : Vector3.right;

    // ============================================
    // Flight Result Parameters
    // ============================================

    /// <summary>Maximum height reached (meters)</summary>
    public float Apex => maxHeight;

    /// <summary>Total flight time (seconds)</summary>
    public float FlightTime => flightTime;

    /// <summary>Position at apex</summary>
    public Vector3 ApexPosition { get; private set; }

    /// <summary>Final landing position</summary>
    public Vector3 FinalPosition => transform.position;

    /// <summary>Carry distance (meters)</summary>
    public float Carry =>
        Vector3.Distance(
            new Vector3(launchPosition.x, 0f, launchPosition.z),
            new Vector3(transform.position.x, 0f, transform.position.z)
        );

    /// <summary>Offline distance at landing. Positive = right (meters)</summary>
    public float Offline => transform.position.x - launchPosition.x;

    /// <summary>Curve after apex. Positive = curved right (meters)</summary>
    public float CurveAfterApex => FinalPosition.x - ApexPosition.x;

    // ============================================
    // State Queries
    // ============================================

    public bool IsMoving() => isMoving;

    public bool IsLanded() => landed;

    public event Action OnBallLaunched;

    void Awake()
    {
        ballCrossSectionArea = Mathf.PI * ballRadius * ballRadius;
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

        float dt = Time.deltaTime;
        flightTime += dt;

        // Gravity
        velocity += Physics.gravity * dt;

        float speed = velocity.magnitude;
        if (speed > 0.1f)
        {
            Vector3 velDir = velocity / speed;

            // Aerodynamic drag: F = 0.5 * ρ * v² * Cd * A
            if (enableDrag)
            {
                float dragForce = 0.5f * airDensity * speed * speed * Cd * ballCrossSectionArea;
                Vector3 dragAccel = -velDir * (dragForce / ballMass);
                velocity += dragAccel * dt;
            }

            // Magnus lift force
            if (enableLift && spinVector.sqrMagnitude > 0f)
            {
                float spinMag = spinVector.magnitude; // rad/s
                float spinParameter = (spinMag * ballRadius) / speed;

                // Effective lift coefficient increases with spin parameter
                float Cl = ClMagnusMultiplier * spinParameter;
                Cl = Mathf.Clamp(Cl, 0f, 0.4f); // Physical limit

                float liftForce = 0.5f * airDensity * speed * speed * Cl * ballCrossSectionArea;
                Vector3 liftDir = Vector3.Cross(spinVector.normalized, velDir).normalized;
                Vector3 liftAccel = liftDir * (liftForce / ballMass);

                velocity += liftAccel * dt;
            }
        }

        transform.position += velocity * dt;

        if (transform.position.y > maxHeight)
        {
            maxHeight = transform.position.y;
            ApexPosition = transform.position;
        }

        if (!landed && transform.position.y <= groundY)
        {
            landed = true;
            isMoving = false;
            velocity = Vector3.zero;
            transform.position = new Vector3(transform.position.x, groundY, transform.position.z);

            if (debugLogs)
                LogShotResult();
        }
    }

    /// <summary>
    /// Handles club-ball impact using D-Plane physics model.
    /// </summary>
    private void HandleImpact(
        Vector3 impactPos,
        Vector3 clubVelocity,
        Vector3 faceNormal,
        float attackAngle,
        float faceAngleDegrees
    )
    {
        transform.position = impactPos + Vector3.up * 0.01f;
        launchPosition = transform.position;

        ClubSpeed = clubVelocity.magnitude;
        if (ClubSpeed < 0.1f)
            return;

        // ============================================
        // STEP 1: Store Club Delivery Parameters
        // ============================================

        Vector3 clubDir = clubVelocity.normalized;

        // Attack Angle: vertical angle of club path (from ClubDriver)
        AttackAngle = attackAngle;

        // Club Path: horizontal direction of club head travel
        Vector3 clubHorizontal = new Vector3(clubDir.x, 0f, clubDir.z);
        if (clubHorizontal.sqrMagnitude > 0.0001f)
        {
            clubHorizontal.Normalize();
            ClubPath = Mathf.Atan2(clubHorizontal.x, clubHorizontal.z) * Mathf.Rad2Deg;
        }
        else
        {
            ClubPath = 0f;
        }

        FaceAngle = faceAngleDegrees;

        // ============================================
        // STEP 2: Calculate Dynamic Loft from Face Normal
        // ============================================

        Vector3 faceDir = faceNormal.normalized;

        // Dynamic Loft: angle of face normal above horizontal
        DynamicLoft = Mathf.Asin(Mathf.Clamp(faceDir.y, -1f, 1f)) * Mathf.Rad2Deg;

        // ============================================
        // STEP 3: D-Plane Calculations
        // ============================================

        // Spin Loft: 3D angle between face normal and club direction
        SpinLoft = Vector3.Angle(faceDir, clubDir);
        SpinLoft = Mathf.Clamp(SpinLoft, 5f, 60f);

        // Face to Path: determines spin axis tilt and curve direction
        // Positive = face open to path = fade/slice
        // Negative = face closed to path = draw/hook
        FaceToPath = FaceAngle - ClubPath;

        // ============================================
        // STEP 4: Ball Speed (Momentum Transfer)
        // ============================================

        float impactNormalSpeed = Vector3.Dot(clubVelocity, faceDir);
        if (impactNormalSpeed <= 0f)
            return;

        float massRatio = clubMass / (clubMass + ballMass);
        BallSpeed = impactNormalSpeed * (1f + COR) * massRatio;
        SmashFactor = BallSpeed / ClubSpeed;

        // ============================================
        // STEP 5: Launch Direction (D-Plane Model)
        // ============================================

        // Face contribution decreases with higher spin loft
        // Driver (~15° spin loft): ~85% face, ~15% path
        // 7-iron (~30° spin loft): ~70% face, ~30% path
        // Wedge (~50° spin loft): ~55% face, ~45% path
        float faceContribution = 1.0f - (SpinLoft / 140f);
        faceContribution = Mathf.Clamp(faceContribution, 0.5f, 0.9f);
        float pathContribution = 1.0f - faceContribution;

        // Horizontal launch direction
        LaunchDirection = FaceAngle * faceContribution + ClubPath * pathContribution;

        // Vertical launch angle
        float loftInfluence = 0.83f;
        float aoaInfluence = 0.5f;
        LaunchAngle = DynamicLoft * loftInfluence + AttackAngle * aoaInfluence;
        LaunchAngle = Mathf.Clamp(LaunchAngle, 0f, 65f);

        // Construct launch velocity vector
        float launchDirRad = LaunchDirection * Mathf.Deg2Rad;
        float launchAngRad = LaunchAngle * Mathf.Deg2Rad;

        Vector3 launchDir = new Vector3(
            Mathf.Sin(launchDirRad) * Mathf.Cos(launchAngRad),
            Mathf.Sin(launchAngRad),
            Mathf.Cos(launchDirRad) * Mathf.Cos(launchAngRad)
        ).normalized;

        velocity = launchDir * BallSpeed;

        // ============================================
        // STEP 6: Spin Calculations
        // ============================================

        float spinLoftRad = SpinLoft * Mathf.Deg2Rad;
        float tangentialSpeed = BallSpeed * Mathf.Sin(spinLoftRad);

        // Surface speed to spin rate
        float spinRateRadS = tangentialSpeed / ballRadius;

        // Apply friction efficiency
        float frictionEfficiency = 0.55f;
        spinRateRadS *= frictionEfficiency;

        SpinRate = spinRateRadS * 60f / (2f * Mathf.PI); // Convert to RPM
        SpinRate = Mathf.Clamp(SpinRate, 1000f, 12000f);

        // Convert back to rad/s for physics
        spinRateRadS = SpinRate * 2f * Mathf.PI / 60f;

        // ============================================
        // STEP 7: Spin Axis Tilt
        // ============================================

        // Positive FaceToPath = fade, Negative = draw
        float f2pClamped = Mathf.Clamp(FaceToPath, -20f, 20f);
        SpinAxisTilt = Mathf.Atan2(f2pClamped, SpinLoft) * Mathf.Rad2Deg;

        // ============================================
        // STEP 8: Construct Spin Vector
        // ============================================

        // Backspin axis: perpendicular to launch direction
        Vector3 backspinAxis = new Vector3(
            -Mathf.Cos(launchDirRad),
            0f,
            Mathf.Sin(launchDirRad)
        ).normalized;

        Vector3 tiltedAxis = Quaternion.AngleAxis(-SpinAxisTilt, launchDir) * backspinAxis;

        spinVector = tiltedAxis.normalized * spinRateRadS;

        // ============================================
        // Initialize flight state
        // ============================================

        isMoving = true;
        landed = false;
        flightTime = 0f;
        maxHeight = transform.position.y;
        ApexPosition = transform.position;

        OnBallLaunched?.Invoke();

        if (debugLogs)
            LogLaunchData();
    }

    private void LogLaunchData()
    {
        string shotShape = "Straight";
        if (FaceToPath > 1f)
            shotShape = "Fade";
        else if (FaceToPath < -1f)
            shotShape = "Draw";

        Debug.Log(
            $"═══════════════════════════════════════\n"
                + $"         D-PLANE IMPACT DATA\n"
                + $"═══════════════════════════════════════\n"
                + $"  CLUB DELIVERY\n"
                + $"───────────────────────────────────────\n"
                + $"  Club Speed:    {ClubSpeed:F2} m/s  ({ClubSpeed * 2.237f:F1} mph)\n"
                + $"  Attack Angle:  {AttackAngle:F1}°\n"
                + $"  Club Path:     {ClubPath:F1}°\n"
                + $"  Face Angle:    {FaceAngle:F1}°\n"
                + $"  Dynamic Loft:  {DynamicLoft:F1}°\n"
                + $"  Spin Loft:     {SpinLoft:F1}°\n"
                + $"  Face to Path:  {FaceToPath:F1}° ({shotShape})\n"
                + $"───────────────────────────────────────\n"
                + $"  BALL LAUNCH\n"
                + $"───────────────────────────────────────\n"
                + $"  Ball Speed:    {BallSpeed:F2} m/s  ({BallSpeed * 2.237f:F1} mph)\n"
                + $"  Smash Factor:  {SmashFactor:F3}\n"
                + $"  Launch Angle:  {LaunchAngle:F1}°\n"
                + $"  Launch Dir:    {LaunchDirection:F1}°\n"
                + $"  Spin Rate:     {SpinRate:F0} rpm\n"
                + $"  Spin Axis:     {SpinAxisTilt:F1}°\n"
                + $"═══════════════════════════════════════"
        );
    }

    private void LogShotResult()
    {
        string curveType = "Straight";
        if (CurveAfterApex > 1f)
            curveType = "Faded";
        else if (CurveAfterApex < -1f)
            curveType = "Drew";

        Debug.Log(
            $"═══════════════════════════════════════\n"
                + $"           SHOT RESULT\n"
                + $"═══════════════════════════════════════\n"
                + $"  Carry:       {Carry:F2} m  ({Carry * 1.094f:F1} yds)\n"
                + $"  Offline:     {Offline:F2} m  ({(Offline > 0 ? "Right" : "Left")})\n"
                + $"  Apex:        {Apex:F2} m  ({Apex * 3.281f:F1} ft)\n"
                + $"  Flight Time: {FlightTime:F2} s\n"
                + $"  Curve:       {CurveAfterApex:F2} m after apex ({curveType})\n"
                + $"═══════════════════════════════════════"
        );
    }

    public void ResetAndPrepare(Vector3 startPos)
    {
        velocity = Vector3.zero;
        spinVector = Vector3.zero;
        isMoving = false;
        landed = false;
        flightTime = 0f;
        maxHeight = startPos.y;
        ApexPosition = startPos;
        transform.position = startPos;
        launchPosition = startPos;

        // Reset tracked values
        ClubSpeed = 0f;
        AttackAngle = 0f;
        ClubPath = 0f;
        FaceAngle = 0f;
        DynamicLoft = 0f;
        SpinLoft = 0f;
        FaceToPath = 0f;
        BallSpeed = 0f;
        SmashFactor = 0f;
        LaunchAngle = 0f;
        LaunchDirection = 0f;
        SpinRate = 0f;
        SpinAxisTilt = 0f;
    }
}
