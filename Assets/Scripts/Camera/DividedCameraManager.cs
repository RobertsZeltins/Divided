using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Split-screen camera manager built on distance hysteresis.
///
/// Together — both cameras share one X, following the active character
///            with look-ahead. Splits when distX crosses splitDist.
///
/// Split    — active camera follows its character normally.
///            Inactive camera has three phases:
///
///            1. Initial pan  — 0.3 s smooth reframe from the shared
///                              position to _inactiveFixedX when the split
///                              first fires (hides the small look-ahead snap).
///            2. Frozen       — held at _inactiveFixedX while characters
///                              are far apart.
///            3. Pre-merge    — when characters are within BlendZone the
///                              inactive camera gradually eases toward the
///                              active camera so the merge is visually seamless.
///
/// Merge    — fires when distX drops below mergeDistance AND the inactive
///            camera has converged within 0.15 world units of the active camera.
///            Force-merge fallback triggers at mergeDistance × 0.1 for fast
///            approaches. The hysteresis gap (splitDist - mergeDistance ≈ 5+
///            units) makes re-split oscillation geometrically impossible.
/// </summary>
public class DividedCameraManager : MonoBehaviour
{
    private const float MaxLookAheadUnits       = 3f;

    // Minimum Rigidbody speed before a facing direction change is accepted.
    // 2.0 prevents physics micro-velocities when standing still from flipping
    // the look-ahead direction and causing camera micro-jitter.
    private const float FacingVelocityThreshold = 2.0f;

    // One-shot pan duration when the inactive cam first enters frozen position.
    // Serialized so it can be tuned from the Inspector.
    [SerializeField] private float inactivePanDuration = 1.0f;

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Characters")]
    [SerializeField] private Transform topCharacter;
    [SerializeField] private Transform bottomCharacter;

    [Header("Cameras")]
    [SerializeField] private Camera topCamera;
    [SerializeField] private Camera bottomCamera;

    [Header("Cinemachine")]
    [Tooltip("CinemachineCamera (virtual camera) that drives TopCamera via its CinemachineBrain. " +
             "DividedCameraManager positions this transform each frame; the Brain copies it to the physical camera.")]
    [SerializeField] private CinemachineCamera topVirtualCamera;

    [Tooltip("CinemachineCamera (virtual camera) that drives BottomCamera via its CinemachineBrain.")]
    [SerializeField] private CinemachineCamera bottomVirtualCamera;

    [Header("Input")]
    [SerializeField] private CharacterSwitchManager switchManager;

    [Header("Effects")]
    [SerializeField] private SplitScreenEffect splitScreenEffect;

    [Header("Merge")]
    [Tooltip("Character X-distance at which cameras merge back together. " +
             "Keep well below splitDist to prevent re-split oscillation.")]
    [SerializeField] private float mergeDistance = 1.5f;

    [Tooltip("Distance over which active-camera look-ahead fades to zero " +
             "as characters approach.")]
    [SerializeField] private float mergeThreshold = 8f;

    [Header("Split")]
    [Tooltip("Viewport fraction from either edge that triggers the split. " +
             "0.15 = split when the inactive character is within 15 % of the edge.")]
    [SerializeField] private float splitEdgeFraction = 0.15f;

    [Tooltip("Characters must stay beyond the split threshold for this many seconds " +
             "before the split actually fires. Prevents accidental splits on brief " +
             "separations and gives a small visual lead-up to the split.")]
    [SerializeField] private float splitConfirmTime = 0.3f;

    [Tooltip("How long the divider takes to fade out when characters reunite. " +
             "The divider stays fully visible during split and only starts fading " +
             "once the cameras have converged and the state switches to Together.")]
    [SerializeField] private float mergeFadeDuration = 0.65f;

    [Header("Follow")]
    [SerializeField] private float followSmoothTime = 0.15f;

    [Tooltip("Smooth time used immediately after a character switch, " +
             "so the camera eases to the newly active character rather than snapping.")]
    [SerializeField] private float switchSmoothTime = 0.6f;

    [Header("Look-Ahead")]
    [SerializeField] private float lookAheadDistance   = 1.5f;
    [SerializeField] private float lookAheadSmoothTime = 0.35f;

    [Header("Camera Zoom — Together Mode")]
    [Tooltip("Minimum Z pull-back fraction applied even when characters are side-by-side. " +
             "0 = scene default distance. 0.2 = always 20% further back than the initial position.")]
    [SerializeField] private float baseZoom = 0.3f;

    [Tooltip("Additional Z pull-back fraction added at maximum horizontal character separation. " +
             "Scales linearly from 0 at zero separation to this value at the split threshold. " +
             "Increase this to zoom out more as characters move apart horizontally.")]
    [SerializeField] private float additionalZoom = 2.0f;

    [Tooltip("How strongly the vertical span between both characters drives extra zoom. " +
             "Formula: vZoom = verticalSpan * multiplier / cameraDistance. " +
             "0 = no vertical zoom. 1 = 1 unit of span → 1 unit of Z pull-back. " +
             "Start around 1–2 and tune from there.")]
    [SerializeField] private float verticalZoomMultiplier = 1.5f;

    [Tooltip("Smooth time for Together-mode zoom in and out transitions.")]
    [SerializeField] private float zoomSmoothTime = 0.9f;

    [Tooltip("Fraction of the character's vertical displacement the camera follows on Y. " +
             "The Z pull-back is derived automatically to keep the dividing line fixed. " +
             "1.0 = camera centre tracks character exactly.")]
    [SerializeField] private float splitVerticalFollowFraction = 0.85f;

    [Tooltip("Maximum world-unit Z pull-back per split camera. " +
             "Must be large relative to the camera-to-scene distance. " +
             "At camera distance 35 a value of 15 leaves almost no vertical headroom; " +
             "set to 60–100 to match the visible zoom of Together mode.")]
    [SerializeField] private float maxSplitZPullback = 100f;

    [Tooltip("Smooth time for the per-camera Z pull-back / Y follow in split mode.")]
    [SerializeField] private float splitZoomSmoothTime = 0.8f;

    [Tooltip("Minimum Z pull-back fraction applied to each split camera independent of " +
             "character vertical position. Increase this to zoom out more when split. " +
             "0 = no persistent base zoom-out, 0.5 = cameras always pull back 50% extra.")]
    [SerializeField] private float splitBaseZoom = 0.6f;

