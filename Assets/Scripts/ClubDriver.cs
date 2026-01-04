using System;
using UnityEngine;

[RequireComponent(typeof(Transform))]
public class ClubDriver : MonoBehaviour
{
    [Header("References")]
    public Transform Head;
    public Transform ImpactPoint;

    [Header("Swing Geometry")]
    public float swingRadius = 1.0f;
    public float swingHeight = 0.5f;
    public float startAngle = -90f;
    public float endAngle = 90f;
    public float impactDetectionRadius = 0.05f;

    [Header("Motion")]
    public float swingTempo = 1f; // NOT real head speed
    public bool autoStart = false;

    [Header("Face / Path")]
    public float swingPathAngle = 0f;
    public float faceAngle = 0f;
    public Vector2 impactOffset = Vector2.zero;

    [Header("Debug")]
    public bool drawDebug = true;
    public Color debugArcColor = Color.cyan;
    public Color debugVelocityColor = Color.yellow;
    public Color debugFaceColor = Color.green;

    [Header("Debug Persistent")]
    public bool drawPersistentFace = true;
    public Color startFaceColor = Color.red;
    public Color endFaceColor = Color.yellow;
    public Vector3 startFaceDirWorld;
    public Vector3 endFaceDirWorld;

    [Header("Debug Testing")]
    public bool logImpactDetails = true;
    public bool drawSwingArcGizmos = true;

    public event Action<Vector3, Vector3, Vector3> OnImpact;

    private float currentAngle;
    private bool swinging = false;
    private Vector3 prevHeadWorldPos;
    private Vector3 headVelocityWorld;
    private bool impactFiredThisSwing = false;
    private float angleDirection = 1f;

    void Start()
    {
        if (Head == null || ImpactPoint == null)
            Debug.LogWarning("ClubDriver: Head or ImpactPoint not assigned.");

        angleDirection = (endAngle >= startAngle) ? 1f : -1f;
        currentAngle = startAngle;
        ResetClub();

        if (autoStart)
            StartSwing();
    }

    void Update()
    {
        if (!swinging || Head == null)
            return;

        // Distance along arc this frame
        float distanceThisFrame = swingTempo * Time.deltaTime;
        float radius = Mathf.Max(0.0001f, swingRadius);
        float deltaThetaDeg = Mathf.Rad2Deg * (distanceThisFrame / radius) * angleDirection;

        float nextAngle = currentAngle + deltaThetaDeg;
        bool reachedEnd = false;
        if (
            (angleDirection > 0f && nextAngle >= endAngle)
            || (angleDirection < 0f && nextAngle <= endAngle)
        )
        {
            nextAngle = endAngle;
            reachedEnd = true;
        }

        Vector3 prevWorld = Head.position;

        // Compute next local position
        Vector3 nextLocal = ComputeLocalPosForAngleDeg(nextAngle);
        nextLocal = Quaternion.Euler(0f, swingPathAngle, 0f) * nextLocal;
        Head.localPosition = nextLocal;
        Vector3 nextWorld = Head.position;

        // Compute velocity
        headVelocityWorld = (nextWorld - prevHeadWorldPos) / Time.deltaTime;
        prevHeadWorldPos = nextWorld;

        // Rotation
        Vector3 tangentWorld = ComputeTangentWorld(currentAngle);
        if (tangentWorld.sqrMagnitude > 1e-8f)
        {
            Quaternion tangentRotation = Quaternion.LookRotation(
                tangentWorld,
                GetSwingPlaneNormal()
            );
            Quaternion uprightFix = Quaternion.Euler(0f, 0f, 90f);
            Quaternion faceRotation = Quaternion.AngleAxis(faceAngle, Vector3.up);
            Head.rotation = tangentRotation * uprightFix * faceRotation;
        }

        // Impact offset
        ImpactPoint.localPosition = new Vector3(impactOffset.x, impactOffset.y, 0f);

        // Sub-step distance-based impact detection
        DetectImpactDistanceSubstep(prevWorld, nextWorld);

        // Persistent face directions
        if (reachedEnd)
            endFaceDirWorld = ComputeFaceDirectionWorld(endAngle);

        currentAngle = nextAngle;

        if (drawDebug)
            DrawDebugging(prevWorld, nextWorld);

        if (reachedEnd)
            swinging = false;
    }

    private Vector3 ComputeLocalPosForAngleDeg(float angleDeg)
    {
        float rad = Mathf.Deg2Rad * angleDeg;
        float z = Mathf.Sin(rad) * swingRadius;
        float y = -Mathf.Cos(rad) * swingHeight;
        return new Vector3(0f, y, z);
    }

    private Vector3 GetSwingPlaneNormal()
    {
        return Quaternion.Euler(0f, swingPathAngle, 0f) * Vector3.right;
    }

