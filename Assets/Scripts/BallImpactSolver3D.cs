using System;
using UnityEngine;

/// <summary>
/// D-Plane golf ball physics solver.
/// Implements TrackMan-style ball flight laws with loft-dependent launch characteristics.
/// Includes ground physics: bounce and roll simulation.
/// </summary>
public class BallImpactSolver3D : MonoBehaviour
{
    public enum BallState
    {
        Idle,
        Flying,
        Bouncing,
        Rolling,
        Stopped,
    }

    [Header("References")]
    public ClubDriver3D clubDriver;

    [Header("Ground Physics")]
    [Tooltip("Default surface when no GroundSurface component is found")]
    public SurfacePreset defaultSurface;

    [Tooltip("Layer mask for ground detection raycasts")]
    public LayerMask groundLayer = ~0;

    [Tooltip("Minimum bounce velocity to continue bouncing (m/s)")]
    public float minBounceVelocity = 0.5f;

    [Tooltip("Maximum number of bounces before forcing roll")]
    public int maxBounces = 10;

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

    [Tooltip("Base drag coefficient at low speed (golf ball with dimples: 0.25-0.35)")]
    public float CdBase = 0.27f;

    [Tooltip("Drag coefficient at high speed (Reynolds number effect)")]
    public float CdHighSpeed = 0.21f;

    [Tooltip("Speed threshold for drag transition (m/s)")]
    public float dragTransitionSpeed = 50f;

    [Tooltip("Baseline lift coefficient from dimples (applies even at lower spin)")]
    public float ClBaseline = 0.10f;

    [Tooltip("Additional lift coefficient per unit spin parameter")]
    public float ClSpinFactor = 0.35f;

    [Header("Debug")]
    public bool debugLogs = true;

    // Internal state
    private Vector3 velocity;
    private Vector3 spinVector; // rad/s, direction is spin axis
    private bool isMoving;
    private bool landed;
    private BallState currentState = BallState.Idle;
    private int bounceCount = 0;
    private SurfacePreset currentSurface;
    private Vector3 groundNormal = Vector3.up;

    private Vector3 launchPosition;
    private Vector3 landingPosition;
    private float maxHeight;
    private float firstBounceApex;
    private float flightTime;
    private float groundY = 0f;
    private float ballCrossSectionArea;
    private float totalDistance;
    private bool trackingFirstBounce;

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

    /// <summary>Carry distance - where ball first landed (meters)</summary>
    public float Carry { get; private set; }

    /// <summary>Total distance including roll (meters)</summary>
    public float TotalDistance =>
        Vector3.Distance(
            new Vector3(launchPosition.x, 0f, launchPosition.z),
            new Vector3(transform.position.x, 0f, transform.position.z)
        );

    /// <summary>Roll distance after landing (meters)</summary>
    public float RollDistance => TotalDistance - Carry;

    /// <summary>Offline distance at final position. Positive = right (meters)</summary>
    public float Offline => transform.position.x - launchPosition.x;

    /// <summary>Curve after apex. Positive = curved right (meters)</summary>
    public float CurveAfterApex => FinalPosition.x - ApexPosition.x;

    /// <summary>Number of bounces before rolling</summary>
    public int BounceCount => bounceCount;

    /// <summary>Maximum height reached after first bounce (meters)</summary>
    public float FirstBounceApex => firstBounceApex;

    /// <summary>Current ball state</summary>
    public BallState CurrentState => currentState;

    // ============================================
    // State Queries
    // ============================================

    public bool IsMoving() => isMoving;

    public bool IsLanded() => landed;

    public bool IsStopped() => currentState == BallState.Stopped;

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

