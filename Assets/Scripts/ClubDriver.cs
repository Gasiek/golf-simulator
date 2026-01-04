using System;
using UnityEngine;

[RequireComponent(typeof(Transform))]
public class ClubDriver : MonoBehaviour
{
    [Header("References")]
    public Transform Head; // Club head transform (child of ClubRoot)
    public Transform ImpactPoint; // Empty child marking sweet spot

    [Header("Swing Geometry")]
    public float swingRadius = 1.0f; // Distance from pivot (ClubRoot) to head
    public float swingHeight = 0.5f; // Vertical amplitude of arc
    public float startAngle = -90f; // Degrees (backswing)
    public float endAngle = 90f; // Degrees (follow-through)
    public float impactAngle = 0f; // Angle considered 'impact'

    [Header("Motion")]
    public float swingTempo = 1f;

    /*
 * Controls how fast the swing progresses along its parameterized arc.
 *
 * IMPORTANT:
 * - This is NOT the true club head speed.
 * - It defines how quickly the swing angle advances over time.
 * - Actual club head speed must be measured from world-space motion
 *   (see headVelocityWorld), especially at impact.
 *
 * Changing swing geometry (radius, height) WILL change the real head speed
 * even if swingTempo stays the same.
 */

    public bool autoStart = false; // Start swing on play

    [Header("Face / Path")]
    public float swingPathAngle = 0f; // Horizontal rotation of the arc (degrees)
    public float faceAngle = 0f; // Open/closed rotation around club's local Y
    public Vector2 impactOffset = Vector2.zero; // local offset for off-center hit

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

    public event Action<Vector3, Vector3, Vector3> OnImpact;

    // signature: OnImpact(impactWorldPos, headVelocityWorld, faceNormalWorld)

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

        // Linear distance along arc this frame
        float distanceThisFrame = swingTempo * Time.deltaTime;

        // Convert linear distance to angular delta
        float radius = Mathf.Max(0.0001f, swingRadius);
        float deltaThetaRad = distanceThisFrame / radius;
        float deltaThetaDeg = Mathf.Rad2Deg * deltaThetaRad * angleDirection;

        // Advance angle
        float nextAngle = currentAngle + deltaThetaDeg;

        bool reachedEnd = false;
        if (angleDirection > 0f && nextAngle >= endAngle)
        {
            nextAngle = endAngle;
            reachedEnd = true;
        }
        if (angleDirection < 0f && nextAngle <= endAngle)
        {
            nextAngle = endAngle;
            reachedEnd = true;
        }

        // Previous world pos for tangent
        Vector3 prevWorld = Head.position;

        // Compute next local position
        Vector3 nextLocal = ComputeLocalPosForAngleDeg(nextAngle);

        // Apply horizontal swingPath rotation
        Quaternion pathRot = Quaternion.Euler(0f, swingPathAngle, 0f);
        nextLocal = pathRot * nextLocal;

        Head.localPosition = nextLocal;
        Vector3 nextWorld = Head.position;

        // Compute velocity
        headVelocityWorld = (nextWorld - prevHeadWorldPos) / Time.deltaTime;
        prevHeadWorldPos = nextWorld;

        // --- Tangent and rotation ---
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

        // Impact point offset
        ImpactPoint.localPosition = new Vector3(impactOffset.x, impactOffset.y, 0f);

        // Detect impact crossing
        DetectImpact(currentAngle, nextAngle);

        // Store persistent start/end face directions
        if (!swinging && startFaceDirWorld == Vector3.zero)
        {
            startFaceDirWorld = ComputeFaceDirectionWorld(startAngle);
        }
        if (reachedEnd)
        {
            endFaceDirWorld = ComputeFaceDirectionWorld(endAngle);
        }

        currentAngle = nextAngle;

        if (drawDebug)
            DrawDebugging(prevWorld, nextWorld);