    // ── Private state ─────────────────────────────────────────────────────────

    private Rigidbody _topRb;
    private Rigidbody _botRb;

    private float _topFacingDir = 1f;
    private float _botFacingDir = 1f;

    // World-space viewport half-width and derived split threshold, both computed
    // once in Start() from the scene camera geometry.
    private float _viewportHalfWidth;
    private float _splitDist;

    private enum CamState { Together, Split }
    private CamState _state = CamState.Together;

    // Frozen X for the inactive camera.
    // Formula: inactiveChar.x + Sign(activeChar.x - inactiveChar.x) * splitDist
    // Always offsets toward the active character so the inactive character sits
    // at splitEdgeFraction of the frozen viewport regardless of which side
    // the active character is on.
    private float _inactiveFixedX;

    // One-shot pan animation played when _inactiveFixedX is first set.
    private float _inactiveStartX;
    private float _inactivePanTimer;

    // Inactive camera smooth time scales with character separation using a
    // quadratic curve. InactiveCamSmoothTimeFar applies when characters are apart;
    // InactiveCamSmoothTimeNear applies near merge AND when approach speed is high.
    // Both values are deliberately moderate — the camera should visibly SLIDE to its
    // convergence target, not snap. Snapping was the root cause of the "insanely fast"
    // merge feel at speed.
    private const float InactiveCamSmoothTimeFar  = 0.4f;
    private const float InactiveCamSmoothTimeNear = 0.25f;

    // At this horizontal approach speed (m/s) the inactive camera smooth time
    // blends fully to InactiveCamSmoothTimeNear.
    private const float InactiveApproachSpeedCap = 18f;

    // Frame-to-frame distX delta gives the true approach rate regardless of
    // whether it comes from horizontal velocity, diagonal movement, etc.
    private float _prevDistX;
    private float _smoothedApproachRate;

    // How long characters have continuously exceeded the split threshold.
    // Split fires only when this reaches splitConfirmTime.
    private float _splitHoldTimer;

    // How long characters have continuously been within mergeDistance.
    private const float MergeConfirmTime = 0.08f;
    private float _mergeHoldTimer;

    // After a split or merge fires, the reverse transition is blocked for this
    // duration. Prevents the state from thrashing when distX hovers on the boundary.
    [Tooltip("Seconds to lock out the reverse transition after any split or merge fires. " +
             "Prevents the divider from flashing when characters hover on the boundary.")]
    [SerializeField] private float stateChangeCooldown = 0.8f;
    private float _stateChangeCooldown;

    // After MergeConfirmTime the inactive camera must be within this world-unit gap
    // of the active camera before the merge fires. With InactiveCamSmoothTimeNear = 0.25f
    // the camera slides visibly into position — by the time camGap is this small the
    // two panels look identical and the state switch is completely imperceptible.
    private const float MergeCameraAlignThreshold = 0.4f;

    private float _topVelX;
    private float _botVelX;
    private float _topLookAheadX;
    private float _topLookAheadVel;
    private float _botLookAheadX;
    private float _botLookAheadVel;

    // Seconds since the last character switch. Used to blend from switchSmoothTime
    // back to followSmoothTime so the reframe after a switch eases in smoothly.
    private const float SwitchSmoothFade = 1.0f;
    private float _switchTimer = float.MaxValue;

    // Asymmetric projection zoom (Together mode only) — camera positions never change.
    // In Split mode the frustum is reset; cameras move on Y and Z instead.
    private float _currentZoom;   // smoothed zoom factor (0 = default view)
    private float _zoomVel;

    // Scene-baseline positions of each camera, captured once in Start().
    // In Split mode cameras deviate from these; on merge they snap back.
    private float _topOriginalY;
    private float _botOriginalY;
    private float _topOriginalZ;
    private float _botOriginalZ;

    // Sign of the Z axis in the camera's pull-back direction.
    // -1 for cameras looking in +Z (typical: camera behind scene at negative Z).
    private float _camPullbackZ;

    // Baseline Y of each character at scene start — used to compute vertical
    // displacement that drives per-camera Z pull-back in split mode.
    private float _topCharBaseY;
    private float _botCharBaseY;

    // Per-camera half-tangents derived from ViewportToWorldPoint at Start — match
    // the camera's actual projection exactly, used only for pull-back target sizing.
    private float _topHalfTan;
    private float _botHalfTan;

    // Original perpendicular distance from each camera to the scene plane.
    private float _topOriginalDist;
    private float _botOriginalDist;

    // Scene-plane Z for each camera (characters' Z at scene start).
    private float _topSceneZ;
    private float _botSceneZ;

    // World Y of each camera's inner edge, MEASURED via ViewportToWorldPoint at
    // Start — reflects the camera's true projection, not a formula assumption.
    //   Top camera    : viewport (0.5, 0) = bottom of view = dividing line
    //   Bottom camera : viewport (0.5, 1) = top    of view = dividing line
    private float _topDivLineY;
    private float _botDivLineY;

    // Per-camera smoothed Z pull-back amounts (world units) for split mode.
    private float _topSplitZoom;
    private float _botSplitZoom;
    private float _topSplitZoomVel;
    private float _botSplitZoomVel;
    // Zoom (pull-back units) locked at the moment each character became inactive.
    // The inactive camera holds this value; it only changes while that character is active.
    private float _topLockedSplitZoom;
    private float _botLockedSplitZoom;

    [Tooltip("How quickly (units/second) the locked zoom can decrease while actively " +
             "controlling that character. Keeps the camera from snapping in on switch. " +
             "Higher = zooms back in faster when moving around. 0 = never zooms back in.")]
    [SerializeField] private float splitZoomLockDecayRate = 8f;

    // Residual per-camera Z pull-back applied immediately after a merge so each
    // camera continues from its exact split position without snapping to the
    // shared average. Decays to zero using the same splitZoomSmoothTime.
    private float _postMergeTopPullback;
    private float _postMergeBotPullback;
    private float _postMergeTopVel;
    private float _postMergeBotVel;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _topRb = topCharacter    != null ? topCharacter.GetComponent<Rigidbody>()    : null;
        _botRb = bottomCharacter != null ? bottomCharacter.GetComponent<Rigidbody>() : null;

