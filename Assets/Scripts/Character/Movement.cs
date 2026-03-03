using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Rigidbody-based platformer movement with wall jump, wall slide,
/// coyote time, and jump buffering. Works with CharacterSwitchManager
/// for split-screen input routing.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Movement : MonoBehaviour
{
    [Header("Components")]
    public Animator anim;
    public Transform spriteTransform;
    
    [Header("Horizontal Movement")]
    [Tooltip("Maximum horizontal speed in units/sec.")]
    public float moveSpeed = 9f;

    [Tooltip("Multiplier for horizontal speed while in the air.")]
    public float airSpeedMultiplier = 0.7f;

    public int facingDirection = 1;

    [Header("Jump Physics")]
    [Tooltip("Initial jump velocity applied on the vertical axis.")]
    public float jumpForce = 24.5f;

    [Header("Gravity")]
    [Tooltip("Effective gravity scale while moving upward. Multiplied with Physics.gravity.")]
    public float gravityRise = 4.0f;

    [Tooltip("Effective gravity scale while falling.")]
    public float gravityFall = 7.5f;

    [Tooltip("Maximum downward fall speed (clamped each FixedUpdate).")]
    public float maxFallSpeed = 25f;

    [Header("Coyote Time")]
    [Tooltip("Extra time you can still jump after leaving a ledge.")]
    public float coyoteTime = 0.1f;
    private float coyoteTimer;

    [Header("Jump Buffer")]
    [Tooltip("Time a jump input is remembered before ground/coyote.")]
    public float jumpBufferTime = 0.1f;
    private float jumpBufferTimer;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.12f;
    public LayerMask groundLayer;

    public bool isGrounded { get; private set; }
    public bool IsGrounded => isGrounded;
    private int groundedFrames;

    [Header("Ceiling Check")]
    [Tooltip("Raycast distance upward to detect ceiling collision.")]
    public float ceilingCheckDistance = 0.3f;

    [Header("Wall Check")]
    [Tooltip("Origin point for left wall detection raycast.")]
    public Transform wallCheckLeft;

    [Tooltip("Origin point for right wall detection raycast.")]
    public Transform wallCheckRight;

    [Tooltip("How far to cast for walls.")]
    public float wallCheckDistance = 0.5f;

    [Tooltip("Layer mask for climbable walls.")]
    public LayerMask wallLayer;

    [Header("Wall Slide")]
    [Tooltip("Maximum downward slide speed while on a wall.")]
    public float wallSlideSpeed = 2f;

    [Tooltip("Effective gravity scale while wall-sliding.")]
    public float wallSlideGravity = 1f;

    [Header("Wall Jump")]
    [Tooltip("Enables wall jump and wall slide.")]
    [SerializeField] private bool hasWallJump = true;

    [Tooltip("Velocity applied on wall jump (X = away from wall, Y = up).")]
    public Vector2 wallJumpForce = new Vector2(15f, 20f);

    [Tooltip("Duration after wall jump where horizontal input is suppressed.")]
    public float wallJumpControlTime = 0.2f;

    [Tooltip("Cooldown before the character can grab the same wall again.")]
    public float wallJumpCooldown = 0.3f;

    [Header("Wall Coyote Time")]
    [Tooltip("Grace period to wall jump after leaving a wall.")]
    public float wallCoyoteTime = 0.1f;
    private float wallCoyoteTimer;
    private int lastWallSide;

    private bool isOnWallLeft;
    private bool isOnWallRight;
    public bool isWallSliding { get; private set; }
    private int wallSide;
    private float wallJumpCooldownTimer;
    private float wallJumpControlTimer;

    [Header("Walk Forgiveness")]
    [Tooltip("Keeps walk anim going a bit after releasing input.")]
    public float walkForgivenessDuration = 0.1f;
    private float walkForgivenessTimer = 0f;

    [Tooltip("Grace period after landing before walk can start (prevents interrupting jump spam).")]
    public float landingGraceDuration = 0.15f;
    private float landingGraceTimer = 0f;

    [Header("VFX")]
    [Tooltip("Emit running dust particles.")]
    public ParticleSystem runningDust;

    [Tooltip("How often to spawn running dust.")]
    public float dustInterval = 0.15f;
    private float dustTimer = 0f;
    
    [Tooltip("Particles to emit while walking.")]
    public int walkDustCount = 3;
    
    [Tooltip("Particles to emit on jump start.")]
    public int jumpDustCount = 12;
    
    [Tooltip("Particles to emit on landing.")]
    public int landDustCount = 15;
    
    private bool wasGroundedLastFrame;

    [Header("Abilities")]
    [Tooltip("When false, all jump input is ignored. Disable for characters with a dig ability instead.")]
    [SerializeField] private bool canJump = true;

    [Header("Double Jump")]
    [Tooltip("Enables a second jump while airborne. Independent force from the first jump.")]
    [SerializeField] private bool hasDoubleJump;

    [Tooltip("Vertical velocity applied on the second (air) jump.")]
    [SerializeField] private float doubleJumpForce = 20f;

    private bool _hasDoubleJumped;

    [Header("Dash")]
    [Tooltip("Enables a ground dash and a single air dash (refreshes on landing).")]
    [SerializeField] private bool hasDash;

    [Tooltip("Horizontal speed during the dash impulse.")]
    [SerializeField] private float dashSpeed = 25f;

    [Tooltip("Duration of the dash in seconds.")]
    [SerializeField] private float dashDuration = 0.12f;

    [Tooltip("Minimum time between dashes.")]
    [SerializeField] private float dashCooldown = 0.4f;

    private bool  _isDashing;
    private float _dashTimer;
    private float _dashCooldownTimer;
    private bool  _canAirDash   = true;
    private bool  _dashRequested;

    [Header("Spawn Settings")]
    [Tooltip("Duration to freeze player movement on spawn/respawn.")]
    public float spawnFreezeDuration = 0.5f;
    private bool isSpawnFrozen;
    private float spawnFreezeEndTime;

    /// <summary>
    /// Set to true by DiggingAbility while the character is burrowing.
    /// Causes FixedUpdate to yield full Rigidbody control to the ability.
    /// </summary>
    public bool IsBurrowing { get; set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private Rigidbody _rb;

    public Vector2 moveInput;
    private bool jumpPressed;
    private bool jumpReleased;
    private bool jumpConsumed;

    /// <summary>
    /// Set when a ground (or coyote) jump fires; cleared ONLY on landing.
    /// Unlike <c>jumpConsumed</c> it is never cleared by wall touch, so wall
    /// contact cannot silently re-enable a ground double-jump mid-air.
    /// </summary>
    private bool _hasGroundJumped;

    private bool isJumping;
    private int lastFacingDirection = 1;

    public bool inputEnabled = true;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;

        facingDirection      = 1;
        lastFacingDirection  = 1;
        wasGroundedLastFrame = true;

        FreezeOnSpawn();
    }

    private void FreezeOnSpawn()
    {
        isSpawnFrozen      = true;
        spawnFreezeEndTime = Time.time + spawnFreezeDuration;
        _rb.linearVelocity = Vector3.zero;
        moveInput          = Vector2.zero;
        jumpPressed        = false;
        jumpReleased       = false;
        jumpConsumed       = false;
        _hasGroundJumped   = false;
        jumpBufferTimer    = 0f;
        coyoteTimer        = 0f;
        wasGroundedLastFrame = true;
        _hasDoubleJumped   = false;
        _canAirDash        = true;
        _isDashing         = false;
        _dashRequested     = false;
        _dashCooldownTimer = 0f;
        if (_rb != null) _rb.useGravity = true;
    }

    /// <summary>Resets movement state and re-applies the spawn freeze.</summary>
    public void OnPlayerRespawn()
    {
        wasGroundedLastFrame = true;
        jumpPressed          = false;
        jumpReleased         = false;
        jumpBufferTimer      = 0f;
        moveInput            = Vector2.zero;
        FreezeOnSpawn();
    }

    private void Update()
    {
        if (isSpawnFrozen)
        {
            if (Time.time >= spawnFreezeEndTime)
            {
                isSpawnFrozen = false;
            }
            else
            {
                _rb.linearVelocity = Vector3.zero;
                moveInput          = Vector2.zero;
                return;
            }
        }

        if (IsBurrowing)
        {
            // DiggingAbility owns physics and VFX while burrowing.
            // Clear movement animator states so they don't linger on re-entry.
            if (anim != null)
            {
                anim.SetBool("isWalking",     false);
                anim.SetBool("isIdle",        false);
                anim.SetBool("isJumping",     false);
                anim.SetBool("isFalling",     false);
                anim.SetBool("isGrounded",    false);
                anim.SetBool("isWallSliding", false);
                anim.SetBool("isBurrowing",   true);
            }
            // Timers still run so coyote and buffer are sane when we land after exit.
            UpdateCoyote();
            UpdateJumpBuffer();
            return;
        }

        if (anim != null)
            anim.SetBool("isBurrowing", false);

        HandleAnimations();
        SpawnRunningDust();
        HandleLandingDust();
        DetectDirectionChange();

        UpdateCoyote();
        UpdateJumpBuffer();
        UpdateWallCoyote();
        UpdateWallJumpCooldown();
        UpdateWallJumpControl();
    }

    private void LateUpdate()
    {
        HandleFlip();
    }

    private void FixedUpdate()
    {
        // Yield full Rigidbody control to DiggingAbility while burrowing.
        if (IsBurrowing) return;

        // ── Dash ──────────────────────────────────────────────────────────────
        // Consume any pending dash request (set from the InputSystem callback which
        // runs after FixedUpdate — same deferred-flag pattern as burrow launch).
        if (_dashRequested)
        {
            _dashRequested = false;
            BeginDash();
        }

        if (_dashCooldownTimer > 0f)
            _dashCooldownTimer -= Time.fixedDeltaTime;

        if (_isDashing)
        {
            _dashTimer -= Time.fixedDeltaTime;

            // Pin horizontal velocity to dashSpeed — prevents drag or other forces
            // from bleeding off the impulse during the short dash window.
            _rb.linearVelocity = new Vector3(facingDirection * dashSpeed, 0f, 0f);

            if (_dashTimer <= 0f)
            {
                _isDashing     = false;
                _rb.useGravity = true;
            }

            // Skip all normal movement while dashing — gravity is off, steering
            // is suppressed, and jump cannot fire mid-dash.
            return;
        }

        // ── Normal movement ───────────────────────────────────────────────────
        CheckGrounded();
        CheckWalls();
        CheckCeilingBump();
        HandleWallSlide();
        ApplyGravity();
        HandleHorizontalMovement();
        HandleJump();
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    // Re-used buffer for OverlapSphere ground check — avoids per-frame allocation.
    private static readonly Collider[] _groundOverlapBuffer = new Collider[8];

    private void CheckGrounded()
    {
        // OverlapSphereNonAlloc instead of CheckSphere so we can filter out the
        // character's own CapsuleCollider. When groundLayer is set to "Everything"
        // (the common Inspector mistake) CheckSphere returns true for the character
        // itself, producing false landing events near the jump apex and enabling
        // infinite jumping. Filtering by IsChildOf(transform) is self-layer-agnostic.
        int hitCount = Physics.OverlapSphereNonAlloc(
            groundCheck.position, groundCheckRadius, _groundOverlapBuffer, groundLayer);

        bool rawGrounded = false;
        for (int i = 0; i < hitCount; i++)
        {
            if (!_groundOverlapBuffer[i].transform.IsChildOf(transform))
            {
                rawGrounded = true;
                break;
            }
        }

        bool wasGrounded = isGrounded;
        isGrounded = rawGrounded && _rb.linearVelocity.y <= 0.1f;

        if (isGrounded && !wasGrounded)
        {
            jumpConsumed      = false;
            _hasGroundJumped  = false;
            isJumping         = false;
            landingGraceTimer = landingGraceDuration;
            _hasDoubleJumped  = false;   // double jump refreshes on land
            _canAirDash       = true;    // air dash refreshes on land
        }
    }

    private void CheckWalls()
    {
        if (wallCheckLeft == null || wallCheckRight == null) return;
        isOnWallLeft  = Physics.Raycast(wallCheckLeft.position,  Vector3.left,  wallCheckDistance, wallLayer);
        isOnWallRight = Physics.Raycast(wallCheckRight.position, Vector3.right, wallCheckDistance, wallLayer);
    }

    private void CheckCeilingBump()
    {
        if (_rb.linearVelocity.y > 0f
            && Physics.Raycast(transform.position, Vector3.up, ceilingCheckDistance, groundLayer | wallLayer))
        {
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, 0f);
        }
    }

    private void HandleWallSlide()
    {
        bool canWallSlide = hasWallJump
                         && !isGrounded
                         && _rb.linearVelocity.y < 0f
                         && wallJumpCooldownTimer <= 0f;

        if (canWallSlide && (isOnWallLeft || isOnWallRight))
        {
            bool wasWallSliding = isWallSliding;
            isWallSliding = true;
            wallSide      = isOnWallLeft ? -1 : 1;

            // Always clear jumpConsumed on first wall touch so that a ground jump
            // followed by a wall slide can still produce a wall jump.
            // Double ground-jumping is prevented by _hasGroundJumped in canGroundJump,
            // not by jumpConsumed, so this is safe.
            if (!wasWallSliding)
                jumpConsumed = false;

            if (_rb.linearVelocity.y < -wallSlideSpeed)
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -wallSlideSpeed, 0f);
        }
        else
        {
            if (isWallSliding)
            {
                wallCoyoteTimer = wallCoyoteTime;
                lastWallSide    = wallSide;
            }
            isWallSliding = false;
        }
    }

    private void ApplyGravity()
    {
        if (isWallSliding)
        {
            // Add upward force to counteract the difference vs. normal gravity.
            float extraG = (wallSlideGravity - 1f) * Physics.gravity.y;
            _rb.AddForce(new Vector3(0f, extraG, 0f), ForceMode.Acceleration);

            if (_rb.linearVelocity.y < -wallSlideSpeed)
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -wallSlideSpeed, 0f);
            return;
        }

        // Unity applies gravity × 1 automatically via rb.useGravity.
        // We add extra acceleration to simulate the higher rise/fall scales.
        float gravityScale = _rb.linearVelocity.y > 0f ? gravityRise : gravityFall;
        float extra        = (gravityScale - 1f) * Physics.gravity.y;
        _rb.AddForce(new Vector3(0f, extra, 0f), ForceMode.Acceleration);

        if (_rb.linearVelocity.y < -maxFallSpeed)
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -maxFallSpeed, 0f);
    }

    private void HandleHorizontalMovement()
    {
        float targetX;

        if (wallJumpControlTimer > 0f)
        {
            targetX = _rb.linearVelocity.x;
        }
        else
        {
            float speedMultiplier = isGrounded ? 1f : airSpeedMultiplier;
            float horizontalInput = moveInput.x;

            if (Mathf.Abs(horizontalInput) > 0.1f)
                horizontalInput = Mathf.Sign(horizontalInput);

            targetX = horizontalInput * moveSpeed * speedMultiplier;
        }

        _rb.linearVelocity = new Vector3(targetX, _rb.linearVelocity.y, 0f);
    }

    /// <summary>
    /// Prevents HandleHorizontalMovement from overriding the Rigidbody's horizontal
    /// velocity for the given duration. Call this from DiggingAbility immediately after
    /// applying the burrow-launch impulse so Movement cannot zero it out in the same
    /// or following FixedUpdate step.
    /// </summary>
    public void SetLaunchImpulse(float duration)
    {
        wallJumpControlTimer = Mathf.Max(wallJumpControlTimer, duration);
    }

    private void HandleJump()
    {
        if (!canJump) return;

        bool canCoyote       = !isGrounded && coyoteTimer > 0f;
        bool canWallCoyote   = !isWallSliding && wallCoyoteTimer > 0f;
        bool canBufferedJump = jumpBufferTimer > 0f;
        bool canGroundJump   = (isGrounded || canCoyote) && !_hasGroundJumped;
        bool canWallJump     = hasWallJump && (isWallSliding || canWallCoyote) && !jumpConsumed;
        bool canDoubleJump   = hasDoubleJump && !isGrounded && !_hasDoubleJumped && !isWallSliding;

        if (canBufferedJump)
        {
            if      (canWallJump)   PerformWallJump();
            else if (canGroundJump) PerformGroundJump();
            else if (canDoubleJump) PerformDoubleJump();
        }

        // Variable-height jump: cut upward velocity on early release.
        if (isJumping && jumpReleased && _rb.linearVelocity.y > 0f)
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, _rb.linearVelocity.y * 0.5f, 0f);

        if (_rb.linearVelocity.y < -2f)
            isJumping = false;
    }

    private void PerformGroundJump()
    {
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, jumpForce, 0f);
        jumpConsumed     = true;
        _hasGroundJumped = true;   // blocks coyote refill and wall-slide jumpConsumed clear
        jumpBufferTimer  = 0f;
        coyoteTimer      = 0f;
        isJumping        = true;
        jumpReleased     = false;

        if (runningDust != null)
            runningDust.Emit(jumpDustCount);
    }

    private void PerformWallJump()
    {
        int jumpDirection = isWallSliding ? -wallSide : -lastWallSide;

        _rb.linearVelocity    = new Vector3(wallJumpForce.x * jumpDirection, wallJumpForce.y, 0f);
        jumpConsumed          = true;
        // A wall jump consumes any remaining coyote time so it cannot be
        // immediately followed by a free "coyote" ground jump mid-air.
        // _hasGroundJumped is also set so the coyote guard in canGroundJump
        // blocks a second jump even if coyoteTimer somehow stays positive.
        coyoteTimer           = 0f;
        _hasGroundJumped      = true;
        jumpBufferTimer       = 0f;
        wallCoyoteTimer       = 0f;
        isJumping             = true;
        jumpReleased          = false;
        isWallSliding         = false;
        wallJumpControlTimer  = wallJumpControlTime;
        wallJumpCooldownTimer = wallJumpCooldown;
        facingDirection       = jumpDirection;

        if (anim != null)
        {
            anim.SetBool("isWallJumping", true);
            Invoke(nameof(ClearWallJumping), 0.15f);
        }
    }

    private void ClearWallJumping()
    {
        if (anim != null)
            anim.SetBool("isWallJumping", false);
    }

    private void PerformDoubleJump()
    {
        // Snap Y velocity to doubleJumpForce so the second jump always feels
        // consistent regardless of whether the character was rising or falling.
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, doubleJumpForce, 0f);
        _hasDoubleJumped   = true;
        jumpBufferTimer    = 0f;
        isJumping          = true;
        jumpReleased       = false;

        if (runningDust != null)
            runningDust.Emit(jumpDustCount);
    }

    /// <summary>
    /// Initiates a dash in the current facing direction. Called from FixedUpdate
    /// when _dashRequested is set, so it always runs before physics simulation.
    /// </summary>
    private void BeginDash()
    {
        _isDashing         = true;
        _dashTimer         = dashDuration;
        _dashCooldownTimer = dashCooldown;
        _rb.useGravity     = false;
        _rb.linearVelocity = new Vector3(facingDirection * dashSpeed, 0f, 0f);

        // Consume the air dash — can only dash once before landing.
        if (!isGrounded)
            _canAirDash = false;
    }

    /// <summary>
    /// Called by CharacterSwitchManager when the dash input fires for this character.
    /// Stores a deferred flag so FixedUpdate processes it before physics runs.
    /// </summary>
    public void OnDashPressed()
    {
        if (!hasDash)                    return;
        if (isSpawnFrozen)               return;
        if (IsBurrowing)                 return;
        if (_isDashing)                  return;
        if (_dashCooldownTimer > 0f)     return;   // still on cooldown

        // Air dash is limited to one use per airtime — ground dashes are unlimited.
        if (!isGrounded && !_canAirDash) return;

        _dashRequested = true;
    }

    // ── Timers ────────────────────────────────────────────────────────────────

    private void UpdateCoyote()
    {
        if (isGrounded)
        {
            // Refill coyote timer only when truly on the ground and not in a
            // ground jump (after a jump, isGrounded becomes false on the same
            // FixedUpdate, but UpdateCoyote runs in Update — gate on the flag).
            if (!_hasGroundJumped)
                coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    private void UpdateWallCoyote()
    {
        if (wallCoyoteTimer > 0f) wallCoyoteTimer -= Time.deltaTime;
    }

    private void UpdateJumpBuffer()
    {
        if (jumpPressed)
        {
            jumpBufferTimer = jumpBufferTime;
            jumpPressed     = false;
        }
        else
        {
            jumpBufferTimer -= Time.deltaTime;
        }
    }

    private void UpdateWallJumpCooldown()
    {
        if (wallJumpCooldownTimer > 0f) wallJumpCooldownTimer -= Time.deltaTime;
    }

    private void UpdateWallJumpControl()
    {
        if (wallJumpControlTimer > 0f) wallJumpControlTimer -= Time.deltaTime;
    }

    // ── Animations & VFX ─────────────────────────────────────────────────────

    private void HandleAnimations()
    {
        if (anim == null) return;

        bool hasInput = Mathf.Abs(moveInput.x) > 0.1f;
        walkForgivenessTimer = hasInput
                             ? walkForgivenessDuration
                             : walkForgivenessTimer - Time.deltaTime;

        landingGraceTimer -= Time.deltaTime;

        bool walking    = walkForgivenessTimer > 0f && isGrounded && !isJumping && landingGraceTimer <= 0f;
        bool shouldFall = _rb.linearVelocity.y < -0.5f && !isGrounded && !isJumping && !isWallSliding;
        bool shouldJump = _rb.linearVelocity.y >  0.5f && !isGrounded && !isWallSliding;

        if (isGrounded) shouldFall = false;

        anim.SetBool("isWalking",    walking);
        anim.SetBool("isIdle",       !walking && isGrounded && !isJumping);
        anim.SetBool("isJumping",    (isJumping || shouldJump) && !isWallSliding);
        anim.SetBool("isFalling",    shouldFall);
        anim.SetBool("isGrounded",   isGrounded);
        anim.SetBool("isWallSliding",isWallSliding);
        anim.SetFloat("xVelocity",   Mathf.Abs(_rb.linearVelocity.x));
        anim.SetFloat("yVelocity",   _rb.linearVelocity.y);
    }

    private void DetectDirectionChange()
    {
        if (facingDirection == lastFacingDirection) return;

        if (isGrounded && Mathf.Abs(moveInput.x) > 0.1f)
        {
            if (anim        != null) anim.SetTrigger("directionChanged");
            if (runningDust != null) runningDust.Emit(6);
        }

        lastFacingDirection = facingDirection;
    }

    private void SpawnRunningDust()
    {
        if (runningDust == null) return;

        bool isMoving = isGrounded && Mathf.Abs(moveInput.x) > 0.1f;
        if (isMoving)
        {
            dustTimer -= Time.deltaTime;
            if (dustTimer <= 0f)
            {
                runningDust.Emit(walkDustCount);
                dustTimer = dustInterval;
            }
        }
        else
        {
            dustTimer = 0f;
        }
    }

    private void HandleLandingDust()
    {
        if (runningDust == null) return;

        if (isGrounded && !wasGroundedLastFrame && !isSpawnFrozen)
        {
            runningDust.Emit(landDustCount);
            if (anim != null) anim.SetTrigger("Land");
        }

        wasGroundedLastFrame = isGrounded;
    }

    private void HandleFlip()
    {
        if (isWallSliding)
        {
            facingDirection = -wallSide;
        }
        else if (wallJumpControlTimer <= 0f)
        {
            if      (moveInput.x >  0.1f) facingDirection =  1;
            else if (moveInput.x < -0.1f) facingDirection = -1;
        }

        if (spriteTransform != null)
        {
            Vector3 scale = spriteTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * facingDirection;
            spriteTransform.localScale = scale;
        }
        else
        {
            transform.localScale = new Vector3(facingDirection, 1f, 1f);
        }
    }

    // ── Input: Unity Input System callbacks ───────────────────────────────────

    /// <summary>Receives move input via Unity Input System action callback.</summary>
    public void Move(InputAction.CallbackContext ctx)
    {
        if (!inputEnabled) return;
        moveInput = ctx.ReadValue<Vector2>();
    }

    /// <summary>Receives jump input via Unity Input System action callback.</summary>
    public void JumpAction(InputAction.CallbackContext ctx)
    {
        if (!inputEnabled) return;
        if (ctx.performed) jumpPressed  = true;
        if (ctx.canceled)  jumpReleased = true;
    }

    // ── Input: CharacterSwitchManager interface ───────────────────────────────

    /// <summary>Sets the horizontal movement axis directly.</summary>
    public void SetMoveInput(float horizontal)
    {
        if (!inputEnabled) return;
        moveInput.x = horizontal;
    }

    /// <summary>Signals that the jump button was pressed.</summary>
    public void OnJumpPressed()
    {
        if (!inputEnabled) return;
        jumpPressed = true;
    }

    /// <summary>Signals that the jump button was released.</summary>
    public void OnJumpReleased()
    {
        if (!inputEnabled) return;
        jumpReleased = true;
    }

    /// <summary>Clears all movement and jump input (called when this character becomes inactive).</summary>
    public void ClearInput()
    {
        moveInput       = Vector2.zero;
        jumpPressed     = false;
        jumpReleased    = false;
        jumpBufferTimer = 0f;
    }

    /// <summary>Sets the facing direction and immediately updates the sprite transform.</summary>
    public void SetFacingDirection(int direction)
    {
        facingDirection     = direction;
        lastFacingDirection = direction;

        if (spriteTransform != null)
        {
            Vector3 scale = spriteTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * facingDirection;
            spriteTransform.localScale = scale;
        }
        else
        {
            transform.localScale = new Vector3(facingDirection, 1f, 1f);
        }
    }

    // ── Debug ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (groundCheck != null)
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * ceilingCheckDistance);

        Gizmos.color = Color.green;
        if (wallCheckLeft  != null)
            Gizmos.DrawLine(wallCheckLeft.position,  wallCheckLeft.position  + Vector3.left  * wallCheckDistance);
        if (wallCheckRight != null)
            Gizmos.DrawLine(wallCheckRight.position, wallCheckRight.position + Vector3.right * wallCheckDistance);
    }
}
