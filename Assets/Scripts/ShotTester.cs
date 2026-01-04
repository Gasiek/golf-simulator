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
}

public class ShotTester : MonoBehaviour
{
    [Header("References")]
    public ClubDriver clubDriver;
    public BallImpactSolver ball;

    [Header("Option Arrays")]
    public float[] lofts = new float[] { 32f };
    public float[] pathAngles = new float[] { 0f };
    public float[] faceAngles = new float[] { 0f };
    public bool[] dragOptions = new bool[] { true, false };
    public bool[] liftOptions = new bool[] { true, false };

    [Header("Timing")]
    public float delayBetweenShots = 1f;

    [Header("CSV Output")]
    public string csvFileName = "ShotResults.csv";

    private List<ShotConfig> allShots = new List<ShotConfig>();
    private string csvPath;

    void Start()
    {
        if (clubDriver == null || ball == null)
        {
            Debug.LogWarning("ShotTester: Assign ClubDriver and Ball!");
            return;
        }

        csvPath = Path.Combine(Application.dataPath, csvFileName);

        // Write CSV header
        File.WriteAllText(
            csvPath,
            "Loft,Drag,Lift,PathAngle,FaceAngle,ClubSpeed,BallSpeed,LaunchAngle,SideAngle,SpinRPM,SpinX,SpinY,SpinZ,Carry,Apex,FlightTime,FinalPosX,FinalPosY,FinalPosZ\n"
        );

        GenerateAllShots();
        StartCoroutine(RunShotsSequentially());
    }

    private void GenerateAllShots()
    {
        allShots.Clear();
        foreach (var loft in lofts)
        {
            foreach (var path in pathAngles)
            {
                foreach (var face in faceAngles)
                {
                    foreach (var drag in dragOptions)
                    {
                        foreach (var lift in liftOptions)
                        {
                            allShots.Add(
                                new ShotConfig
                                {
                                    loft = loft,
                                    pathAngle = path,
                                    faceAngle = face,
                                    drag = drag,
                                    lift = lift,
                                }
                            );
                        }
                    }
                }
            }
        }

        Debug.Log($"Generated {allShots.Count} shot scenarios.");
    }

    private IEnumerator RunShotsSequentially()
    {
        Vector3 startPos = ball.transform.position;

        for (int i = 0; i < allShots.Count; i++)
        {
            var config = allShots[i];

            // Reset ball and club
            ball.ResetAndPrepare(startPos);
            clubDriver.ResetClub();

            // Apply configuration
            ball.loftDegrees = config.loft;
            ball.enableDrag = config.drag;
            ball.enableLift = config.lift;

            clubDriver.faceAngle = config.faceAngle;
            clubDriver.swingPathAngle = config.pathAngle;

            Debug.Log(
                $"=== Testing Shot {i + 1}/{allShots.Count} ===\n"
                    + $"Loft: {ball.loftDegrees}°, Drag: {ball.enableDrag}, Lift: {ball.enableLift}, "
                    + $"Path Angle: {clubDriver.swingPathAngle}°, Face Angle: {clubDriver.faceAngle}°"
            );

            // Start swing
            clubDriver.StartSwing();

            // Wait until ball lands
            while (ball.IsMoving() || !ball.IsLanded() || clubDriver.IsSwinging())
                yield return null;

            // Write shot data to CSV
            WriteShotToCSV(config);

            // Delay between shots
            yield return new WaitForSeconds(delayBetweenShots);
        }

        Debug.Log($"=== All {allShots.Count} Shots Complete ===");
        Debug.Log($"CSV saved at: {csvPath}");
    }

    private void WriteShotToCSV(ShotConfig config)
    {
        string line = string.Format(
            "{0},{1},{2},{3},{4},{5:F2},{6:F2},{7:F1},{8:F1},{9:F0},{10:F3},{11:F3},{12:F3},{13:F2},{14:F2},{15:F2},{16:F2},{17:F2},{18:F2}\n",
            config.loft,
            config.drag,
            config.lift,
            config.pathAngle,
            config.faceAngle,
            ball.ClubSpeed,
            ball.BallSpeed,
            ball.LaunchAngle,
            ball.SideAngle,
            ball.SpinRPM,
            ball.SpinVector.x,
            ball.SpinVector.y,
            ball.SpinVector.z,
            ball.Carry,
            ball.Apex,
            ball.FlightTime,
            ball.FinalPosition.x,
            ball.FinalPosition.y,
            ball.FinalPosition.z
        );

        File.AppendAllText(csvPath, line);
    }
}