        // Align virtual cameras to the designer-set physical camera positions at startup.
        // CinemachineBrain (LateUpdate, order 0) will own the physical transforms from
        // the first frame onward; DividedCameraManager (LateUpdate, order -100) always
        // runs first so the Brain reads fully-resolved positions every frame.
        topVirtualCamera.transform.SetPositionAndRotation(
            topCamera.transform.position, topCamera.transform.rotation);
        bottomVirtualCamera.transform.SetPositionAndRotation(
            bottomCamera.transform.position, bottomCamera.transform.rotation);

        // Viewport half-width in world space — cameras only move on X so this
        // is constant for the lifetime of the scene.
        if (topCamera.orthographic)
        {
            _viewportHalfWidth = topCamera.orthographicSize * topCamera.aspect;
        }
        else
        {
            float zDist = Mathf.Abs(topCamera.transform.position.z - topCharacter.position.z);
            _viewportHalfWidth = Mathf.Tan(topCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                               * zDist * topCamera.aspect;
        }

        _splitDist = (1f - 2f * splitEdgeFraction) * _viewportHalfWidth;

        // tan(fov/2) — used only to build per-camera halfTan below; not stored globally.
        float sceneTan = topCamera.orthographic
                       ? 0f
                       : Mathf.Tan(topCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

        // Pull-back direction: opposite to the camera's forward vector along Z.
        _camPullbackZ = -Mathf.Sign(topCamera.transform.forward.z);

        // Capture scene-baseline positions. Cameras return to these on merge.
        _topOriginalY = topCamera.transform.position.y;
        _botOriginalY = bottomCamera.transform.position.y;
        _topOriginalZ = topCamera.transform.position.z;
        _botOriginalZ = bottomCamera.transform.position.z;

        // Capture each character's baseline Y.
        _topCharBaseY = topCharacter.position.y;
        _botCharBaseY = bottomCharacter.position.y;

        // Scene-plane Z (character positions assumed to be on the scene plane).
        _topSceneZ = topCharacter.position.z;
        _botSceneZ = bottomCharacter.position.z;

        // Original camera-to-scene distances.
        _topOriginalDist = Mathf.Abs(_topOriginalZ - _topSceneZ);
        _botOriginalDist = Mathf.Abs(_botOriginalZ - _botSceneZ);

        // Measure each camera's inner edge world Y using ViewportToWorldPoint.
        // This is the ground-truth value — it uses Unity's actual projection
        // pipeline so it is correct regardless of URP specifics, aspect ratio
        // changes, or any FoV rounding in the Inspector.
        //   Top camera    : viewport (0.5, 0) = bottom of view = dividing line
        //   Bottom camera : viewport (0.5, 1) = top    of view = dividing line
        _topDivLineY = topCamera.ViewportToWorldPoint(
                           new Vector3(0.5f, 0f, _topOriginalDist)).y;
        _botDivLineY = bottomCamera.ViewportToWorldPoint(
                           new Vector3(0.5f, 1f, _botOriginalDist)).y;

        // Derive per-camera halfTan from ViewportToWorldPoint measurements so the
        // pull-back target computation is consistent with the actual projection.
        //   top: divLine = camY − halfTan × dist  →  halfTan = (camY − divLine) / dist
        //   bot: divLine = camY + halfTan × dist  →  halfTan = (divLine − camY) / dist
        _topHalfTan = (_topOriginalY - _topDivLineY) / Mathf.Max(_topOriginalDist, 0.001f);
        _botHalfTan = (_botDivLineY - _botOriginalY) / Mathf.Max(_botOriginalDist, 0.001f);
    }

    private void OnEnable()
    {
        if (switchManager != null)
            switchManager.OnCharacterSwitched += HandleCharacterSwitched;
    }

    private void OnDisable()
    {
        if (switchManager != null)
            switchManager.OnCharacterSwitched -= HandleCharacterSwitched;
    }

    private void LateUpdate()
    {
        if (topCharacter == null || bottomCharacter == null) return;

        float topVelX = _topRb != null ? _topRb.linearVelocity.x : 0f;
        float botVelX = _botRb != null ? _botRb.linearVelocity.x : 0f;

        if (Mathf.Abs(topVelX) > FacingVelocityThreshold) _topFacingDir = Mathf.Sign(topVelX);
        if (Mathf.Abs(botVelX) > FacingVelocityThreshold) _botFacingDir = Mathf.Sign(botVelX);

        float distX       = Mathf.Abs(topCharacter.position.x - bottomCharacter.position.x);
        bool  topIsActive = switchManager != null
                         && switchManager.ActiveCharacter != null
                         && switchManager.ActiveCharacter.transform == topCharacter;

        // Total vertical world-space gap between the two characters.
        // Used by ApplyZoom to drive zoom-out when characters are far apart vertically —
        // accounts for both characters, not just the currently active one.
        float verticalSpan = Mathf.Abs(topCharacter.position.y - bottomCharacter.position.y);

        _switchTimer        += Time.deltaTime;
        _stateChangeCooldown = Mathf.Max(0f, _stateChangeCooldown - Time.deltaTime);

        ApplyZoom(distX, verticalSpan);

        switch (_state)
        {
            case CamState.Together: DriveTogetherCameras(topIsActive, distX); break;
            case CamState.Split:    DriveSplitCameras(topIsActive,    distX); break;
        }

        // CinemachineBrain.UpdateMethod is set to LateUpdate and DividedCameraManager
        // is configured to run before CinemachineBrain via Script Execution Order.
        // The Brain automatically picks up the virtual camera positions at the end
        // of the same LateUpdate pass — no ManualUpdate() call needed.
    }

    // ── Together ──────────────────────────────────────────────────────────────

    private void DriveTogetherCameras(bool topIsActive, float distX)
    {
        float activeX      = topIsActive ? topCharacter.position.x : bottomCharacter.position.x;
        float activeFacing = topIsActive ? _topFacingDir : _botFacingDir;

        float lookTarget = Mathf.Clamp(activeFacing * lookAheadDistance, -MaxLookAheadUnits, MaxLookAheadUnits);
        _topLookAheadX   = Mathf.SmoothDamp(_topLookAheadX, lookTarget, ref _topLookAheadVel, lookAheadSmoothTime);
        _botLookAheadX   = _topLookAheadX;

        float activeSmoothTime = Mathf.Lerp(switchSmoothTime, followSmoothTime,
                                            Mathf.Clamp01(_switchTimer / SwitchSmoothFade));

        // Always start SmoothDamp from the ACTIVE virtual camera's current X so Together
        // mode has the correct reference regardless of which camera is top vs bottom.
        float activeCamCurrentX = topIsActive
            ? topVirtualCamera.transform.position.x
            : bottomVirtualCamera.transform.position.x;
        float sharedX = Mathf.SmoothDamp(activeCamCurrentX,
                                         activeX + _topLookAheadX,
                                         ref _topVelX, activeSmoothTime);
        _botVelX = _topVelX;

        // Apply Together-mode Z pull-back + Y correction using the same
        // ViewportToWorldPoint approach as split mode.  Both modes now use
        // an identical camera representation so split/merge transitions are
        // position-continuous with zero snap or frustum switching.
        //
        // Post-merge residuals (_postMergeTopPullback / _postMergeBotPullback)
        // carry each camera's per-camera Z surplus from the split state and
        // decay to zero here, preventing the snap that would otherwise occur
        // when the two cameras had different zoom levels (e.g. large vertical
        // separation) and are averaged into a single _currentZoom at merge.
        _postMergeTopPullback = Mathf.SmoothDamp(
            _postMergeTopPullback, 0f, ref _postMergeTopVel, splitZoomSmoothTime);
        _postMergeBotPullback = Mathf.SmoothDamp(
            _postMergeBotPullback, 0f, ref _postMergeBotVel, splitZoomSmoothTime);

        float topPull    = _currentZoom * _topOriginalDist + _postMergeTopPullback;
        float botPull    = _currentZoom * _botOriginalDist + _postMergeBotPullback;
        float topNewZ    = _topOriginalZ + _camPullbackZ * topPull;
        float botNewZ    = _botOriginalZ + _camPullbackZ * botPull;
        float topNewDist = Mathf.Abs(topNewZ - _topSceneZ);
        float botNewDist = Mathf.Abs(botNewZ - _botSceneZ);

        // Post-merge blend: the previously-inactive camera eases toward sharedX
        // over MergeBlendDuration so the panel transition is a smooth pan, not a snap.
        // The active camera always uses sharedX directly — no change to its tracking.
        topVirtualCamera.transform.position    = new Vector3(sharedX, _topOriginalY, topNewZ);
        bottomVirtualCamera.transform.position = new Vector3(sharedX, _botOriginalY, botNewZ);

        Vector3 topInner = VirtualViewportToWorldPoint(topCamera, topVirtualCamera.transform, new Vector3(0.5f, 0f, topNewDist));
        Vector3 botInner = VirtualViewportToWorldPoint(bottomCamera, bottomVirtualCamera.transform, new Vector3(0.5f, 1f, botNewDist));

        topVirtualCamera.transform.position    = new Vector3(sharedX, _topOriginalY + (_topDivLineY - topInner.y), topNewZ);
        bottomVirtualCamera.transform.position = new Vector3(sharedX, _botOriginalY + (_botDivLineY - botInner.y), botNewZ);

        if (distX > _splitDist)
        {
            float dynamicSplitDist = _splitDist * (1f + _currentZoom);
            if (distX > dynamicSplitDist && _stateChangeCooldown <= 0f)
            {
                _splitHoldTimer += Time.deltaTime;
                if (_splitHoldTimer >= splitConfirmTime)
                    FireSplit(topIsActive);
            }
            else
            {
                _splitHoldTimer = 0f;
            }
        }
        else
        {
            _splitHoldTimer = 0f;
        }
    }

    // ── Split ─────────────────────────────────────────────────────────────────

    private void DriveSplitCameras(bool topIsActive, float distX)
    {
        // Look-ahead fades to zero as characters approach so the active camera
        // naturally decelerates near the merge point.
        float lookScale = Mathf.Clamp01(distX / mergeThreshold);

        // ── Active camera ─────────────────────────────────────────────────────
        // Blend from switchSmoothTime → followSmoothTime after a character switch
        // so the reframe to the newly active character eases in rather than snapping.
        float activeSmoothTime = Mathf.Lerp(switchSmoothTime, followSmoothTime,
                                            Mathf.Clamp01(_switchTimer / SwitchSmoothFade));
        if (topIsActive)
        {
            float lookTarget = Mathf.Clamp(_topFacingDir * lookAheadDistance * lookScale,
                                           -MaxLookAheadUnits, MaxLookAheadUnits);
            _topLookAheadX = Mathf.SmoothDamp(_topLookAheadX, lookTarget,
                                               ref _topLookAheadVel, lookAheadSmoothTime);
            MoveX(topVirtualCamera.transform, topCharacter.position.x + _topLookAheadX,
                  ref _topVelX, activeSmoothTime);
            _botLookAheadX   = 0f;
            _botLookAheadVel = 0f;
        }
        else
        {
            float lookTarget = Mathf.Clamp(_botFacingDir * lookAheadDistance * lookScale,
                                           -MaxLookAheadUnits, MaxLookAheadUnits);
            _botLookAheadX = Mathf.SmoothDamp(_botLookAheadX, lookTarget,
                                               ref _botLookAheadVel, lookAheadSmoothTime);
            MoveX(bottomVirtualCamera.transform, bottomCharacter.position.x + _botLookAheadX,
                  ref _botVelX, activeSmoothTime);
            _topLookAheadX   = 0f;
            _topLookAheadVel = 0f;
        }

        float activeCamX = topIsActive ? topVirtualCamera.transform.position.x
                                       : bottomVirtualCamera.transform.position.x;

        // ── Inactive camera ───────────────────────────────────────────────────
        //
        // Phase 1 — initial pan (InactivePanDuration seconds):
        //   Reframes from the shared together-position to _inactiveFixedX so
        //   the split doesn't snap.
        //
        // Phase 2 — frozen at _inactiveFixedX until characters are within
        //   mergeThreshold of each other. Only then does the inactive camera
        //   start blending toward the active camera's X. This means the pip
        //   (inactive character) doesn't visibly drift in its panel until
        //   the two characters are genuinely close, and the cameras are
        //   already nearly aligned by the time the merge fires.
        //
        //   Using activeCamX (rather than inactiveCharX) as the blend target
        //   guarantees camGap → 0 at merge time regardless of look-ahead.

        float inactiveCharX = topIsActive ? bottomCharacter.position.x : topCharacter.position.x;

        if (_inactivePanTimer < inactivePanDuration)
            _inactivePanTimer += Time.deltaTime;

        float panT     = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_inactivePanTimer / inactivePanDuration));
        float panX     = Mathf.Lerp(_inactiveStartX, _inactiveFixedX, panT);

