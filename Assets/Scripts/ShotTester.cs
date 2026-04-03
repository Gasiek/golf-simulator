using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class ShotConfig
{
    public float loft;
    public bool drag;
    public bool lift;
    public float pathAngle;
    public float faceAngle;
    public float swingPlaneTilt;
    public TeeHeightPreset teeHeight;

    /// <summary>Null when <see cref="ShotTester.surfacePresets"/> is empty (scene/ball defaults unchanged).</summary>
    public SurfacePreset surface;
}

public class ShotTester : MonoBehaviour
{
    [Header("References")]
    public ClubDriver3D clubDriver;
    public BallImpactSolver3D ball;

    [Header("Tee Heights")]
    [Tooltip("Tee height presets to test. Assign ScriptableObject presets here.")]
    public TeeHeightPreset[] teeHeights;

    [Header("Surfaces")]
    [Tooltip(
        "Surface presets to sweep (bounce/roll). Leave empty for a single run using the ball's Default Surface and scene as-is."
    )]
    public SurfacePreset[] surfacePresets;

    [Tooltip(
        "Ground collider's GroundSurface, if any. Required for per-shot surface changes when the ball raycast hits that collider; otherwise assign only on the ball or leave surfaces empty."
    )]
    public GroundSurface groundSurface;

    [Header("Option Arrays")]
    public float[] lofts = new float[] { 32f };
    public float[] pathAngles = new float[] { 0f };
    public float[] faceAngles = new float[] { 0f };
    public float[] swingPlaneTilts = new float[] { -10f, -5f, 0f, 5f, 10f };
    public bool[] dragOptions = new bool[] { true };
    public bool[] liftOptions = new bool[] { true };

    [Header("Timing")]
    public float delayBetweenShots = 0.5f;
    public float maxShotTime = 30f; // Timeout to prevent infinite loops

    [Header("CSV Output")]
    public string csvFileName = "ShotResults.csv";

    [Header("Debug")]
    public bool verboseLogging = false;

    private List<ShotConfig> allShots = new List<ShotConfig>();
    private string csvPath;
    private bool isRunning = false;

    void Start()
    {
        if (clubDriver == null || ball == null)
        {
            Debug.LogError("ShotTester: Assign ClubDriver and Ball references!");
            return;
        }

        if (teeHeights == null || teeHeights.Length == 0)
        {
            Debug.LogError("ShotTester: Assign at least one TeeHeightPreset!");
            return;
        }

        for (int i = 0; i < teeHeights.Length; i++)
        {
            if (teeHeights[i] == null)
            {
                Debug.LogError($"ShotTester: TeeHeightPreset at index {i} is null!");
                return;
            }
        }

        if (surfacePresets != null && surfacePresets.Length > 0 && groundSurface == null)
        {
            Debug.LogWarning(
                "ShotTester: surfacePresets is set but groundSurface is not assigned. "
                    + "If your ground uses a GroundSurface component, per-shot surfaces will not apply until you assign it here."
            );
        }

        csvPath = Path.Combine(GetTestResultsPath(), csvFileName);

        // Write CSV header with D-Plane and ground physics parameters
        File.WriteAllText(
            csvPath,
            // Config columns
            "TeeHeight,TeeY,Surface,ConfigLoft,ConfigDrag,ConfigLift,ConfigPathAngle,ConfigFaceAngle,ConfigSwingPlaneTilt,"
                // Club delivery (D-Plane inputs)
                + "ClubSpeed_mps,ClubSpeed_mph,AttackAngle,ClubPath,FaceAngle,DynamicLoft,SpinLoft,FaceToPath,"
                // Ball launch (D-Plane outputs)
                + "BallSpeed_mps,BallSpeed_mph,SmashFactor,LaunchAngle,LaunchDirection,SpinRate_rpm,SpinAxisTilt,"
                // Flight results
                + "Carry_m,Carry_yds,Apex_m,FlightTime_s,CurveAfterApex_m,"
                // Ground physics
                + "Roll_m,Roll_yds,Total_m,Total_yds,Bounces,FirstBounceApex_m,"
                // Final position
                + "Offline_m,FinalPosX,FinalPosY,FinalPosZ\n"
        );

        GenerateAllShots();
        StartCoroutine(RunShotsSequentially());
    }

