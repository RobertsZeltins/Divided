using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages which character is currently active and routes all player input to them.
/// Handles the core Divided switching mechanic.
///
/// Input layout:
///   Move     — WASD / left stick          (InputActionReference, shared)
///   Jump     — Spacebar / South button    (InputActionReference)
///   Switch   — assigned in Inspector      (InputActionReference)
///   Dig      — Left Shift                 (created in code, no .inputactions edit needed)
///
/// The Dig action is always routed to BottomDigging regardless of which
/// character is active, so the player can launch the bottom character out
/// of sand even after switching to the top character mid-burrow.
/// </summary>
public class CharacterSwitchManager : MonoBehaviour
{
    [Header("Characters")]
    [SerializeField] private Movement topCharacter;
    [SerializeField] private Movement bottomCharacter;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference switchAction;

    [Header("Abilities")]
    [Tooltip("DiggingAbility on the bottom character.")]
    [SerializeField] private DiggingAbility bottomDigging;

    // Created in code — binds Left Shift without touching the .inputactions asset.
    private InputAction _digAction;

    private Movement _activeCharacter;
    private Movement _inactiveCharacter;

    public Movement ActiveCharacter => _activeCharacter;

    /// <summary>Fired when the active character changes.</summary>
    public event System.Action OnCharacterSwitched;

    private void Awake()
    {
        _digAction = new InputAction("Dig", InputActionType.Button, "<Keyboard>/leftShift");
        SetActiveCharacter(topCharacter);
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        switchAction.action.Enable();
        _digAction.Enable();

        jumpAction.action.started  += HandleJumpStarted;
        jumpAction.action.canceled += HandleJumpCanceled;
        switchAction.action.started += HandleSwitchStarted;
        _digAction.started  += HandleDigStarted;
        _digAction.canceled += HandleDigCanceled;
    }

    private void OnDisable()
    {
        jumpAction.action.started  -= HandleJumpStarted;
        jumpAction.action.canceled -= HandleJumpCanceled;
        switchAction.action.started -= HandleSwitchStarted;
        _digAction.started  -= HandleDigStarted;
        _digAction.canceled -= HandleDigCanceled;

        moveAction.action.Disable();
        jumpAction.action.Disable();
        switchAction.action.Disable();
        _digAction.Disable();
    }

    private void Update()
    {
        if (_activeCharacter == null) return;
        float horizontal = moveAction.action.ReadValue<Vector2>().x;
        _activeCharacter.SetMoveInput(horizontal);
    }

    // ── Input handlers ────────────────────────────────────────────────────────

    private void HandleJumpStarted(InputAction.CallbackContext context)
    {
        // Spacebar is pure jump — dig is handled by the separate shift action.
        _activeCharacter?.OnJumpPressed();
    }

    private void HandleJumpCanceled(InputAction.CallbackContext context)
    {
        _activeCharacter?.OnJumpReleased();
    }

    /// <summary>
    /// Shift is context-aware:
    ///   • Always routes to bottomDigging when the bottom character is actively
    ///     burrowing — so the player can launch it out of sand even after switching
    ///     to the top character mid-burrow.
    ///   • Routes to bottomDigging for sand entry only when bottom is the active character.
    ///   • Routes to the top character's dash when top is the active character.
    /// This prevents accidentally diving the bottom character into sand while the
    /// player is trying to dash the top character.
    /// </summary>
    private void HandleDigStarted(InputAction.CallbackContext context)
    {
        bool bottomIsActive   = _activeCharacter == bottomCharacter;
        bool bottomIsBurrowing = bottomDigging != null
                              && bottomDigging.Phase != DiggingAbility.BurrowPhase.Idle;

        if (bottomIsActive || bottomIsBurrowing)
            bottomDigging?.OnDigPressed();

        if (_activeCharacter == topCharacter)
            topCharacter?.OnDashPressed();
    }

    private void HandleDigCanceled(InputAction.CallbackContext context)
    {
        bottomDigging?.OnDigReleased();
    }

    private void HandleSwitchStarted(InputAction.CallbackContext context)
    {
        SwitchCharacter();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Switches control to the other character.</summary>
    public void SwitchCharacter()
    {
        SetActiveCharacter(_activeCharacter == topCharacter ? bottomCharacter : topCharacter);
    }

    private void SetActiveCharacter(Movement target)
    {
        _activeCharacter?.ClearInput();
        _activeCharacter   = target;
        _inactiveCharacter = _activeCharacter == topCharacter ? bottomCharacter : topCharacter;
        OnCharacterSwitched?.Invoke();
    }
}