        // InverseLerp remaps distX so that:
        //   distX == mergeThreshold → blendT = 0  (frozen at _inactiveFixedX)
        //   distX == mergeDistance  → blendT = 1  (fully at activeCamX)
        //
        // This guarantees convergenceX == activeCamX exactly when the merge condition
        // fires, so the snap at merge is mathematically zero regardless of splitDist.
        float mergeBlendT  = Mathf.Clamp01(Mathf.InverseLerp(mergeThreshold, mergeDistance, distX));
        float convergenceX = Mathf.Lerp(_inactiveFixedX, activeCamX, mergeBlendT);

        // Phase 1 — instant SetX so the SmoothStep pan drives precisely.
        // Phase 2 — SmoothDamp toward convergenceX. Carrying velocity across
        //           frames means direction reversals decelerate smoothly.
        if (panT < 1f)
        {
            if (topIsActive) { SetX(bottomVirtualCamera.transform, panX); _botVelX = 0f; }
            else             { SetX(topVirtualCamera.transform,    panX); _topVelX = 0f; }
        }
        else
        {
            // ── Inactive camera smooth time ────────────────────────────────────
            //
            // Three inputs are blended to produce a final smooth time:
            //
            //  0. Switch blend (highest priority immediately after a switch):
            //     Both cameras start at switchSmoothTime so they are driven by
            //     the exact same curve during the reframe. Without this the
            //     active camera (SmoothDamp) and the inactive camera
            //     (distance/velocity based) use completely different profiles,
            //     making the two panels feel out-of-sync and wobbly.
            //     This fades out over SwitchSmoothFade seconds just like the
            //     active camera's own blend.
            //
            //  1. Distance-based (quadratic): drops steeply as characters
            //     approach so the camera is tightly converged near the merge
            //     point during normal walking/slow movement.
            //
            //  2. Velocity-based (adaptive): when characters close at high speed
            //     (dash, burrow exit, running jump) the distance-based curve
            //     cannot react in time — characters traverse the full
            //     mergeThreshold → mergeDistance zone in only a few frames.
            //     Reading the actual Rigidbody horizontal velocities lets us
            //     drop the smooth time to its minimum before the gap opens,
            //     guaranteeing the inactive camera stays aligned at merge.
            float distFraction = Mathf.Clamp01(distX / _splitDist);
            float distSmooth   = Mathf.Lerp(InactiveCamSmoothTimeNear,
                                             InactiveCamSmoothTimeFar,
                                             distFraction * distFraction);

            // Rate at which the characters' X-separation is shrinking.
            // separationSign × (topVelX − botVelX) > 0 means separating,
            //                                        < 0 means approaching.
            float topVX_rb      = _topRb != null ? _topRb.linearVelocity.x : 0f;
            float botVX_rb      = _botRb != null ? _botRb.linearVelocity.x : 0f;
            float sepSign       = Mathf.Sign(topCharacter.position.x - bottomCharacter.position.x);
            float approachSpeed = Mathf.Max(0f, -(sepSign * (topVX_rb - botVX_rb)));

            // Blend distSmooth → InactiveCamSmoothTimeNear as approach speed
            // rises. Full reduction at InactiveApproachSpeedCap (18 m/s).
            float speedBlend     = Mathf.Clamp01(approachSpeed / InactiveApproachSpeedCap);
            float inactiveSmooth = Mathf.Lerp(distSmooth, InactiveCamSmoothTimeNear, speedBlend);

            // Blend from switchSmoothTime → inactiveSmooth over SwitchSmoothFade
            // so that immediately after a character switch both cameras are driven
            // by the same smooth time, then diverge to their individual targets.
            float switchBlend        = Mathf.Clamp01(_switchTimer / SwitchSmoothFade);
            float inactiveSmoothFinal = Mathf.Lerp(switchSmoothTime, inactiveSmooth, switchBlend);

            if (topIsActive) MoveX(bottomVirtualCamera.transform, convergenceX, ref _botVelX, inactiveSmoothFinal);
            else             MoveX(topVirtualCamera.transform,    convergenceX, ref _topVelX, inactiveSmoothFinal);
        }

