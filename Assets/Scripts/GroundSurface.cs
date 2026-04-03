using UnityEngine;

/// <summary>
/// Attach to ground objects to define their surface properties.
/// Requires a Collider for raycast detection.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GroundSurface : MonoBehaviour
{
    private static RaycastHit[] s_raycastHits = new RaycastHit[24];

    [Tooltip("Surface preset defining bounce and roll properties")]
    public SurfacePreset surfacePreset;

    [Tooltip("Layers used when raycasting for mesh normals. Default: all layers.")]
    public LayerMask normalRaycastLayers = ~0;

    /// <summary>
    /// Get the surface normal at a point (for slopes)
    /// </summary>
    public Vector3 GetSurfaceNormal(Vector3 point)
    {
        Collider col = GetComponent<Collider>();

        if (col is MeshCollider meshCol && !meshCol.convex)
        {
            Vector3 origin = point + Vector3.up * 0.1f;
            const float maxDist = 0.5f;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                s_raycastHits,
                maxDist,
                normalRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            float bestDist = float.MaxValue;
            Vector3 bestNormal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                ref RaycastHit h = ref s_raycastHits[i];
                if (h.collider != col)
                    continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    bestNormal = h.normal;
                }
            }

            if (bestDist < float.MaxValue)
                return bestNormal;
        }

        return Vector3.up;
    }
}