    private Vector3 ComputeTangentWorld(float angle)
    {
        float delta = 0.01f * angleDirection;
        Vector3 currentPos =
            Quaternion.Euler(0f, swingPathAngle, 0f) * ComputeLocalPosForAngleDeg(angle);
        Vector3 nextPos =
            Quaternion.Euler(0f, swingPathAngle, 0f) * ComputeLocalPosForAngleDeg(angle + delta);
        return (
            transform.TransformPoint(nextPos) - transform.TransformPoint(currentPos)
        ).normalized;
    }

    private Vector3 ComputeFaceDirectionWorld(float angle)
    {
        Vector3 tangent = ComputeTangentWorld(angle);
        Quaternion tangentRotation = Quaternion.LookRotation(tangent, GetSwingPlaneNormal());
        Quaternion uprightFix = Quaternion.Euler(0f, 0f, 90f);
        Quaternion faceRot = Quaternion.AngleAxis(faceAngle, Vector3.up);
        return (tangentRotation * uprightFix * faceRot) * Vector3.forward;
    }

    private void DetectImpactDistanceSubstep(Vector3 prevPos, Vector3 nextPos)
    {
        if (impactFiredThisSwing || ImpactPoint == null)
            return;

        GameObject ball = GameObject.FindWithTag("Ball");
        if (ball == null)
            return;

        Vector3 ballPos = ball.transform.position;
        Vector3 closest = ClosestPointOnLineSegment(prevPos, nextPos, ballPos);
        float dist = Vector3.Distance(ballPos, closest);

        if (dist <= impactDetectionRadius)
        {
            impactFiredThisSwing = true;
            Vector3 impactWorldPos = ImpactPoint.position;
            Vector3 velocityAtImpact = headVelocityWorld;
            Vector3 faceNormalWorld = Head.forward;
            float vNormal = Vector3.Dot(velocityAtImpact, faceNormalWorld);

            OnImpact?.Invoke(impactWorldPos, velocityAtImpact, faceNormalWorld);

            if (logImpactDetails)
                Debug.Log(
                    $"Impact fired! pos={impactWorldPos}, speed={velocityAtImpact.magnitude:F2}, vNormal={vNormal:F2}, faceNormal={faceNormalWorld}"
                );

            // Draw line to ball
            Debug.DrawLine(impactWorldPos, ballPos, Color.magenta, 2f);
        }
    }

    private Vector3 ClosestPointOnLineSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(point - a, ab) / Vector3.Dot(ab, ab);
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    private void DrawDebugging(Vector3 prevWorld, Vector3 nextWorld)
    {
        Debug.DrawLine(prevWorld, nextWorld, debugArcColor);
        Debug.DrawRay(nextWorld, headVelocityWorld.normalized * 0.2f, debugVelocityColor);
        Debug.DrawRay(ImpactPoint.position, Head.forward * 0.5f, debugFaceColor);

        if (drawPersistentFace)
        {
            Debug.DrawRay(transform.position, startFaceDirWorld * 0.7f, startFaceColor);
            Debug.DrawRay(transform.position, endFaceDirWorld * 0.7f, endFaceColor);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawSwingArcGizmos || Head == null)
            return;

        Gizmos.color = Color.cyan;
        Vector3 prev = transform.TransformPoint(ComputeLocalPosForAngleDeg(startAngle));
        int steps = 50;
        for (int i = 1; i <= steps; i++)
        {
            float angle = Mathf.Lerp(startAngle, endAngle, i / (float)steps);
            Vector3 next = transform.TransformPoint(ComputeLocalPosForAngleDeg(angle));
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    // --- Public API ---
    public void StartSwing()
    {
        currentAngle = startAngle;
        impactFiredThisSwing = false;
        swinging = true;

        if (Head != null)
        {
            Vector3 initLocal =
                Quaternion.Euler(0f, swingPathAngle, 0f) * ComputeLocalPosForAngleDeg(currentAngle);
            Head.localPosition = initLocal;

            Vector3 tangentWorld = ComputeTangentWorld(currentAngle);
            Quaternion tangentRotation = Quaternion.LookRotation(
                tangentWorld,
                GetSwingPlaneNormal()
            );
            Quaternion uprightFix = Quaternion.Euler(0f, 0f, 90f);
            Quaternion faceRot = Quaternion.AngleAxis(faceAngle, Vector3.up);
            Head.rotation = tangentRotation * uprightFix * faceRot;

            prevHeadWorldPos = Head.position;
            startFaceDirWorld = ComputeFaceDirectionWorld(startAngle);
        }
    }

    public void StopSwing() => swinging = false;

    public bool IsSwinging() => swinging;

    public Vector3 GetHeadVelocityWorld() => headVelocityWorld;

    [ContextMenu("Reset Club")]
    private void ResetClub()
    {
        currentAngle = startAngle;
        impactFiredThisSwing = false;
        swinging = false;

        if (Head != null)
        {
            Head.localPosition = ComputeLocalPosForAngleDeg(startAngle);
            Head.localRotation = Quaternion.identity;
        }

        if (ImpactPoint != null)
            ImpactPoint.localPosition = Vector3.zero;

        startFaceDirWorld = Vector3.zero;
        endFaceDirWorld = Vector3.zero;
    }
}