        switch (currentState)
        {
            case BallState.Flying:
                UpdateFlying(dt);
                break;
            case BallState.Bouncing:
                UpdateBouncing(dt);
                break;
            case BallState.Rolling:
                UpdateRolling(dt);
                break;
        }
    }

    private void UpdateFlying(float dt)
    {
        flightTime += dt;

        // Gravity
        velocity += Physics.gravity * dt;

        float speed = velocity.magnitude;
        if (speed > 0.1f)
        {
            Vector3 velDir = velocity / speed;

            // Aerodynamic drag with Reynolds number effect
            if (enableDrag)
            {
                float transitionRange = Mathf.Max(dragTransitionSpeed - 20f, 1f);
                float speedFactor = Mathf.Clamp01((speed - 20f) / transitionRange);
                float Cd = Mathf.Lerp(CdBase, CdHighSpeed, speedFactor);

                float dragForce = 0.5f * airDensity * speed * speed * Cd * ballCrossSectionArea;
                Vector3 dragAccel = -velDir * (dragForce / ballMass);
                velocity += dragAccel * dt;
            }

            // Magnus lift force with dimple baseline effect
            if (enableLift && spinVector.sqrMagnitude > 0f)
            {
                float spinMag = spinVector.magnitude;
                float spinParameter = (spinMag * ballRadius) / speed;

                float Cl = ClBaseline + ClSpinFactor * spinParameter;
                Cl = Mathf.Clamp(Cl, 0f, 0.5f);

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

        // Check for ground collision
        if (transform.position.y <= groundY + ballRadius)
        {
            HandleGroundContact();
        }
    }

    private void UpdateBouncing(float dt)
    {
        // Apply gravity
        velocity += Physics.gravity * dt;

        // Move ball
        transform.position += velocity * dt;

        // Track first bounce apex
        if (trackingFirstBounce && transform.position.y > firstBounceApex)
        {
            firstBounceApex = transform.position.y;
        }

        // Check for ground contact
        if (transform.position.y <= groundY + ballRadius)
        {
            HandleGroundContact();
        }
    }

    private void UpdateRolling(float dt)
    {
        if (currentSurface == null)
            currentSurface = defaultSurface;

        // Check for special surfaces
        if (currentSurface != null)
        {
            if (currentSurface.instantStop)
            {
                StopBall();
                return;
            }
        }

        float speed = velocity.magnitude;
        float stopThreshold = currentSurface?.stopThreshold ?? 0.05f;

        if (speed < stopThreshold)
        {
            StopBall();
            return;
        }

        // Apply friction deceleration
        float friction = currentSurface?.rollFriction ?? 1.5f;
        float rollMultiplier = Mathf.Max(currentSurface?.rollSpeedMultiplier ?? 1.0f, 0.01f);

        // Scale decel only (not speed each frame) — multiplying speed by rollMultiplier every frame compounds when > 1
        // Tooltip: >1 = rolls longer (wet), <1 = shorter (rough) → effective friction ∝ 1 / rollMultiplier
        float decel = (friction / rollMultiplier) * dt;
        float newSpeed = Mathf.Max(0f, speed - decel);

        if (newSpeed > 0f)
        {
            velocity = velocity.normalized * newSpeed;
        }
        else
        {
            StopBall();
            return;
        }

        // Apply slope effect (gravity component along surface)
        Vector3 slopeForce =
            Physics.gravity - Vector3.Dot(Physics.gravity, groundNormal) * groundNormal;
        velocity += slopeForce * dt;

        // Tangent to slope — world XZ-only drops the downhill component when normal != up
        velocity -= Vector3.Dot(velocity, groundNormal) * groundNormal;

        // Move ball
        transform.position += velocity * dt;
        transform.position = new Vector3(
            transform.position.x,
            groundY + ballRadius,
            transform.position.z
        );

        // Update surface as ball rolls
        DetectSurface();
    }

    private void HandleGroundContact()
    {
        // Detect surface type
        DetectSurface();

        // Record carry distance on first landing
        if (!landed)
        {
            landed = true;
            landingPosition = transform.position;
            Carry = Vector3.Distance(
                new Vector3(launchPosition.x, 0f, launchPosition.z),
                new Vector3(landingPosition.x, 0f, landingPosition.z)
            );
        }

        // Check for special surfaces
        if (currentSurface != null)
        {
            if (currentSurface.instantStop || currentSurface.outOfBounds)
            {
                StopBall();
                return;
            }
        }

        // Calculate bounce
        float bounceCOR = currentSurface?.bounceCOR ?? 0.5f;
        float horizontalRetention = currentSurface?.bounceHorizontalRetention ?? 0.6f;
        float spinRetention = currentSurface?.bounceSpinRetention ?? 0.5f;

        // Reflect velocity off surface
        float verticalSpeed = -velocity.y;
        float newVerticalSpeed = verticalSpeed * bounceCOR;

        // Reduce horizontal speed on bounce
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        horizontalVel *= horizontalRetention;

        // Reduce spin on bounce
        spinVector *= spinRetention;

        // Check if bounce is strong enough to continue bouncing
        if (newVerticalSpeed < minBounceVelocity || bounceCount >= maxBounces)
        {
            // Transition to rolling
            currentState = BallState.Rolling;
            velocity = horizontalVel;
            transform.position = new Vector3(
                transform.position.x,
                groundY + ballRadius,
                transform.position.z
            );

            if (debugLogs)
                Debug.Log(
                    $"[Ball] Transitioning to roll after {bounceCount} bounces. Roll speed: {velocity.magnitude:F2} m/s"
                );
        }
        else
        {
            // Continue bouncing
            currentState = BallState.Bouncing;
            velocity = new Vector3(horizontalVel.x, newVerticalSpeed, horizontalVel.z);
            bounceCount++;

            // Start tracking apex after first bounce
            if (bounceCount == 1)
            {
                trackingFirstBounce = true;
                firstBounceApex = groundY + ballRadius;
            }
            else if (trackingFirstBounce)
            {
                // Stop tracking after first bounce apex is recorded
                trackingFirstBounce = false;
            }

            // Clamp position to ground
            transform.position = new Vector3(
                transform.position.x,
                groundY + ballRadius,
                transform.position.z
            );

            if (debugLogs)
                Debug.Log(
                    $"[Ball] Bounce {bounceCount}: vertical={newVerticalSpeed:F2} m/s, horizontal={horizontalVel.magnitude:F2} m/s"
                );
        }
    }

    private void DetectSurface()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 0.5f, groundLayer))
        {
            groundNormal = hit.normal;
            groundY = hit.point.y;

            GroundSurface surface = hit.collider.GetComponent<GroundSurface>();
            if (surface != null && surface.surfacePreset != null)
            {
                currentSurface = surface.surfacePreset;
            }
            else
            {
                currentSurface = defaultSurface;
            }
        }
        else
        {
            groundNormal = Vector3.up;
            currentSurface = defaultSurface;
        }
    }

    private void StopBall()
    {
        currentState = BallState.Stopped;
        isMoving = false;
        velocity = Vector3.zero;
        transform.position = new Vector3(
            transform.position.x,
            groundY + ballRadius,
            transform.position.z
        );

        if (debugLogs)
            LogShotResult();
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

        currentState = BallState.Flying;
        isMoving = true;
        landed = false;
        bounceCount = 0;
        flightTime = 0f;
        maxHeight = transform.position.y;
        ApexPosition = transform.position;
        Carry = 0f;
        firstBounceApex = 0f;
        trackingFirstBounce = false;
        currentSurface = defaultSurface;
        groundNormal = Vector3.up;

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

        string surfaceName = currentSurface != null ? currentSurface.displayName : "Unknown";

        Debug.Log(
            $"═══════════════════════════════════════\n"
                + $"           SHOT RESULT\n"
                + $"═══════════════════════════════════════\n"
                + $"  Carry:       {Carry:F2} m  ({Carry * 1.094f:F1} yds)\n"
                + $"  Roll:        {RollDistance:F2} m  ({RollDistance * 1.094f:F1} yds)\n"
                + $"  Total:       {TotalDistance:F2} m  ({TotalDistance * 1.094f:F1} yds)\n"
                + $"  Bounces:     {bounceCount}\n"
                + $"  Surface:     {surfaceName}\n"
                + $"───────────────────────────────────────\n"
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
        currentState = BallState.Idle;
        bounceCount = 0;
        flightTime = 0f;
        maxHeight = startPos.y;
        ApexPosition = startPos;
        transform.position = startPos;
        launchPosition = startPos;
        landingPosition = startPos;
        Carry = 0f;
        firstBounceApex = 0f;
        trackingFirstBounce = false;
        currentSurface = defaultSurface;
        groundNormal = Vector3.up;

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
