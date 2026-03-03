using UnityEngine;

/// <summary>
/// A trigger-based pressure plate that tracks whether a character is standing on it.
/// Requires a Collider on the same GameObject (set to trigger automatically).
///
/// Setup:
///   1. Attach to the platform/button GameObject.
///   2. Ensure a Collider is present — it will be forced to isTrigger = true.
///   3. Reference this component from CoopDoorSystem.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PressurePlate : MonoBehaviour
{
    /// <summary>True while any character (Movement component) is inside the trigger volume.</summary>
    public bool IsOccupied { get; private set; }

    private int _overlapCount;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<Movement>() != null)
        {
            _overlapCount++;
            IsOccupied = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Movement>() != null)
        {
            _overlapCount = Mathf.Max(0, _overlapCount - 1);
            IsOccupied = _overlapCount > 0;
        }
    }
}
