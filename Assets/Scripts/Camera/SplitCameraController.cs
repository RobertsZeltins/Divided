using UnityEngine;

/// <summary>
/// Tracks one character horizontally for a split-screen camera.
/// Y and Z never change — this camera is a literal half-slice of the
/// merged camera view, just following a different X target.
/// </summary>
[RequireComponent(typeof(Camera))]
public class SplitCameraController : MonoBehaviour
{
    [Header("Follow Settings")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private float followSmoothTime = 0.18f;
    [SerializeField] private float lookAheadDistance = 1.5f;
    [SerializeField] private float lookAheadSmoothTime = 0.25f;

    [Header("Bounds (optional)")]
    [SerializeField] private bool useBounds;
    [SerializeField] private float minX;
    [SerializeField] private float maxX;

    private Rigidbody _targetRigidbody;
    private float _velocityX;
    private float _lookAheadX;
    private float _lookAheadVelocity;

    private void Start()
    {
        CacheRigidbody();
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;

        UpdateLookAhead();
        FollowTarget();
    }

    private void UpdateLookAhead()
    {
        float targetLookAhead = 0f;
        if (_targetRigidbody != null && Mathf.Abs(_targetRigidbody.linearVelocity.x) > 0.1f)
            targetLookAhead = Mathf.Sign(_targetRigidbody.linearVelocity.x) * lookAheadDistance;

        _lookAheadX = Mathf.SmoothDamp(_lookAheadX, targetLookAhead, ref _lookAheadVelocity, lookAheadSmoothTime);
    }

    private void FollowTarget()
    {
        float targetX = followTarget.position.x + _lookAheadX;

        if (useBounds)
            targetX = Mathf.Clamp(targetX, minX, maxX);

        float smoothX = Mathf.SmoothDamp(transform.position.x, targetX, ref _velocityX, followSmoothTime);

        // Only X moves — Y and Z are identical to the merged camera, preserving the exact same view
        transform.position = new Vector3(smoothX, transform.position.y, transform.position.z);
    }

    /// <summary>
    /// Instantly snaps the camera X to the given world-space position,
    /// resetting all follow velocity so there is no rubber-band effect.
    /// Call this before activating the GameObject to match the merged camera's X.
    /// </summary>
    public void SnapToX(float worldX)
    {
        Vector3 pos = transform.position;
        pos.x = worldX;
        transform.position = pos;
        _velocityX = 0f;
        _lookAheadX = 0f;
        _lookAheadVelocity = 0f;
    }

    /// <summary>Reassigns the follow target at runtime.</summary>
    public void SetFollowTarget(Transform target)
    {
        followTarget = target;
        CacheRigidbody();
    }

    private void CacheRigidbody()
    {
        _targetRigidbody = followTarget != null ? followTarget.GetComponent<Rigidbody>() : null;
    }
}
