using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages which character is currently active and routes all player input to them.
/// Handles the core Divided switching mechanic.
///
/// Input layout:
///   Move     — WASD / left stick          (InputActionReference, shared)
///   Jump     — Spacebar / South button    (InputActionReference)
///   Switch   — Tab / LB                   (InputActionReference)
///   Dig/Dash — Left Shift / RB            (InputActionReference, via digAction)
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
    [Tooltip("Action that drives dig (bottom) and dash (top). " +
             "Assign the Dig/Sprint action from InputSystem_Actions — " +
             "add both <Keyboard>/leftShift and <Gamepad>/rightShoulder bindings there.")]
    [SerializeField] private InputActionReference digAction;

    [Header("Abilities")]
    [Tooltip("DiggingAbility on the bottom character.")]
    [SerializeField] private DiggingAbility bottomDigging;

    private Movement _activeCharacter;
    private Movement _inactiveCharacter;

    public Movement ActiveCharacter => _activeCharacter;

    /// <summary>Fired when the active character changes.</summary>
    public event System.Action OnCharacterSwitched;

    private void Awake()
    {
        SetActiveCharacter(topCharacter);
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        switchAction.action.Enable();
        digAction.action.Enable();

        jumpAction.action.started   += HandleJumpStarted;
        jumpAction.action.canceled  += HandleJumpCanceled;
        switchAction.action.started += HandleSwitchStarted;
        digAction.action.started    += HandleDigStarted;
        digAction.action.canceled   += HandleDigCanceled;
    }

    private void OnDisable()
    {
        jumpAction.action.started   -= HandleJumpStarted;
        jumpAction.action.canceled  -= HandleJumpCanceled;
        switchAction.action.started -= HandleSwitchStarted;
        digAction.action.started    -= HandleDigStarted;
        digAction.action.canceled   -= HandleDigCanceled;

        moveAction.action.Disable();
        jumpAction.action.Disable();
        switchAction.action.Disable();
        digAction.action.Disable();
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
        _activeCharacter?.OnJumpPressed();
    }

    private void HandleJumpCanceled(InputAction.CallbackContext context)
    {
        _activeCharacter?.OnJumpReleased();
    }

    /// <summary>
    /// Dig/Dash is context-aware:
    ///   • Always routes to bottomDigging when the bottom character is actively
    ///     burrowing — so the player can launch it out of sand even after switching
    ///     to the top character mid-burrow.
    ///   • Routes to bottomDigging for sand entry only when bottom is the active character.
    ///   • Routes to the top character's dash when top is the active character.
    /// </summary>
    private void HandleDigStarted(InputAction.CallbackContext context)
    {
        bool bottomIsActive    = _activeCharacter == bottomCharacter;
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
