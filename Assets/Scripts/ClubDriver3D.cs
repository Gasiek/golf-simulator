using System;
using UnityEngine;

[RequireComponent(typeof(Transform))]
public class ClubDriver3D : MonoBehaviour
{
    [Header("References")]
    public Transform Head;
    public Transform ImpactPoint;

    [Header("Swing Geometry")]
    public float swingRadius = 1.0f;
    public float swingHeight = 0.5f;
    public float startAngle = -90f;
    public float endAngle = 90f;

    [Header("Motion")]
    public float swingTempo = 1f;
    public bool autoStart = false;

    [Header("Club Face Settings")]
    [Tooltip("Static loft of the club (degrees). Driver: 10.5, 7-iron: 32, PW: 46")]
    public float clubLoftDegrees = 32f;

    [Tooltip("Face angle at impact relative to target. Positive = open/right (degrees)")]
    public float faceAngle = 0f;

    [Header("Swing Path Settings")]
    [Tooltip("Horizontal swing path direction. Positive = in-to-out/right (degrees)")]
    public float swingPathAngle = 0f;

    [Tooltip(
        "Swing plane tilt. Positive = steep/descending blow, Negative = shallow/ascending blow (degrees)"
    )]
    public float swingPlaneTilt = 0f;

    [Header("Impact Plane")]
    [Tooltip("Height of club head at impact. For ball on ground: 0.02135 (ball radius)")]
    public float impactPlaneY = 0.02135f;

    [Header("Debug")]
    public bool drawDebug = true;
    public Color debugArcColor = Color.cyan;
    public Color debugVelocityColor = Color.yellow;
    public Color debugFaceColor = Color.green;

    public bool IsSwinging() => swinging;

    /// <summary>
    /// Event fired at impact: (impactPosition, clubVelocity, faceNormal, attackAngle, faceAngleDegrees)
    /// </summary>
    public event Action<Vector3, Vector3, Vector3, float, float> OnImpact;

    private float currentAngle;
    private bool swinging;
    private bool impactFired;
    private float angleDir;
    private Vector3 prevHeadWorldPos;
    private Vector3 headVelocityWorld;
    private Vector3 clubRootOffset;

    void Start()
    {
        angleDir = endAngle >= startAngle ? 1f : -1f;
        CalculateClubRootOffset();
        ResetClub();
        if (autoStart)
            StartSwing();
    }

    void Update()
    {
        if (!swinging || Head == null)
            return;

        float dist = swingTempo * Time.deltaTime;
        float deltaAngle = Mathf.Rad2Deg * (dist / Mathf.Max(0.0001f, swingRadius)) * angleDir;
        float nextAngle = currentAngle + deltaAngle;
        if ((angleDir > 0 && nextAngle >= endAngle) || (angleDir < 0 && nextAngle <= endAngle))
        {
            nextAngle = endAngle;
            swinging = false;
        }

        Vector3 prevWorld = Head.position;

        Quaternion planeRot = Quaternion.Euler(swingPlaneTilt, swingPathAngle, 0f);
        Vector3 localPos = ComputeLocalPos(nextAngle) + clubRootOffset;
        Head.localPosition = planeRot * localPos;

        Vector3 nextWorld = Head.position;
        headVelocityWorld = (nextWorld - prevHeadWorldPos) / Time.deltaTime;
        prevHeadWorldPos = nextWorld;

        Vector3 tangent = ComputeTangentWorld(currentAngle);
        Vector3 swingNormal = planeRot * Vector3.right;

        if (tangent.sqrMagnitude > 1e-8f)
        {
            Quaternion tangentRot = Quaternion.LookRotation(tangent, swingNormal);
            Quaternion faceRot = Quaternion.AngleAxis(faceAngle, Vector3.up);
            Quaternion modelOffset = Quaternion.Euler(0f, 0f, 90f);
            Head.rotation = tangentRot * faceRot * modelOffset;
        }

        DetectImpact(prevWorld, nextWorld);
        currentAngle = nextAngle;

        if (drawDebug)
            DrawDebug(prevWorld, nextWorld);
    }

