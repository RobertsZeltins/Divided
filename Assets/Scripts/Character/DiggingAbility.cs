using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ori-style sand burrow ability for the bottom character.
///
/// Three-phase state machine:
///
///   ENTERING  — Character dives into sand at high speed. Committed on press;
///               a second press during entry is ignored.
///
///   BURROWING — Slow, floaty steering through the sand using analog input.
///               The last non-zero input direction is remembered for the launch.
///               Press the dig button again at any time to launch out at full
///               speed. The natural boundary exit (OnTriggerExit) also launches
///               automatically.
///
///   EXIT (instant) — Full-strength launch in the current burrowing direction.
///
/// Setup:
///   1. Attach alongside Movement on BottomCharacter.
///   2. Assign the same Move InputActionReference used by CharacterSwitchManager.
///   3. Set Diggable Layer to the layer your DiggableWall objects are on.
///   4. Wire to CharacterSwitchManager → Bottom Digging slot.
///   5. Uncheck canJump on BottomCharacter's Movement component.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DiggingAbility : MonoBehaviour
{
    // ── Phase ─────────────────────────────────────────────────────────────────

    public enum BurrowPhase { Idle, Entering, Burrowing }

    /// <summary>Current phase of the burrow state machine.</summary>
    public BurrowPhase Phase { get; private set; } = BurrowPhase.Idle;

    /// <summary>True in any non-Idle phase.</summary>
    public bool IsBurrowing => Phase != BurrowPhase.Idle;
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Entry — Dive into sand")]
    [Tooltip("Initial speed of the dive impulse. ~2–2.5× normal moveSpeed feels right.")]
    [SerializeField] private float entrySpeed = 20f;

    [Tooltip("Linear drag during entry. High = rapid deceleration (sand resistance feel).")]
    [SerializeField] private float entryDrag = 10f;

    [Tooltip("Speed at which entry transitions to the burrowing steering phase.")]
    [SerializeField] private float entryToburrowThreshold = 3.2f;

    [Header("Burrowing — Steering through sand")]
    [Tooltip("Maximum drift speed in any direction while burrowing.")]
    [SerializeField] private float burrowMaxSpeed = 4.5f;

    [Tooltip("Rate at which velocity steers toward input direction (units/s²). " +
             "Lower = more sand inertia, higher = snappier turns.")]
    [SerializeField] private float burrowSteering = 28f;

    [Tooltip("Linear drag during burrowing. Low keeps momentum; high makes it sluggish.")]
    [SerializeField] private float burrowDrag = 2f;

    [Header("Exit — Launch out of sand")]
    [Tooltip("Launch speed applied when the player presses shift near a sand edge. ~2–3× burrowMaxSpeed.")]
    [SerializeField] private float exitLaunchSpeed = 18f;

    [Tooltip("How close to the sand wall boundary (in the current burrowing direction) the character " +
             "must be before shift-to-launch activates. At burrowMaxSpeed 4.5 a value of 1.5 gives " +
             "~0.33 s of window before the natural trigger exit fires.")]
    [SerializeField] private float edgeLaunchDetectionRadius = 1.5f;

    [Header("Detection")]
    [Tooltip("Radius of the overlap sphere used to detect adjacent DiggableWall objects.")]
    [SerializeField] private float detectionRadius = 1.5f;

    [Tooltip("Layer mask that DiggableWall GameObjects are placed on.")]
    [SerializeField] private LayerMask diggableLayer;

    [Header("Input")]
    [Tooltip("The Move InputActionReference from your Input Action Asset.")]
    [SerializeField] private InputActionReference moveAction;

    [Header("References")]
    [SerializeField] private Movement movement;

    [Header("Visuals")]
    [Tooltip("BurrowVisuals component on this character. Manages the sprite/drill swap " +
             "and the sand trail. Auto-found on this GameObject if unset.")]
    [SerializeField] private BurrowVisuals burrowVisuals;

    [Header("VFX — all optional")]
    [Tooltip("One-shot burst played on entry into sand.")]
    [SerializeField] private ParticleSystem entryBurstParticles;

    [Tooltip("Looping particles played throughout the burrowing phase.")]
    [SerializeField] private ParticleSystem burrowLoopParticles;

    [Tooltip("One-shot burst played on exit/launch.")]
    [SerializeField] private ParticleSystem exitBurstParticles;

    // ── Private state ─────────────────────────────────────────────────────────

    private Rigidbody _rb;
    private Collider _ownCollider;
    private DiggableWall _currentWall;
    private float _originalDrag;

    // Last non-zero burrowing direction — preserved so a momentary zero-input
    // frame at the sand edge does not zero-out the exit launch direction.
    private Vector2 _lastBurrowDirection;

    // When a shift-launch fires, the character is still physically inside the
    // burrowZone trigger. Storing the wall here lets OnTriggerExit clean up the
    // collider ignore state AFTER the character has fully left the solid, so the
    // solid wall cannot apply depenetration forces that fight the launch velocity.
    private DiggableWall _pendingExitWall;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>
    /// True when the character is burrowing AND is within edgeLaunchDetectionRadius
    /// of any face of the sand wall. Read by BurrowLaunchIndicator to show the UI ring.
    /// </summary>
    public bool LaunchAvailable { get; private set; }

    /// <summary>
    /// True when the character is idle, the exit cooldown has elapsed, and there is at least
    /// one DiggableWall within detectionRadius. Read by BurrowLaunchIndicator to show a
    /// proximity ring before the character has entered the sand.
    /// </summary>
    public bool IsNearSandWall { get; private set; }

    // After any exit (natural or launched) block TryEnterSand for this many seconds.
    private const float ExitCooldownDuration = 0.5f;
    private float _exitCooldownTimer;

    // Set by OnDigPressed (InputSystem callback, which runs after FixedUpdate).
    // Consumed at the TOP of FixedUpdateBurrowing (which runs before physics and
    // trigger events) so the launch always fires before any natural-exit path.
    private bool _launchRequested;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb           = GetComponent<Rigidbody>();
        _ownCollider  = GetComponent<Collider>();
        _originalDrag = _rb.linearDamping;

        if (movement == null)
            movement = GetComponent<Movement>();

        if (burrowVisuals == null)
            burrowVisuals = GetComponent<BurrowVisuals>();
    }

    private void Update()
    {
        if (_exitCooldownTimer > 0f)
            _exitCooldownTimer -= Time.deltaTime;

        LaunchAvailable = Phase == BurrowPhase.Burrowing && IsNearSandEdge();

        // Show the proximity ring before the character has entered the sand.
        IsNearSandWall = Phase == BurrowPhase.Idle
                      && _exitCooldownTimer <= 0f
                      && Physics.CheckSphere(transform.position, detectionRadius, diggableLayer);
    }

    private void FixedUpdate()
    {
        switch (Phase)
        {
            case BurrowPhase.Entering:  FixedUpdateEntering();  break;
            case BurrowPhase.Burrowing: FixedUpdateBurrowing(); break;
        }
    }

    // ── Public API — called by CharacterSwitchManager ─────────────────────────

    /// <summary>
    /// First press while Idle: dive into adjacent sand.
    /// Second press while Burrowing: launch at full speed only when near a sand edge.
    /// Mid-wall presses are ignored — the burrow cannot be cancelled prematurely.
    /// Press during Entering is ignored — the dive is committed.
    /// </summary>
    public void OnDigPressed()
    {
        switch (Phase)
        {
            case BurrowPhase.Idle:
                TryEnterSand();
                break;

            case BurrowPhase.Burrowing:
                // Do NOT call ExitSandWithLaunch() here directly.
                // InputSystem callbacks fire AFTER FixedUpdate, so by the time we get
                // here, the FixedUpdate bounds-check or OnTriggerExit may have already
                // set Phase = Idle and ExitSandWithLaunch would return immediately.
                //
                // Instead, store a flag. FixedUpdateBurrowing reads it at the TOP of
                // the very next FixedUpdate pass — before physics simulation, before
                // trigger events, before the natural-exit bounds check — guaranteeing
                // the launch fires while Phase is still Burrowing.
                if (IsNearSandEdge())
                    _launchRequested = true;
                break;

            // Entering: dive is committed, second press is ignored.
        }
    }

    /// <summary>
    /// Holding the dig button is not required — release has no effect.
    /// </summary>
    public void OnDigReleased() { }

    // ── Called by DiggableWall ─────────────────────────────────────────────────

    /// <summary>
    /// Called by DiggableWall.OnTriggerExit when the character's collider leaves the burrow zone.
    ///
    /// Two cases:
    ///   1. Pending launch  — Shift was already pressed and the character flew out under the
    ///      launch impulse. Solid-collider collision was kept ignored while in flight to prevent
    ///      depenetration forces fighting the velocity. Now that we have physically cleared the
    ///      trigger we restore it safely.
    ///   2. Natural exit    — No launch was fired; restore physics and keep current velocity so
    ///      the character drifts out the other side and falls under gravity.
    /// </summary>
    public void OnExitedSandVolume(DiggableWall caller)
    {
        if (_pendingExitWall == caller)
        {
            // Launch already fired — restore collision now that we've physically cleared
            // the burrow zone trigger. Doing it here (not in ExitSandWithLaunch) prevents
            // the solid wall's depenetration force from fighting the launch velocity.
            caller.UnregisterBurrower(_ownCollider);
            _pendingExitWall = null;
            return;
        }

        // Natural exit via trigger boundary. The FixedUpdateBurrowing solid-bounds check
        // usually fires first, but this is a safety net for fast movement or edge cases
        // where the physics step integrates the character past the solid in one step.
        if (Phase == BurrowPhase.Idle) return;
        ExitSandNaturally();
    }

    // ── Phase: ENTERING ───────────────────────────────────────────────────────

    private void FixedUpdateEntering()
    {
        Vector3 vel = _rb.linearVelocity;
        if (vel.sqrMagnitude > 0.1f)
        {
            _lastBurrowDirection = new Vector2(vel.x, vel.y).normalized;
            burrowVisuals?.SetBurrowDirection(_lastBurrowDirection);
        }

        if (_rb.linearVelocity.magnitude <= entryToburrowThreshold)
            TransitionToBurrowing();
    }

    // ── Phase: BURROWING ──────────────────────────────────────────────────────

    private void FixedUpdateBurrowing()
    {
        // ── 1. Pending launch request ─────────────────────────────────────────
        // Must be first — before the bounds check and before Unity fires trigger
        // events for this physics step. This is why the flag exists: InputSystem
        // callbacks run after FixedUpdate, so a direct call from OnDigPressed would
        // always arrive too late. By deferring to the next FixedUpdate we guarantee
        // the launch fires while Phase is still Burrowing.
        if (_launchRequested)
        {
            _launchRequested = false;
            if (IsNearSandEdge())
            {
                ExitSandWithLaunch();
                return;
            }
        }

        // ── 2. Natural boundary exit ──────────────────────────────────────────
        // Fires as soon as the character's centre crosses the solid-collider face.
        // Using SandBounds (solid) guarantees collision can be safely restored —
        // the character is already outside the solid when UnregisterBurrower runs.
        if (_currentWall != null)
        {
            Bounds solid = _currentWall.SandBounds;
            Vector3 pos  = transform.position;
            bool outside = pos.x < solid.min.x || pos.x > solid.max.x
                        || pos.y < solid.min.y || pos.y > solid.max.y;
            if (outside)
            {
                ExitSandNaturally();
                return;
            }
        }

        // ── 3. Burrowing steering ─────────────────────────────────────────────
        Vector2 input = ReadInput();

        if (input.sqrMagnitude > 0.04f)
        {
            _lastBurrowDirection = input.normalized;
            burrowVisuals?.SetBurrowDirection(_lastBurrowDirection);
        }

        Vector3 target = new Vector3(
            input.x * burrowMaxSpeed,
            input.y * burrowMaxSpeed,
            0f);

        _rb.linearVelocity = Vector3.MoveTowards(
            _rb.linearVelocity, target, burrowSteering * Time.fixedDeltaTime);
    }

    // ── Transitions ───────────────────────────────────────────────────────────

    private void TryEnterSand()
    {
        // Block re-entry for ExitCooldownDuration after any exit.
        // This prevents the race where OnTriggerExit sets Phase=Idle during FixedUpdate
        // and the same-frame shift press calls TryEnterSand instead of ExitSandWithLaunch.
        if (_exitCooldownTimer > 0f) return;

        // Resolve entry direction first — it drives both the probe and the dive impulse.
        Vector2 input    = ReadInput();
        Vector2 entryDir = input.sqrMagnitude > 0.09f
            ? input.normalized
            : new Vector2(movement.facingDirection, 0f);

        Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, diggableLayer);
        foreach (Collider hit in hits)
        {
            DiggableWall wall = hit.GetComponent<DiggableWall>();
            if (wall == null) continue;

            // Validate: a point one detectionRadius ahead in the entry direction
            // must land inside the wall's XY footprint. This prevents diving when
            // the character is running along a wall face or standing just outside
            // a corner — situations where the impulse would push them beside the
            // wall rather than into it, leaving them stuck burrowing in open air.
            Vector3 probe = transform.position
                          + new Vector3(entryDir.x, entryDir.y, 0f) * detectionRadius;

            Bounds b = wall.BurrowZoneBounds;
            bool probeInsideXY = probe.x > b.min.x && probe.x < b.max.x
                              && probe.y > b.min.y && probe.y < b.max.y;

            if (probeInsideXY)
            {
                EnterSand(wall, entryDir);
                return;
            }
        }
    }

    private void EnterSand(DiggableWall wall, Vector2 entryDir)
    {
        Phase        = BurrowPhase.Entering;
        _currentWall = wall;

        _exitCooldownTimer = 0f;
        _launchRequested   = false;

        movement.IsBurrowing = true;
        _rb.useGravity       = false;
        _rb.linearDamping    = entryDrag;

        _lastBurrowDirection = entryDir;

        // Override velocity entirely — the dive impulse ignores run-up velocity
        // for a punchy, deliberate entry feel.
        _rb.linearVelocity = new Vector3(
            entryDir.x * entrySpeed,
            entryDir.y * entrySpeed,
            0f);

        wall.RegisterBurrower(_ownCollider);

        burrowVisuals?.OnBurrowStart(entryDir, wall);
        if (entryBurstParticles != null) entryBurstParticles.Play();
        if (burrowLoopParticles != null) burrowLoopParticles.Play();
    }

    private void TransitionToBurrowing()
    {
        Phase             = BurrowPhase.Burrowing;
        _rb.linearDamping = burrowDrag;
    }

    private void ExitSandWithLaunch()
    {
        if (Phase == BurrowPhase.Idle) return;

        Vector2 exitDir = GetExitDirection();

        Phase                = BurrowPhase.Idle;
        _launchRequested     = false;
        movement.IsBurrowing = false;
        _rb.useGravity       = true;
        _rb.linearDamping    = _originalDrag;

        _rb.linearVelocity = new Vector3(
            exitDir.x * exitLaunchSpeed,
            exitDir.y * exitLaunchSpeed,
            0f);

        // Prevent Movement.HandleHorizontalMovement from zeroing the launch velocity
        // in the same or next FixedUpdate step. Reuses Movement's wallJumpControlTimer
        // logic: while the timer is positive, horizontal velocity is preserved as-is.
        movement.SetLaunchImpulse(0.3f);

        _pendingExitWall   = _currentWall;
        _currentWall       = null;
        _exitCooldownTimer = ExitCooldownDuration;

        burrowVisuals?.OnBurrowEnd();
        if (burrowLoopParticles != null) burrowLoopParticles.Stop();
        if (exitBurstParticles  != null) exitBurstParticles.Play();
    }

    /// <summary>
    /// Returns the launch direction in priority order:
    ///   1. Live WASD input — lets the player deliberately aim.
    ///   2. Last burrowing direction — the direction the character was actually heading
    ///      through the sand (Ori-style: you exit the way you were going).
    ///   3. Nearest wall face — absolute geometric fallback; should rarely be reached.
    /// </summary>
    private Vector2 GetExitDirection()
    {
        // 1. Live input
        Vector2 input = ReadInput();
        if (input.sqrMagnitude > 0.09f)
            return input.normalized;

        // 2. Last burrowing direction (set during Entering from velocity, then from input).
        //    This is the direction that feels right — the character exits the way it was moving.
        //    The nearest-face approach was wrong: near the bottom face of a wall at floor level
        //    it returned Vector2.down and the character shot into the ground and stopped.
        if (_lastBurrowDirection.sqrMagnitude > 0.01f)
            return _lastBurrowDirection;

        // 3. Nearest face — only if _lastBurrowDirection is somehow zero.
        if (_currentWall != null)
        {
            Bounds b  = _currentWall.BurrowZoneBounds;
            Vector3 p = transform.position;

            float dLeft   = p.x - b.min.x;
            float dRight  = b.max.x - p.x;
            float dBottom = p.y - b.min.y;
            float dTop    = b.max.y - p.y;

            float minDist = Mathf.Min(dLeft, dRight, dBottom, dTop);

            if      (minDist == dLeft)   return Vector2.left;
            else if (minDist == dRight)  return Vector2.right;
            else if (minDist == dBottom) return Vector2.down;
            else                         return Vector2.up;
        }

        return new Vector2(movement.facingDirection, 0f);
    }

    /// <summary>
    /// Natural boundary exit — restores physics but keeps the current burrowing
    /// velocity so the character drifts out and falls under gravity with no extra impulse.
    /// </summary>
    private void ExitSandNaturally()
    {
        if (Phase == BurrowPhase.Idle) return;
        // Preserve whatever velocity the character had while burrowing.
        Vector3 exitVelocity = _rb.linearVelocity;
        RestorePhysics();
        _rb.linearVelocity = exitVelocity;
    }

    /// <summary>Tears down the burrow state and restores normal physics. Does not set velocity.</summary>
    private void RestorePhysics()
    {
        Phase            = BurrowPhase.Idle;
        _launchRequested = false; // discard any pending request from this session

        movement.IsBurrowing = false;
        _rb.useGravity       = true;
        _rb.linearDamping    = _originalDrag;

        _currentWall?.UnregisterBurrower(_ownCollider);
        _currentWall = null;

        _exitCooldownTimer = ExitCooldownDuration;

        burrowVisuals?.OnBurrowEnd();
        if (burrowLoopParticles != null) burrowLoopParticles.Stop();
        if (exitBurstParticles  != null) exitBurstParticles.Play();
    }

    /// <summary>
    /// Forces an immediate exit from any burrow phase. Called by CharacterRespawnManager on death
    /// so the character is fully reset before teleporting to the spawn point.
    /// </summary>
    public void ForceExitBurrow()
    {
        if (Phase == BurrowPhase.Idle) return;

        RestorePhysics();

        // Skip the standard cooldown so the player can burrow again immediately after respawn.
        _exitCooldownTimer = 0f;
    }

    /// <summary>
    /// Returns true when the character is within edgeLaunchDetectionRadius of the nearest
    /// face of the burrow zone bounds on either the X or Y axis.
    ///
    /// Direction-agnostic — does NOT use _lastBurrowDirection so it fires correctly
    /// regardless of which direction the player is moving or whether input is zero.
    /// The forward-point / direction approach was unreliable because the "escape face"
    /// is often perpendicular to the current movement direction.
    /// </summary>
    private bool IsNearSandEdge()
    {
        if (_currentWall == null) return false;

        Bounds b = _currentWall.BurrowZoneBounds;
        if (b.size == Vector3.zero) return false;

        Vector3 pos = transform.position;

        // Distance to the nearest face on X and Y separately.
        // Positive values mean the character is inside the wall on that axis.
        float dx = Mathf.Min(pos.x - b.min.x, b.max.x - pos.x);
        float dy = Mathf.Min(pos.y - b.min.y, b.max.y - pos.y);

        // The character is near an edge when the closest face on either axis
        // is within edgeLaunchDetectionRadius world units.
        return Mathf.Min(dx, dy) < edgeLaunchDetectionRadius;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Set to false by CharacterSwitchManager when this character is inactive.
    /// Prevents burrowing steering from consuming the other player's move input.
    /// Launch via OnDigPressed still works regardless of this flag.
    /// </summary>
    public bool ReceiveInput { get; set; } = true;

    private Vector2 ReadInput()
    {
        if (!ReceiveInput) return Vector2.zero;

        if (moveAction != null && moveAction.action != null)
            return moveAction.action.ReadValue<Vector2>();

        return new Vector2(movement.facingDirection * 0.5f, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        // Detection sphere for sand entry.
        Gizmos.color = new Color(0.9f, 0.7f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Edge-launch detection zone: draw a shrunk version of the current wall's
        // burrow zone bounds, offset inward by edgeLaunchDetectionRadius, so the
        // region between the two boxes is where shift-to-launch activates.
        if (_currentWall == null) return;

        Bounds b = _currentWall.BurrowZoneBounds;
        if (b.size == Vector3.zero) return;

        bool nearEdge = IsNearSandEdge();
        Gizmos.color = nearEdge
            ? new Color(0.2f, 1f, 0.2f, 0.5f)   // green = launch available
            : new Color(1f, 0.3f, 0.3f, 0.35f);  // red   = mid-wall, ignored

        // Draw the inner threshold boundary (full wall bounds shrunk by detection radius).
        Vector3 innerSize   = b.size - new Vector3(edgeLaunchDetectionRadius * 2f,
                                                   edgeLaunchDetectionRadius * 2f, 0f);
        Vector3 clampedSize = new Vector3(Mathf.Max(0f, innerSize.x),
                                          Mathf.Max(0f, innerSize.y),
                                          b.size.z);
        Gizmos.DrawWireCube(b.center, clampedSize);
    }
}
