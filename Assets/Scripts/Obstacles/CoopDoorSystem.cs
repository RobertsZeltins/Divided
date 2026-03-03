using System.Collections;
using UnityEngine;

/// <summary>
/// Cooperative door puzzle: both doors open permanently once both pressure plates
/// have been occupied simultaneously for holdDuration seconds.
///
/// The doors slide from their initial (closed) positions by openOffset when unlocked.
/// Once open they never close again unless the game is restarted.
///
/// Setup:
///   1. Attach to any persistent GameObject (e.g. -GameManager).
///   2. Assign topPlate, bottomPlate, topDoor, bottomDoor in the Inspector.
///   3. Adjust holdDuration, openOffset, and openSpeed to taste.
///   4. Give each door a BoxCollider so characters can't pass through while closed.
///      The collider moves with the door transform and naturally clears the path on open.
/// </summary>
public class CoopDoorSystem : MonoBehaviour
{
    [Header("Pressure Plates")]
    [Tooltip("Plate that the top character must stand on.")]
    [SerializeField] private PressurePlate topPlate;

    [Tooltip("Plate that the bottom character must stand on.")]
    [SerializeField] private PressurePlate bottomPlate;

    [Header("Doors")]
    [Tooltip("Door in the top character's world.")]
    [SerializeField] private Transform topDoor;

    [Tooltip("Door in the bottom character's world.")]
    [SerializeField] private Transform bottomDoor;

    [Header("Timing")]
    [Tooltip("Seconds both plates must be simultaneously occupied before the doors open.")]
    [SerializeField] private float holdDuration = 2f;

    [Header("Door Animation")]
    [Tooltip("World-space offset applied to each door's starting position when opened. " +
             "E.g. (0, 3, 0) slides the door upward by 3 units.")]
    [SerializeField] private Vector3 openOffset = new Vector3(0f, 3f, 0f);

    [Tooltip("Speed in units per second at which the doors slide to the open position.")]
    [SerializeField] private float openSpeed = 3f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    /// <summary>True once the hold completes; doors never re-close this session.</summary>
    public bool IsUnlocked { get; private set; }

    /// <summary>0–1 progress toward completing the hold. Resets if either plate is vacated.</summary>
    public float HoldProgress => holdDuration > 0f ? _holdTimer / holdDuration : 1f;

    private float   _holdTimer;
    private Vector3 _topDoorOpenPos;
    private Vector3 _bottomDoorOpenPos;
    private bool    _animating;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (topDoor    != null) _topDoorOpenPos    = topDoor.position    + openOffset;
        if (bottomDoor != null) _bottomDoorOpenPos = bottomDoor.position + openOffset;
    }

    private void Update()
    {
        if (IsUnlocked)
        {
            // Slide both doors toward the open position every frame until they arrive.
            SlideToOpen(topDoor,    _topDoorOpenPos);
            SlideToOpen(bottomDoor, _bottomDoorOpenPos);
            return;
        }

        bool bothOccupied = topPlate    != null && topPlate.IsOccupied
                         && bottomPlate != null && bottomPlate.IsOccupied;

        if (bothOccupied)
        {
            _holdTimer += Time.deltaTime;

            if (_holdTimer >= holdDuration)
            {
                IsUnlocked = true;
            }
        }
        else
        {
            // Reset the timer whenever either plate is vacated.
            _holdTimer = 0f;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void SlideToOpen(Transform door, Vector3 target)
    {
        if (door == null) return;
        door.position = Vector3.MoveTowards(door.position, target, openSpeed * Time.deltaTime);
    }
}