    private Vector3 ComputeLocalPos(float angleDeg)
    {
        float r = Mathf.Deg2Rad * angleDeg;
        return new Vector3(0f, -Mathf.Cos(r) * swingHeight, Mathf.Sin(r) * swingRadius);
    }

    private Vector3 ComputeTangentWorld(float angleDeg)
    {
        float d = 0.01f * angleDir;
        Quaternion planeRot = Quaternion.Euler(swingPlaneTilt, swingPathAngle, 0f);
        Vector3 p0 = planeRot * ComputeLocalPos(angleDeg) + clubRootOffset;
        Vector3 p1 = planeRot * ComputeLocalPos(angleDeg + d) + clubRootOffset;
        return (transform.TransformPoint(p1) - transform.TransformPoint(p0)).normalized;
    }

    private void CalculateClubRootOffset()
    {
        float bottomY = -Mathf.Cos(0f) * swingHeight;
        clubRootOffset = new Vector3(0f, impactPlaneY - bottomY, 0f);
    }

    private void DetectImpact(Vector3 prev, Vector3 next)
    {
        if (impactFired || ImpactPoint == null)
            return;

        GameObject ball = GameObject.FindWithTag("Ball");
        if (!ball)
            return;

        Vector3 ballPos = ball.transform.position;
        Vector3 closest = ClosestPoint(prev, next, ballPos);

        if (Vector3.Distance(ballPos, closest) <= 0.05f)
        {
            impactFired = true;
            Vector3 vel = headVelocityWorld;
            float speed = vel.magnitude;

            if (speed < 0.1f)
                return;

            // Attack angle: vertical angle of club path
            Vector3 velHorizontal = new Vector3(vel.x, 0f, vel.z);
            float horizontalSpeed = velHorizontal.magnitude;
            float attackAngle = Mathf.Atan2(vel.y, horizontalSpeed) * Mathf.Rad2Deg;

            // Face normal: apply loft then face angle
            Vector3 velDir = vel.normalized;
            Vector3 velDirHorizontal =
                velHorizontal.magnitude > 0.001f ? velHorizontal.normalized : Vector3.forward;
            Vector3 loftAxis = Vector3.Cross(Vector3.up, velDirHorizontal).normalized;

            Quaternion loftRotation = Quaternion.AngleAxis(-clubLoftDegrees, loftAxis);
            Vector3 faceNormal = loftRotation * velDir;

            Quaternion faceRotation = Quaternion.AngleAxis(faceAngle, Vector3.up);
            faceNormal = faceRotation * faceNormal;

            OnImpact?.Invoke(
                ImpactPoint.position,
                vel,
                faceNormal.normalized,
                attackAngle,
                faceAngle
            );

            if (drawDebug)
            {
                Debug.DrawRay(ImpactPoint.position, faceNormal * 0.5f, Color.magenta, 2f);
                Debug.DrawRay(ImpactPoint.position, velDir * 0.5f, Color.blue, 2f);
            }
        }
    }

    private Vector3 ClosestPoint(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Vector3.Dot(ab, ab));
        return a + ab * t;
    }

    public void StartSwing()
    {
        CalculateClubRootOffset();
        currentAngle = startAngle;
        impactFired = false;
        swinging = true;

        Quaternion planeRot = Quaternion.Euler(swingPlaneTilt, swingPathAngle, 0f);
        Head.localPosition = planeRot * (ComputeLocalPos(currentAngle) + clubRootOffset);
        prevHeadWorldPos = Head.position;
    }

    public void ResetClub()
    {
        swinging = false;
        impactFired = false;
        currentAngle = startAngle;

        if (Head != null)
            Head.localPosition = ComputeLocalPos(startAngle) + clubRootOffset;
    }

    private void DrawDebug(Vector3 prev, Vector3 next)
    {
        Debug.DrawLine(prev, next, debugArcColor);
        Debug.DrawRay(next, headVelocityWorld.normalized * 0.3f, debugVelocityColor);
        Debug.DrawRay(ImpactPoint.position, Head.forward * 0.4f, debugFaceColor);
    }
}