        // ── Vertical follow + Z pull-back (split mode) ───────────────────────
        // Baseline: _topCharBaseY / _botCharBaseY are captured at the moment of
        // FireSplit (when characters are on the ground), so these deltas are 0
        // at rest and grow only when characters deviate from that ground level.
        //
        // Top camera  — topDy > 0 when top character jumps ABOVE their split-time Y.
        // Bottom camera — botDy > 0 when bottom character falls BELOW their split-time Y.
        float topDy = Mathf.Max(0f, topCharacter.position.y    - _topCharBaseY);
        float botDy = Mathf.Max(0f, _botCharBaseY - bottomCharacter.position.y);

        float topSafeHT = Mathf.Max(_topHalfTan, 0.001f);
        float botSafeHT = Mathf.Max(_botHalfTan, 0.001f);

        // Mirror the exact Together-mode fraction formula:
        //   vZoom = charDeltaY * fraction / (halfTan * origDist)
        // so at equal character Y-displacement the split pull-back is identical
        // in magnitude to what the merged camera would produce. Visible effect
        // is guaranteed to match what the user already sees in merged mode.
        float topVertFrac = topDy * splitVerticalFollowFraction
                          / Mathf.Max(topSafeHT * _topOriginalDist, 0.001f);
        float botVertFrac = botDy * splitVerticalFollowFraction
                          / Mathf.Max(botSafeHT * _botOriginalDist, 0.001f);

