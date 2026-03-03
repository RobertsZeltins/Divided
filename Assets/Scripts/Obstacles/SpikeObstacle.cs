using UnityEngine;

/// <summary>
/// Kills any character that enters this trigger collider, triggering the
/// CharacterRespawnManager fade-and-teleport sequence.
///
/// Setup:
///   1. Attach to the spike GameObject (e.g. greens._42).
///   2. Ensure a Collider is present on the same GameObject — this script
///      forces it to be a trigger in Awake().
///   3. CharacterRespawnManager must exist in the scene.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SpikeObstacle : MonoBehaviour
{
    private void Awake()
    {
        // Always treat this collider as a trigger regardless of Inspector settings.
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryKill(other);
    }

    /// <summary>
    /// OnTriggerStay catches the case where a character was already inside the trigger
    /// when it was activated, or physics missed the Enter event (e.g. after a respawn
    /// put the character briefly inside the collider volume).
    /// CharacterRespawnManager's own _topRespawning / _botRespawning guards prevent
    /// double-respawn, so calling Respawn() every physics frame is safe.
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        TryKill(other);
    }

    private void TryKill(Collider other)
    {
        // Search up the hierarchy — the character's physics collider may be on a child
        // GameObject while Movement lives on the root, so GetComponent would return null.
        Movement movement = other.GetComponentInParent<Movement>();
        if (movement == null) return;
        CharacterRespawnManager.Instance?.Respawn(movement.gameObject);
    }
}