    private void GenerateAllShots()
    {
        allShots.Clear();
        foreach (var surface in EnumerateSurfacesForTest())
        foreach (var tee in teeHeights)
        foreach (var loft in lofts)
        foreach (var path in pathAngles)
        foreach (var face in faceAngles)
        foreach (var tilt in swingPlaneTilts)
        foreach (var drag in dragOptions)
        foreach (var lift in liftOptions)
        {
            allShots.Add(
                new ShotConfig
                {
                    loft = loft,
                    pathAngle = path,
                    faceAngle = face,
                    swingPlaneTilt = tilt,
                    drag = drag,
                    lift = lift,
                    teeHeight = tee,
                    surface = surface,
                }
            );
        }

        Debug.Log($"[ShotTester] Generated {allShots.Count} shot scenarios.");
    }

    /// <summary>
    /// One null when <see cref="surfacePresets"/> is empty (no surface mutation). Otherwise each array slot, including nulls (no-op apply).
    /// </summary>
    private IEnumerable<SurfacePreset> EnumerateSurfacesForTest()
    {
        if (surfacePresets == null || surfacePresets.Length == 0)
        {
            yield return null;
            yield break;
        }

        foreach (var preset in surfacePresets)
            yield return preset;
    }

    private void ApplySurfaceForShot(SurfacePreset preset)
    {
        if (preset == null)
            return;

        ball.defaultSurface = preset;
        if (groundSurface != null)
            groundSurface.surfacePreset = preset;
    }

    private IEnumerator RunShotsSequentially()
    {
        isRunning = true;

        int surfaceSlots =
            surfacePresets == null || surfacePresets.Length == 0 ? 1 : surfacePresets.Length;
        Debug.Log(
            $"[ShotTester] Starting test sequence with {teeHeights.Length} tee height(s), {surfaceSlots} surface slot(s)."
        );

        for (int i = 0; i < allShots.Count; i++)
        {
            var config = allShots[i];

            Debug.Log(
                $"═══════════════════════════════════════\n"
                    + $"  SHOT {i + 1}/{allShots.Count}\n"
                    + $"═══════════════════════════════════════\n"
                    + $"  Tee: {config.teeHeight.displayName} (Y: {config.teeHeight.ballY})\n"
                    + $"  Surface: {(config.surface != null ? config.surface.displayName : "(unchanged)")}\n"
                    + $"  Loft: {config.loft}°\n"
                    + $"  Path Angle: {config.pathAngle}°\n"
                    + $"  Face Angle: {config.faceAngle}°\n"
                    + $"  Swing Plane Tilt: {config.swingPlaneTilt}°\n"
                    + $"  Drag: {config.drag}, Lift: {config.lift}"
            );

            // Step 1: Surface + reset (defaultSurface must be set before ResetAndPrepare)
            ApplySurfaceForShot(config.surface);
            Vector3 ballStartPos = new Vector3(0f, config.teeHeight.ballY, 0f);
            ball.ResetAndPrepare(ballStartPos);

            if (verboseLogging)
                Debug.Log(
                    $"[ShotTester] Ball reset. Position: {ball.transform.position}, IsMoving: {ball.IsMoving()}, IsLanded: {ball.IsLanded()}"
                );

            // Step 2: Reset club
            clubDriver.ResetClub();

            if (verboseLogging)
                Debug.Log($"[ShotTester] Club reset. IsSwinging: {clubDriver.IsSwinging()}");

            // Step 3: Apply configuration
            clubDriver.clubLoftDegrees = config.loft;
            ball.enableDrag = config.drag;
            ball.enableLift = config.lift;
            clubDriver.faceAngle = config.faceAngle;
            clubDriver.swingPathAngle = config.pathAngle;
            clubDriver.swingPlaneTilt = config.swingPlaneTilt;
            clubDriver.impactPlaneY = config.teeHeight.ballY;
            clubDriver.clubZOffset = config.teeHeight.clubZOffset;

            // Step 4: Wait a frame to ensure everything is initialized
            yield return null;

            // Step 5: Start swing
            clubDriver.StartSwing();

            if (verboseLogging)
                Debug.Log($"[ShotTester] Swing started. IsSwinging: {clubDriver.IsSwinging()}");

            // Step 6: Wait for swing to hit the ball (club swinging, ball not yet moving)
            float timeout = Time.time + maxShotTime;
            while (clubDriver.IsSwinging() && !ball.IsMoving())
            {
                if (Time.time > timeout)
                {
                    Debug.LogError($"[ShotTester] Timeout waiting for impact on shot {i + 1}");
                    break;
                }
                yield return null;
            }

            if (verboseLogging)
                Debug.Log($"[ShotTester] After swing phase. Ball IsMoving: {ball.IsMoving()}");

            // Step 7: Wait for ball to stop. Idle is not Stopped — if we never launched (miss or
            // HandleImpact early exit), waiting here would hit maxShotTime every time.
            if (ball.IsMoving())
            {
                timeout = Time.time + maxShotTime;
                while (!ball.IsStopped())
                {
                    if (Time.time > timeout)
                    {
                        Debug.LogError(
                            $"[ShotTester] Timeout waiting for ball to stop on shot {i + 1}"
                        );
                        break;
                    }
                    yield return null;
                }

                if (verboseLogging)
                    Debug.Log(
                        $"[ShotTester] Ball stopped. Carry: {ball.Carry:F1}m, Roll: {ball.RollDistance:F1}m, Total: {ball.TotalDistance:F1}m, Bounces: {ball.BounceCount}"
                    );
            }
            else
            {
                Debug.LogWarning(
                    $"[ShotTester] Shot {i + 1}/{allShots.Count}: ball never launched "
                        + "(club missed, club speed < threshold, or impact normal rejected). "
                        + "CSV row will show zeros."
                );
            }

            // Step 8: Wait for club to finish swinging (if not already)
            while (clubDriver.IsSwinging())
            {
                yield return null;
            }

            // Step 9: Write shot data to CSV
            WriteShotToCSV(config);

            // Step 10: Delay between shots
            yield return new WaitForSeconds(delayBetweenShots);
        }

        Debug.Log(
            $"═══════════════════════════════════════\n"
                + $"  ALL {allShots.Count} SHOTS COMPLETE\n"
                + $"═══════════════════════════════════════\n"
                + $"  CSV saved at:\n"
                + $"  {csvPath}"
        );

        isRunning = false;
    }