        // Base zoom floor: whichever is larger — the inspector minimum or the
        // residual Together-mode zoom still decaying after the split.
        float topBaseZoomFrac = Mathf.Max(splitBaseZoom, _currentZoom);
        float botBaseZoomFrac = Mathf.Max(splitBaseZoom, _currentZoom);

        // ── Per-camera zoom targets ────────────────────────────────────────────
        //
        // Active camera:
        //   • Computes the natural pull-back from the character's vertical position.
        //   • _lockedSplitZoom acts as a FLOOR: the camera cannot zoom in below it.
        //   • The lock itself decays toward the natural target at splitZoomLockDecayRate,
        //     so controlling the character and losing vertical distance gradually
        //     lets the camera zoom back in — it never snaps inward on a switch.
        //   • If the natural target exceeds the lock (zooming out further), the lock
        //     jumps immediately to match.
        //
        // Inactive camera:
        //   • Lock is completely frozen. Target = locked value. No change until active.

        if (topIsActive)
        {
            float naturalTop = Mathf.Min(
                (topBaseZoomFrac + topVertFrac) * _topOriginalDist, maxSplitZPullback);

            // Lock can grow instantly but only shrinks at the decay rate.
            if (naturalTop > _topLockedSplitZoom)
                _topLockedSplitZoom = naturalTop;
            else
                _topLockedSplitZoom = Mathf.Max(naturalTop,
                    _topLockedSplitZoom - splitZoomLockDecayRate * Time.deltaTime);

            float topPullTarget = Mathf.Max(naturalTop, _topLockedSplitZoom);
            _topSplitZoom = Mathf.SmoothDamp(_topSplitZoom, topPullTarget,
                                              ref _topSplitZoomVel, splitZoomSmoothTime);

            // Inactive (bot) — hold completely frozen.
            _botSplitZoom = Mathf.SmoothDamp(_botSplitZoom, _botLockedSplitZoom,
                                              ref _botSplitZoomVel, splitZoomSmoothTime);
        }
        else
        {
            float naturalBot = Mathf.Min(
                (botBaseZoomFrac + botVertFrac) * _botOriginalDist, maxSplitZPullback);

            if (naturalBot > _botLockedSplitZoom)
                _botLockedSplitZoom = naturalBot;
            else
                _botLockedSplitZoom = Mathf.Max(naturalBot,
                    _botLockedSplitZoom - splitZoomLockDecayRate * Time.deltaTime);

            float botPullTarget = Mathf.Max(naturalBot, _botLockedSplitZoom);
            _botSplitZoom = Mathf.SmoothDamp(_botSplitZoom, botPullTarget,
                                              ref _botSplitZoomVel, splitZoomSmoothTime);

            // Inactive (top) — hold completely frozen.
            _topSplitZoom = Mathf.SmoothDamp(_topSplitZoom, _topLockedSplitZoom,
                                              ref _topSplitZoomVel, splitZoomSmoothTime);
        }

        ApplySplitPositions();