        if (reachedEnd)
            swinging = false;
    }

    private Vector3 ComputeLocalPosForAngleDeg(float angleDeg)
    {
        float rad = Mathf.Deg2Rad * angleDeg;

        // ZY-plane swing, downward first
        float z = Mathf.Sin(rad) * swingRadius;
        float y = -Mathf.Cos(rad) * swingHeight;
        float x = 0f;

        return new Vector3(x, y, z);
    }

    private Vector3 GetSwingPlaneNormal()
    {
        // The swing plane rotates horizontally by swingPathAngle
        return Quaternion.Euler(0f, swingPathAngle, 0f) * Vector3.right;
    }

    private Vector3 ComputeTangentWorld(float angle)
    {
        // Small delta along arc to compute tangent
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

    private void DetectImpact(float prevAngle, float nextAngle)
    {
        if (impactFiredThisSwing)
            return;

        bool crossed =
            (angleDirection > 0)
                ? prevAngle <= impactAngle && nextAngle >= impactAngle
                : prevAngle >= impactAngle && nextAngle <= impactAngle;

        if (crossed)
        {
            impactFiredThisSwing = true;

            Vector3 impactWorldPos = ImpactPoint.position;
            Vector3 velocityAtImpact = headVelocityWorld;
            Vector3 faceNormalWorld = Head.forward;

            OnImpact?.Invoke(impactWorldPos, velocityAtImpact, faceNormalWorld);

            Debug.Log(
                $"Impact fired! pos={impactWorldPos}, speed={velocityAtImpact.magnitude:F2} m/s, faceNormal={faceNormalWorld}"
            );
        }
    }

    private void DrawDebugging(Vector3 prevWorld, Vector3 nextWorld)
    {
        // Arc segment
        Debug.DrawLine(prevWorld, nextWorld, debugArcColor);

        // Tangent vector
        Vector3 tangentDir = (nextWorld - prevWorld).normalized;
        Debug.DrawRay(nextWorld, tangentDir * 0.5f, Color.magenta);

        // Velocity vector
        Debug.DrawRay(nextWorld, headVelocityWorld.normalized * 0.2f, debugVelocityColor);

        // Face normal from impact point
        Debug.DrawRay(ImpactPoint.position, Head.forward * 0.5f, debugFaceColor);

        // Persistent start/end face
        if (drawPersistentFace)
        {
            Debug.DrawRay(transform.position, startFaceDirWorld * 0.7f, startFaceColor); // start
            Debug.DrawRay(transform.position, endFaceDirWorld * 0.7f, endFaceColor); // end
        }

        // Start/end markers
        Vector3 startLocal =
            Quaternion.Euler(0f, swingPathAngle, 0f) * ComputeLocalPosForAngleDeg(startAngle);
        Vector3 endLocal =
            Quaternion.Euler(0f, swingPathAngle, 0f) * ComputeLocalPosForAngleDeg(endAngle);
        Debug.DrawLine(
            transform.TransformPoint(startLocal) + Vector3.up * 0.02f,
            transform.TransformPoint(startLocal) - Vector3.up * 0.02f,
            Color.white
        );
        Debug.DrawLine(
            transform.TransformPoint(endLocal) + Vector3.up * 0.02f,
            transform.TransformPoint(endLocal) - Vector3.up * 0.02f,
            Color.white
        );
    }

    // --- Public API ---
    public void StartSwing()
    {
        currentAngle = startAngle;
        prevHeadWorldPos = Head != null ? Head.position : transform.position;
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

            startFaceDirWorld = ComputeFaceDirectionWorld(startAngle);
        }
    }

    public void StopSwing() => swinging = false;

    public bool IsSwinging() => swinging;

    public Vector3 GetHeadVelocityWorld() => headVelocityWorld;

    // --- Context Menu Methods ---
    [ContextMenu("Start Swing")]
    private void ContextStartSwing() => StartSwing();

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

        // Reset persistent debug rays
        startFaceDirWorld = Vector3.zero;
        endFaceDirWorld = Vector3.zero;
    }
}