    private void WriteShotToCSV(ShotConfig config)
    {
        // Convert units for readability
        float clubSpeedMph = ball.ClubSpeed * 2.237f;
        float ballSpeedMph = ball.BallSpeed * 2.237f;
        float carryYds = ball.Carry * 1.094f;
        float rollYds = ball.RollDistance * 1.094f;
        float totalYds = ball.TotalDistance * 1.094f;

        string surfaceLabel = config.surface != null ? config.surface.displayName : "";

        string line = string.Format(
            // Config columns (9)
            "{0},{1:F4},{2},{3},{4},{5},{6},{7},{8},"
                // Club delivery (8)
                + "{9:F2},{10:F1},{11:F2},{12:F2},{13:F2},{14:F2},{15:F2},{16:F2},"
                // Ball launch (7)
                + "{17:F2},{18:F1},{19:F3},{20:F2},{21:F2},{22:F0},{23:F2},"
                // Flight results (5)
                + "{24:F2},{25:F1},{26:F2},{27:F2},{28:F2},"
                // Ground physics (6)
                + "{29:F2},{30:F1},{31:F2},{32:F1},{33},{34:F2},"
                // Final position (4)
                + "{35:F2},{36:F2},{37:F2},{38:F2}\n",
            // Config
            config.teeHeight.displayName,
            config.teeHeight.ballY,
            surfaceLabel,
            config.loft,
            config.drag,
            config.lift,
            config.pathAngle,
            config.faceAngle,
            config.swingPlaneTilt,
            // Club delivery
            ball.ClubSpeed,
            clubSpeedMph,
            ball.AttackAngle,
            ball.ClubPath,
            ball.FaceAngle,
            ball.DynamicLoft,
            ball.SpinLoft,
            ball.FaceToPath,
            // Ball launch
            ball.BallSpeed,
            ballSpeedMph,
            ball.SmashFactor,
            ball.LaunchAngle,
            ball.LaunchDirection,
            ball.SpinRate,
            ball.SpinAxisTilt,
            // Flight results
            ball.Carry,
            carryYds,
            ball.Apex,
            ball.FlightTime,
            ball.CurveAfterApex,
            // Ground physics
            ball.RollDistance,
            rollYds,
            ball.TotalDistance,
            totalYds,
            ball.BounceCount,
            ball.FirstBounceApex,
            // Final position
            ball.Offline,
            ball.FinalPosition.x,
            ball.FinalPosition.y,
            ball.FinalPosition.z
        );

        File.AppendAllText(csvPath, line);
    }

    public bool IsRunning() => isRunning;

    public static string GetProjectRootPath()
    {
        // Application.dataPath points to "ProjectRoot/Assets"
        string assetsPath = Application.dataPath;

        // Go one level up to get the project root
        string projectRoot = Directory.GetParent(assetsPath).FullName;

        return projectRoot;
    }

    public static string GetTestResultsPath()
    {
        string projectRoot = GetProjectRootPath();
        string testResultsPath = Path.Combine(projectRoot, "TestResults");

        // Ensure the folder exists
        if (!Directory.Exists(testResultsPath))
        {
            Directory.CreateDirectory(testResultsPath);
        }

        return testResultsPath;
    }
}
