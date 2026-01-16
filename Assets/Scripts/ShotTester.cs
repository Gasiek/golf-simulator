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
}

public class ShotTester : MonoBehaviour
{
    [Header("References")]
    public ClubDriver3D clubDriver;
    public BallImpactSolver3D ball;

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

        csvPath = Path.Combine(GetTestResultsPath(), csvFileName);

        // Write CSV header with D-Plane parameters
        File.WriteAllText(
            csvPath,
            // Config columns
            "ConfigLoft,ConfigDrag,ConfigLift,ConfigPathAngle,ConfigFaceAngle,ConfigSwingPlaneTilt,"
                // Club delivery (D-Plane inputs)
                + "ClubSpeed_mps,ClubSpeed_mph,AttackAngle,ClubPath,FaceAngle,DynamicLoft,SpinLoft,FaceToPath,"
                // Ball launch (D-Plane outputs)
                + "BallSpeed_mps,BallSpeed_mph,SmashFactor,LaunchAngle,LaunchDirection,SpinRate_rpm,SpinAxisTilt,"
                // Flight results
                + "Carry_m,Carry_yds,Offline_m,Apex_m,FlightTime_s,CurveAfterApex_m,"
                // Final position
                + "FinalPosX,FinalPosY,FinalPosZ,ApexPosX,ApexPosZ\n"
        );

        GenerateAllShots();
        StartCoroutine(RunShotsSequentially());
    }

    private void GenerateAllShots()
    {
        allShots.Clear();
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
                }
            );
        }

        Debug.Log($"[ShotTester] Generated {allShots.Count} shot scenarios.");
    }

    private IEnumerator RunShotsSequentially()
    {
        isRunning = true;
        Vector3 startPos = ball.transform.position;

        Debug.Log($"[ShotTester] Starting test sequence. Ball start position: {startPos}");

        for (int i = 0; i < allShots.Count; i++)
        {
            var config = allShots[i];

            Debug.Log(
                $"═══════════════════════════════════════\n"
                    + $"  SHOT {i + 1}/{allShots.Count}\n"
                    + $"═══════════════════════════════════════\n"
                    + $"  Loft: {config.loft}°\n"
                    + $"  Path Angle: {config.pathAngle}°\n"
                    + $"  Face Angle: {config.faceAngle}°\n"
                    + $"  Swing Plane Tilt: {config.swingPlaneTilt}°\n"
                    + $"  Drag: {config.drag}, Lift: {config.lift}"
            );

            // Step 1: Reset ball to starting position
            ball.ResetAndPrepare(startPos);

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
                Debug.Log($"[ShotTester] Impact detected. Ball IsMoving: {ball.IsMoving()}");

            // Step 7: Wait for ball to land
            timeout = Time.time + maxShotTime;
            while (!ball.IsLanded())
            {
                if (Time.time > timeout)
                {
                    Debug.LogError(
                        $"[ShotTester] Timeout waiting for ball to land on shot {i + 1}"
                    );
                    break;
                }
                yield return null;
            }

            if (verboseLogging)
                Debug.Log($"[ShotTester] Ball landed. Final position: {ball.FinalPosition}");

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

        string line = string.Format(
            // Config columns (6)
            "{0},{1},{2},{3},{4},{5},"
                // Club delivery (8)
                + "{6:F2},{7:F1},{8:F2},{9:F2},{10:F2},{11:F2},{12:F2},{13:F2},"
                // Ball launch (7)
                + "{14:F2},{15:F1},{16:F3},{17:F2},{18:F2},{19:F0},{20:F2},"
                // Flight results (6)
                + "{21:F2},{22:F1},{23:F2},{24:F2},{25:F2},{26:F2},"
                // Final position (5)
                + "{27:F2},{28:F2},{29:F2},{30:F2},{31:F2}\n",
            // Config
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
            ball.Offline,
            ball.Apex,
            ball.FlightTime,
            ball.CurveAfterApex,
            // Final position
            ball.FinalPosition.x,
            ball.FinalPosition.y,
            ball.FinalPosition.z,
            ball.ApexPosition.x,
            ball.ApexPosition.z
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
