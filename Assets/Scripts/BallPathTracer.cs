using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BallPathTracer : MonoBehaviour
{
    [Header("Tracer Settings")]
    public Material lineMaterial;
    public float minDistance = 0.01f; // Minimum distance between points
    public float fadeDelay = 3f; // Seconds after landing to clear
    public float lineWidth = 0.05f;

    private BallImpactSolver3D ball;
    private LineRenderer lineRenderer;
    private List<Vector3> points = new List<Vector3>();
    private float timeSinceStop = 0f;
    private bool ballStopped = false;
    private bool tracingActive = false;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.enabled = false; // start disabled
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        if (lineMaterial != null)
            lineRenderer.sharedMaterial = lineMaterial;

        TryGetComponent(out ball);
    }

    void Start()
    {
        if (ball != null)
            ball.OnBallLaunched += StartTracing; // subscribe to launch event
    }

    void OnDestroy()
    {
        if (ball != null)
            ball.OnBallLaunched -= StartTracing;
    }

    void Update()
    {
        if (!tracingActive || ball == null || !ball.IsMoving())
            return;

        Vector3 currentPos = ball.transform.position;

        // Only add point if far enough from last
        if (points.Count == 0 || Vector3.Distance(points[^1], currentPos) >= minDistance)
        {
            points.Add(currentPos);
            lineRenderer.positionCount = points.Count;
            lineRenderer.SetPositions(points.ToArray());
        }

        // Ball hit the ground?
        if (!ballStopped && currentPos.y <= 0.01f)
        {
            ballStopped = true;
            timeSinceStop = 0f;
        }

        // Fade after delay
        if (ballStopped)
        {
            timeSinceStop += Time.deltaTime;
            if (timeSinceStop >= fadeDelay)
            {
                ClearPath();
            }
        }
    }

    private void StartTracing()
    {
        tracingActive = true;
        ballStopped = false;
        timeSinceStop = 0f;
        points.Clear();
        lineRenderer.positionCount = 0;
        lineRenderer.enabled = true;
    }

    public void ClearPath()
    {
        points.Clear();
        lineRenderer.positionCount = 0;
        lineRenderer.enabled = false;
        tracingActive = false;
        ballStopped = false;
        timeSinceStop = 0f;
    }
}