        if (distX < mergeDistance)
        {
            _mergeHoldTimer += Time.deltaTime;

            float inactiveCamX = topIsActive ? bottomVirtualCamera.transform.position.x
                                             : topVirtualCamera.transform.position.x;
            float camGap = Mathf.Abs(inactiveCamX - activeCamX);

            bool camerasAligned = camGap < MergeCameraAlignThreshold
                                && _mergeHoldTimer >= MergeConfirmTime
                                && _stateChangeCooldown <= 0f;

            if (camerasAligned)
            {
                float activeVel = topIsActive ? _topVelX : _botVelX;
                _topVelX = activeVel;
                _botVelX = activeVel;

                _state               = CamState.Together;
                _stateChangeCooldown = stateChangeCooldown;
                splitScreenEffect.FadeDivider(false, mergeFadeDuration);

                float topK = _topSplitZoom / Mathf.Max(_topOriginalDist, 0.001f);
                float botK = _botSplitZoom / Mathf.Max(_botOriginalDist, 0.001f);
                _currentZoom = (topK + botK) * 0.5f;

                _zoomVel = (_topSplitZoomVel / Mathf.Max(_topOriginalDist, 0.001f)
                          + _botSplitZoomVel / Mathf.Max(_botOriginalDist, 0.001f)) * 0.5f;

                _postMergeTopPullback = _topSplitZoom - _currentZoom * _topOriginalDist;
                _postMergeBotPullback = _botSplitZoom - _currentZoom * _botOriginalDist;
                _postMergeTopVel      = _topSplitZoomVel;
                _postMergeBotVel      = _botSplitZoomVel;

                _topSplitZoom    = 0f;
                _botSplitZoom    = 0f;
                _topSplitZoomVel = 0f;
                _botSplitZoomVel = 0f;
                _mergeHoldTimer  = 0f;
            }
        }
        else
        {
            _mergeHoldTimer = 0f;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Transitions to Split state and initialises the inactive camera pan.</summary>
    private void FireSplit(bool topIsActive)
    {
        _state               = CamState.Split;
        _stateChangeCooldown = stateChangeCooldown;
        _splitHoldTimer      = 0f;

        float activeCharX   = topIsActive ? topCharacter.position.x    : bottomCharacter.position.x;
        float inactiveCharX = topIsActive ? bottomCharacter.position.x : topCharacter.position.x;
        float dir           = Mathf.Sign(activeCharX - inactiveCharX);

        _inactiveFixedX = inactiveCharX + dir * _splitDist;

        _inactiveStartX   = (topIsActive ? bottomVirtualCamera : topVirtualCamera).transform.position.x;
        _inactivePanTimer = 0f;
        _mergeHoldTimer   = 0f;

        if (topIsActive) _botVelX = 0f; else _topVelX = 0f;

        // Physical camera Z pull-back at split time — seeding _topSplitZoom from these
        // prevents any snap since the cameras are already at exactly this position.
        float physTopZoom = _currentZoom * _topOriginalDist + _postMergeTopPullback;
        float physBotZoom = _currentZoom * _botOriginalDist + _postMergeBotPullback;

        // Zoom TARGET derived from actual character world positions right now.
        // _currentZoom lags behind its target (SmoothDamp). If the split fires before
        // _currentZoom converged — e.g. a large vertical gap opened and characters
        // immediately separated horizontally — physZoom is much smaller than the target.
        // Seeding the lock from max(physical, target) guarantees cameras zoom out to
        // the correct distance regardless of how fast the split was triggered.
        // _topSplitZoom / _botSplitZoom still start at physZoom so there is no snap;
        // SmoothDamp carries them smoothly to the lock floor over splitZoomSmoothTime.
        float splitVSpan     = Mathf.Abs(topCharacter.position.y - bottomCharacter.position.y);
        float splitDistX     = Mathf.Abs(topCharacter.position.x - bottomCharacter.position.x);
        float avgOrigDist    = Mathf.Max((_topOriginalDist + _botOriginalDist) * 0.5f, 0.001f);
        float hFrac          = Mathf.Clamp01(splitDistX / _splitDist);
        float zoomAtSplit    = baseZoom + additionalZoom * hFrac
                             + splitVSpan * verticalZoomMultiplier / avgOrigDist;
        float targetTopUnits = Mathf.Min(zoomAtSplit * _topOriginalDist, maxSplitZPullback);
        float targetBotUnits = Mathf.Min(zoomAtSplit * _botOriginalDist, maxSplitZPullback);

        if (topIsActive)
        {
            // Active top: lock = max(physical, target, prev) so a quick split before
            // _currentZoom converged never under-zooms the camera the player controls.
            _topSplitZoom       = physTopZoom;
            _topSplitZoomVel    = _postMergeTopVel;
            _topLockedSplitZoom = Mathf.Max(Mathf.Max(physTopZoom, targetTopUnits), _topLockedSplitZoom);

            // Inactive bot: lock = physical only — camera holds exactly where it was.
            // targetBotUnits must NOT be applied here; it would force the inactive
            // camera to zoom out to match the current character separation, which is
            // the "fully zoomed out on split" bug the user reported.
            _botSplitZoom       = physBotZoom;
            _botSplitZoomVel    = 0f;
            _botLockedSplitZoom = Mathf.Max(physBotZoom, _botLockedSplitZoom);
            if (_botLockedSplitZoom <= 0f) _botLockedSplitZoom = _botSplitZoom;
        }
        else
        {
            // Active bot: lock = max(physical, target, prev).
            _botSplitZoom       = physBotZoom;
            _botSplitZoomVel    = _postMergeBotVel;
            _botLockedSplitZoom = Mathf.Max(Mathf.Max(physBotZoom, targetBotUnits), _botLockedSplitZoom);

            // Inactive top: lock = physical only.
            _topSplitZoom       = physTopZoom;
            _topSplitZoomVel    = 0f;
            _topLockedSplitZoom = Mathf.Max(physTopZoom, _topLockedSplitZoom);
            if (_topLockedSplitZoom <= 0f) _topLockedSplitZoom = _topSplitZoom;
        }

        // Post-merge residuals are now absorbed into the split zoom — clear them.
        _postMergeTopPullback = 0f;
        _postMergeBotPullback = 0f;
        _postMergeTopVel      = 0f;
        _postMergeBotVel      = 0f;

        // Capture character ground-level Y at the moment of split.
        // This is more reliable than Start() which runs before physics settles.
        // topDy / botDy will be 0 at rest and grow only when characters deviate
        // upward (top) or downward (bottom) from this reference.
        _topCharBaseY = topCharacter.position.y;
        _botCharBaseY = bottomCharacter.position.y;

        splitScreenEffect.FadeDivider(true, 0.25f);
    }

    /// <summary>Smoothly moves a camera's X using SmoothDamp.</summary>
    private void MoveX(Transform cam, float targetX, ref float velocity, float smoothTime)
    {
        float x      = Mathf.SmoothDamp(cam.position.x, targetX, ref velocity, smoothTime);
        cam.position = new Vector3(x, cam.position.y, cam.position.z);
    }

    /// <summary>Instantly sets a camera's X with no smoothing.</summary>
    private static void SetX(Transform cam, float x)
    {
        cam.position = new Vector3(x, cam.position.y, cam.position.z);
    }

    /// <summary>
    /// Computes <c>ViewportToWorldPoint</c> using the virtual camera's current position
    /// combined with the physical camera's projection settings (FOV, aspect, clip planes).
    ///
    /// The Cinemachine Brain syncs virtual → physical only at the end of LateUpdate
    /// (via <c>ManualUpdate</c>), so mid-frame the physical camera still holds its
    /// previous-frame position. This helper temporarily repositions the physical camera
    /// to match the virtual camera for the pure math query, then restores it — no
    /// rendering occurs and the Brain never sees the temporary move.
    /// </summary>
    private static Vector3 VirtualViewportToWorldPoint(
        Camera physicalCam, Transform vcamTransform, Vector3 viewportPoint)
    {
        Vector3 savedPos             = physicalCam.transform.position;
        physicalCam.transform.position = vcamTransform.position;
        Vector3 result               = physicalCam.ViewportToWorldPoint(viewportPoint);
        physicalCam.transform.position = savedPos;
        return result;
    }

    /// <summary>
    /// Smooths <c>_currentZoom</c> toward its target each frame.
    /// In Together mode the target scales with both horizontal character separation
    /// and the active character's vertical elevation above baseline (same formula
    /// as split-mode vertical follow so both modes feel consistent).
    /// In Split mode <c>_currentZoom</c> decays to zero — each split camera drives
    /// its own pull-back via <c>_topSplitZoom</c> / <c>_botSplitZoom</c>.
    /// Both modes apply zoom as a physical Z pull-back so the representation is
    /// identical and split/merge transitions are position-continuous.
    /// </summary>
    private void ApplyZoom(float distX, float verticalSpan)
    {
        float targetZoom = 0f;
        if (_state == CamState.Together)
        {
            // Horizontal component: scales from baseZoom at zero separation to
            // baseZoom + additionalZoom at the split threshold.
            float hFraction = Mathf.Clamp01(distX / _splitDist);
            float hZoom     = baseZoom + additionalZoom * hFraction;

            // Vertical component: directly proportional to the world-space gap
            // between both characters, normalised by the average camera-to-scene
            // distance so the fraction is camera-rig-agnostic.
            //   verticalZoomMultiplier = 1 → 1 world unit of gap = 1 unit of Z pull-back
            float avgOrigDist = Mathf.Max((_topOriginalDist + _botOriginalDist) * 0.5f, 0.001f);
            float vZoom       = verticalSpan * verticalZoomMultiplier / avgOrigDist;

            targetZoom = hZoom + vZoom;
        }

        _currentZoom = Mathf.SmoothDamp(_currentZoom, targetZoom, ref _zoomVel, zoomSmoothTime);
    }

    /// <summary>
    /// Positions both cameras each frame in split mode.
    ///
    /// Z axis — cameras pull back by <c>_topSplitZoom</c> / <c>_botSplitZoom</c> world units.
    /// Y axis — adjusted via <c>ViewportToWorldPoint</c> so that the camera's INNER
    ///          viewport edge (the on-screen dividing line) stays locked at its
    ///          original world Y (<c>_topDivLineY</c> / <c>_botDivLineY</c>).
    ///
    /// Effect: as the camera pulls back the OUTER edge expands freely while the
    /// inner edge (= the horizontal boundary between the two split halves) is
    /// "glued" to the same world Y — exactly the asymmetric zoom the user wants.
    /// </summary>
    private void ApplySplitPositions()
    {
        float topNewZ = _topOriginalZ + _camPullbackZ * _topSplitZoom;
        float botNewZ = _botOriginalZ + _camPullbackZ * _botSplitZoom;

        float topNewDist = Mathf.Abs(topNewZ - _topSceneZ);
        float botNewDist = Mathf.Abs(botNewZ - _botSceneZ);

        float topX = topVirtualCamera.transform.position.x;
        float botX = bottomVirtualCamera.transform.position.x;

        // Place virtual cameras at the unmodified baseline Y so VirtualViewportToWorldPoint
        // samples from a known reference — no feedback from previous-frame Y.
        topVirtualCamera.transform.position    = new Vector3(topX, _topOriginalY, topNewZ);
        bottomVirtualCamera.transform.position = new Vector3(botX, _botOriginalY, botNewZ);

        // Measure where the inner edge currently maps in world space.
        // VirtualViewportToWorldPoint temporarily repositions the physical camera
        // to match the virtual camera so the projection math is correct, then restores it.
        Vector3 topInner = VirtualViewportToWorldPoint(topCamera, topVirtualCamera.transform,
                               new Vector3(0.5f, 0f, topNewDist));   // bottom of top view
        Vector3 botInner = VirtualViewportToWorldPoint(bottomCamera, bottomVirtualCamera.transform,
                               new Vector3(0.5f, 1f, botNewDist));   // top of bottom view

        // Correct Y so the inner edge lands exactly on the dividing line.
        // This locks the inner edge while the outer edge expands with pull-back.
        topVirtualCamera.transform.position    = new Vector3(topX,
                                              _topOriginalY + (_topDivLineY - topInner.y),
                                              topNewZ);
        bottomVirtualCamera.transform.position = new Vector3(botX,
                                              _botOriginalY + (_botDivLineY - botInner.y),
                                              botNewZ);
    }

    /// <summary>Resets follow state when the active character changes.</summary>
    private void HandleCharacterSwitched()
    {
        _topVelX = 0f;
        _botVelX = 0f;
        _switchTimer = 0f;
        // Split zoom state is intentionally NOT reset here — cameras stay at their
        // current zoom level and SmoothDamp to the new character's position naturally.

        if (_state != CamState.Split)
        {
            _topLookAheadX   = 0f;
            _botLookAheadX   = 0f;
            _topLookAheadVel = 0f;
            _botLookAheadVel = 0f;
            return;
        }

        bool newTopIsActive = switchManager != null
                           && switchManager.ActiveCharacter != null
                           && switchManager.ActiveCharacter.transform == topCharacter;

        // Seed the NEWLY ACTIVE character's look-ahead from the camera's actual offset
        // relative to its character rather than from zero.
        //
        // Resetting to zero causes a direction reversal: the camera first travels toward
        // the character (zero look-ahead target) then reverses as the look-ahead builds
        // in the facing direction. This is the "rebound" effect on the bottom camera.
        //
        // Starting from the clamped offset means the initial target already matches
        // the camera's current position, so the camera only moves as the look-ahead
        // decays toward the facing direction — always in one direction.
        //
        // The NEWLY INACTIVE character's look-ahead resets to zero so it does not
        // bleed into the next active phase.
        if (newTopIsActive)
        {
            float topOffset  = topVirtualCamera.transform.position.x - topCharacter.position.x;
            _topLookAheadX   = Mathf.Clamp(topOffset, -MaxLookAheadUnits, MaxLookAheadUnits);
            _topLookAheadVel = 0f;
            _botLookAheadX   = 0f;
            _botLookAheadVel = 0f;
        }
        else
        {
            float botOffset  = bottomVirtualCamera.transform.position.x - bottomCharacter.position.x;
            _botLookAheadX   = Mathf.Clamp(botOffset, -MaxLookAheadUnits, MaxLookAheadUnits);
            _botLookAheadVel = 0f;
            _topLookAheadX   = 0f;
            _topLookAheadVel = 0f;
        }

        // Recompute _inactiveFixedX for the new pairing and start a fresh pan
        // from wherever the new inactive camera currently sits.
        float newActiveCharX   = newTopIsActive ? topCharacter.position.x    : bottomCharacter.position.x;
        float newInactiveCharX = newTopIsActive ? bottomCharacter.position.x : topCharacter.position.x;
        float dir              = Mathf.Sign(newActiveCharX - newInactiveCharX);
        _inactiveFixedX        = newInactiveCharX + dir * _splitDist;

        _inactiveStartX   = newTopIsActive
                          ? bottomVirtualCamera.transform.position.x
                          : topVirtualCamera.transform.position.x;
        // Do NOT restart Phase 1 on a character switch — the cameras are already
        // in split positions and only need to reframe to the new pairing.
        // Phase 1 (SmoothStep + SetX) is reserved for FireSplit, which pans from
        // the Together position into the initial split positions. Re-triggering it
        // on a switch gives the inactive camera a completely different motion curve
        // (SmoothStep) from the active camera (SmoothDamp), causing the wobbly
        // out-of-sync feel. Skip straight to Phase 2 by setting the timer to done.
        _inactivePanTimer = inactivePanDuration;
    }
}