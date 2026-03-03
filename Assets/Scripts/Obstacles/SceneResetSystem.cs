using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reloads the active scene when both reset plates are held simultaneously
/// for <see cref="holdDuration"/> seconds, resetting all state including
/// CoopDoorSystem doors, character positions, and respawn flags.
///
/// Setup:
///   1. Attach to any persistent GameObject (e.g. -GameManager).
///   2. Assign topResetPlate and bottomResetPlate in the Inspector.
///   3. Optionally adjust holdDuration and fadeOutDuration.
/// </summary>
public class SceneResetSystem : MonoBehaviour
{
    [Header("Reset Plates")]
    [Tooltip("Plate the top character must stand on to trigger a reset.")]
    [SerializeField] private PressurePlate topResetPlate;

    [Tooltip("Plate the bottom character must stand on to trigger a reset.")]
    [SerializeField] private PressurePlate bottomResetPlate;

    [Header("Timing")]
    [Tooltip("Seconds both plates must be occupied simultaneously before the reset fires.")]
    [SerializeField] private float holdDuration = 1.5f;

    [Tooltip("Seconds to wait after hold completes before the scene reloads. " +
             "Use this time to play a fade-out or audio cue.")]
    [SerializeField] private float delayBeforeReload = 0.5f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    /// <summary>0–1 progress toward completing the hold. Resets if either plate is vacated.</summary>
    public float HoldProgress => holdDuration > 0f ? Mathf.Clamp01(_holdTimer / holdDuration) : 1f;

    private float _holdTimer;
    private bool  _resetting;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_resetting) return;

        bool bothOccupied = topResetPlate    != null && topResetPlate.IsOccupied
                         && bottomResetPlate != null && bottomResetPlate.IsOccupied;

        if (bothOccupied)
        {
            _holdTimer += Time.deltaTime;

            if (_holdTimer >= holdDuration)
            {
                _resetting = true;
                StartCoroutine(ReloadRoutine());
            }
        }
        else
        {
            _holdTimer = 0f;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits <see cref="delayBeforeReload"/> seconds then reloads the active scene.
    /// The delay gives time for a fade-out effect or audio cue if desired.
    /// </summary>
    private IEnumerator ReloadRoutine()
    {
        yield return new WaitForSeconds(delayBeforeReload);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
