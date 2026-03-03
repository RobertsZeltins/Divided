using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slides a compass pip along the horizontal divider line to track the
/// inactive character's X position, while a needle inside the pip rotates
/// to point toward the character (accounting for which world half they
/// occupy and how far off-screen they are).
///
/// When the character is within the visible viewport the pip tracks them
/// directly and the needle points straight up or down into their world.
/// When the character is off-screen the pip clamps to the nearest edge,
/// the needle tilts diagonally toward them, and a diamond pulses at the edge.
///
/// Everything fades in/out with the divider line alpha.
/// Attach to the CameraCanvas GameObject.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class CharacterCompassTracker : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform              topCharacter;
    [SerializeField] private Transform              bottomCharacter;
    [SerializeField] private Camera                 topCamera;
    [SerializeField] private Camera                 bottomCamera;
    [SerializeField] private CharacterSwitchManager switchManager;
    [SerializeField] private SplitScreenEffect       splitScreenEffect;

    [Header("Pip / Compass")]
    [Tooltip("Diameter of the sliding compass pip in canvas units.")]
    [SerializeField] private float pipDiameter  = 36f;
    [Tooltip("Length of the rotating needle inside the pip.")]
    [SerializeField] private float needleLength = 22f;
    [Tooltip("Width of the needle shaft.")]
    [SerializeField] private float needleWidth  = 3f;
    [Tooltip("Size of the bright square at the needle tip.")]
    [SerializeField] private float tipSize      = 6f;

    [Header("Edge Indicators")]
    [Tooltip("Size of the diamond that appears at the edge when the character is off-screen.")]
    [SerializeField] private float diamondSize = 12f;
    [Tooltip("Fraction of viewport width used as the clamping margin on each side.")]
    [SerializeField] [Range(0.01f, 0.2f)] private float edgeMarginFraction = 0.05f;

    [Header("Edge Pulse")]
    [Tooltip("Oscillations per second when the character is off-screen.")]
    [SerializeField] private float pulseFrequency = 1.8f;
    [Tooltip("Minimum alpha during a pulse (0 = fully invisible at trough).")]
    [SerializeField] [Range(0f, 1f)] private float pulseMinAlpha = 0.25f;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.5f);
    [SerializeField] private Color needleColor     = new Color(1f, 1f, 1f, 0.8f);
    [SerializeField] private Color tipColor        = Color.white;
    [SerializeField] private Color diamondColor    = Color.white;

    [Header("Rotation")]
    [Tooltip("How aggressively the needle tilts when the character is off-screen (clamped). " +
             "Has no effect when the pip is tracking the character directly.")]
    [SerializeField] [Range(0.5f, 8f)] private float offScreenSensitivity = 3f;
    [Tooltip("Speed at which the needle smoothly rotates.")]
    [SerializeField] private float rotationSmoothSpeed = 10f;

    // Runtime UI — kept as fields so LateUpdate can re-apply Inspector values every frame
    private CanvasGroup   _group;
    private RectTransform _canvasRect;
    private RectTransform _rootRect;
    private RectTransform _pipRect;
    private Image         _pipBgImage;
    private RectTransform _needleGroupRect;
    private RectTransform _shaftRect;
    private Image         _shaftImage;
    private RectTransform _tipRect;
    private Image         _tipImage;
    private RectTransform _leftDiamondRect;
    private Image         _leftDiamondImage;
    private RectTransform _rightDiamondRect;
    private Image         _rightDiamondImage;

    // Rotation state
    private float _currentAngle;
    private bool  _snapOnNextFrame = true;

    // Sprite resources
    private Texture2D _whiteTex;
    private Sprite    _whiteSprite;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        _canvasRect = GetComponent<RectTransform>();
        CreateWhiteSprite();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (_whiteTex    != null) Destroy(_whiteTex);
        if (_whiteSprite != null) Destroy(_whiteSprite);
    }

    private void LateUpdate()
    {
        if (_group == null || splitScreenEffect == null) return;

        // Re-apply every Inspector-driven size and color so changes take effect in real time.
        ApplyInspectorValues();

        float dividerAlpha = splitScreenEffect.VisualAlpha;

        if (dividerAlpha < 0.01f)
        {
            _group.alpha     = 0f;
            _snapOnNextFrame = true;
            return;
        }

        bool topIsActive  = switchManager != null
                         && switchManager.ActiveCharacter != null
                         && switchManager.ActiveCharacter.transform == topCharacter;
        Transform inactiveChar = topIsActive ? bottomCharacter : topCharacter;
        Camera    inactiveCam  = topIsActive ? bottomCamera    : topCamera;

        if (inactiveChar == null || inactiveCam == null)
        {
            _group.alpha = 0f;
            return;
        }

        // ── Viewport X from the inactive camera ───────────────────────────────
        // The pip must show where the inactive character appears in the INACTIVE
        // camera's panel — not where it appears relative to the active character.
        //
        // The previous formula used the active character's world X as the centre
        // reference, so the pip jumped whenever the active character accelerated
        // (burrow, dash, fall) while the inactive camera was still frozen at
        // _inactiveFixedX. The two measurements were in different reference frames,
        // causing the visible desync at speed.
        //
        // WorldToViewportPoint on the inactive camera is always in sync with the
        // inactive panel by definition: if the camera hasn't moved, the character
        // reads ≈0.5; once the camera converges the pip correctly drifts with it.
        float viewportX = inactiveCam.WorldToViewportPoint(inactiveChar.position).x;

        float edgeMin    = edgeMarginFraction;
        float edgeMax    = 1f - edgeMarginFraction;
        bool  isOffLeft  = viewportX < edgeMin;
        bool  isOffRight = viewportX > edgeMax;
        bool  isAtEdge   = isOffLeft || isOffRight;
        float clampedX   = Mathf.Clamp(viewportX, edgeMin, edgeMax);

        // ── Slide the pip ──────────────────────────────────────────────────────
        float canvasWidth = _canvasRect.rect.width;
        _pipRect.anchoredPosition = new Vector2((clampedX - 0.5f) * canvasWidth, 0f);

        // ── Rotate the needle ──────────────────────────────────────────────────
        // Needle direction is based on the ACTIVE camera's view of the inactive
        // character — how far to the left/right do you need to look to find them.
        // This is independent of the pip position (which uses the inactive camera).
        Camera activeCam      = topIsActive ? topCamera : bottomCamera;
        float  needleViewport = activeCam.WorldToViewportPoint(inactiveChar.position).x;
        float  dx = (needleViewport - Mathf.Clamp(needleViewport, edgeMin, edgeMax))
                    * offScreenSensitivity;
        // dy: always points toward the inactive character's world half.
        float dy = topIsActive ? -1f : 1f;

        // Atan2(dy, dx) is standard math (0° = right, 90° = up).
        // Subtract 90° to shift 0° → up for Unity's CCW Z-rotation convention.
        float targetAngle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg - 90f;

        if (_snapOnNextFrame)
        {
            _currentAngle    = targetAngle;
            _snapOnNextFrame = false;
        }
        else
        {
            _currentAngle = Mathf.LerpAngle(_currentAngle, targetAngle,
                                             Time.deltaTime * rotationSmoothSpeed);
        }

        _needleGroupRect.localRotation = Quaternion.Euler(0f, 0f, _currentAngle);

        // ── Pulse ──────────────────────────────────────────────────────────────
        float pulse = isAtEdge
            ? Mathf.Lerp(pulseMinAlpha, 1f,
                (Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f)
            : 1f;

        _group.alpha = dividerAlpha;

        // Edge diamonds
        float edgeCanvasX = (edgeMax - 0.5f) * canvasWidth;
        _leftDiamondRect.anchoredPosition  = new Vector2(-edgeCanvasX, 0f);
        _rightDiamondRect.anchoredPosition = new Vector2( edgeCanvasX, 0f);
        SetImageAlpha(_leftDiamondImage,  isOffLeft  ? pulse : 0f);
        SetImageAlpha(_rightDiamondImage, isOffRight ? pulse : 0f);
    }

    // ── UI Construction ───────────────────────────────────────────────────────

    /// <summary>
    /// Re-applies every Inspector-driven size and color to the live UI objects.
    /// Called every LateUpdate so tweaks during play mode take effect immediately.
    /// </summary>
    private void ApplyInspectorValues()
    {
        _rootRect.sizeDelta        = new Vector2(0f, pipDiameter + 8f);
        _pipRect.sizeDelta         = new Vector2(pipDiameter, pipDiameter);
        _needleGroupRect.sizeDelta = new Vector2(needleWidth, needleLength);

        _shaftRect.sizeDelta      = new Vector2(needleWidth, needleLength);
        _shaftImage.color         = needleColor;

        _tipRect.sizeDelta        = new Vector2(tipSize, tipSize);
        _tipRect.anchoredPosition = new Vector2(0f, (needleLength - tipSize) * 0.5f);
        _tipImage.color           = tipColor;

        _pipBgImage.color = backgroundColor;

        _leftDiamondRect.sizeDelta  = new Vector2(diamondSize, diamondSize);
        _rightDiamondRect.sizeDelta = new Vector2(diamondSize, diamondSize);

        // Preserve alpha managed by pulse logic; only update RGB from Inspector color.
        Color ld = _leftDiamondImage.color;
        Color rd = _rightDiamondImage.color;
        _leftDiamondImage.color  = new Color(diamondColor.r, diamondColor.g, diamondColor.b, ld.a);
        _rightDiamondImage.color = new Color(diamondColor.r, diamondColor.g, diamondColor.b, rd.a);
    }

    private void BuildUI()
    {
        // Root spans the full canvas width, centred on the divider line
        GameObject root = new GameObject("__CompassRoot", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);

        _group = root.GetComponent<CanvasGroup>();
        _group.alpha          = 0f;
        _group.interactable   = false;
        _group.blocksRaycasts = false;

        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin        = new Vector2(0f, 0.5f);
        rootRt.anchorMax        = new Vector2(1f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        rootRt.sizeDelta        = new Vector2(0f, pipDiameter + 8f);
        _rootRect = rootRt;

        // ── Pip (sliding container) ────────────────────────────────────────────
        GameObject pip = new GameObject("__Pip", typeof(RectTransform));
        pip.transform.SetParent(root.transform, false);

        _pipRect = pip.GetComponent<RectTransform>();
        _pipRect.anchorMin        = new Vector2(0.5f, 0.5f);
        _pipRect.anchorMax        = new Vector2(0.5f, 0.5f);
        _pipRect.pivot            = new Vector2(0.5f, 0.5f);
        _pipRect.anchoredPosition = Vector2.zero;
        _pipRect.sizeDelta        = new Vector2(pipDiameter, pipDiameter);

        // Circle background inside pip
        BuildCircle(pip.transform);

        // Needle group inside pip — this rotates
        GameObject needleGo = new GameObject("__NeedleGroup", typeof(RectTransform));
        needleGo.transform.SetParent(pip.transform, false);

        _needleGroupRect = needleGo.GetComponent<RectTransform>();
        _needleGroupRect.anchorMin        = new Vector2(0.5f, 0.5f);
        _needleGroupRect.anchorMax        = new Vector2(0.5f, 0.5f);
        _needleGroupRect.pivot            = new Vector2(0.5f, 0.5f);
        _needleGroupRect.anchoredPosition = Vector2.zero;
        _needleGroupRect.sizeDelta        = new Vector2(needleWidth, needleLength);

        // Shaft
        (_shaftRect, _shaftImage) = PlaceImage("__Shaft", needleGo.transform, needleWidth, needleLength,
                                                needleColor, Vector2.zero);

        // Tip at the +Y end of the needle
        float tipYOffset = (needleLength - tipSize) * 0.5f;
        (_tipRect, _tipImage) = PlaceImage("__Tip", needleGo.transform, tipSize, tipSize,
                                           tipColor, new Vector2(0f, tipYOffset));

        // ── Edge diamonds ──────────────────────────────────────────────────────
        (_leftDiamondRect,  _leftDiamondImage)  = MakeDiamond("__LeftDiamond",  root.transform);
        (_rightDiamondRect, _rightDiamondImage) = MakeDiamond("__RightDiamond", root.transform);
        SetImageAlpha(_leftDiamondImage,  0f);
        SetImageAlpha(_rightDiamondImage, 0f);
    }

    /// <summary>Creates the circular pip background using Unity's built-in Knob sprite.</summary>
    private void BuildCircle(Transform parent)
    {
        GameObject go = new GameObject("__PipBg", typeof(Image));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        Sprite circle = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        img.sprite        = circle != null ? circle : _whiteSprite;
        img.color         = backgroundColor;
        img.raycastTarget = false;
        _pipBgImage       = img;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Creates a square (rotated 45° = diamond) edge indicator.</summary>
    private (RectTransform rt, Image img) MakeDiamond(string id, Transform parent)
    {
        GameObject go = new GameObject(id, typeof(Image));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.sprite        = _whiteSprite;
        img.color         = diamondColor;
        img.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(diamondSize, diamondSize);
        rt.localRotation    = Quaternion.Euler(0f, 0f, 45f);

        return (rt, img);
    }

    /// <summary>Creates a solid white Image at the given anchor-relative offset and returns its refs.</summary>
    private (RectTransform rt, Image img) PlaceImage(string id, Transform parent,
                                                      float width, float height,
                                                      Color color, Vector2 anchoredPos)
    {
        GameObject go = new GameObject(id, typeof(Image));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.sprite        = _whiteSprite;
        img.color         = color;
        img.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = new Vector2(width, height);

        return (rt, img);
    }

    private void CreateWhiteSprite()
    {
        _whiteTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _whiteTex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        _whiteTex.Apply();
        _whiteTex.hideFlags = HideFlags.HideAndDontSave;

        _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, 2, 2), new Vector2(1f, 1f), 1f);
        _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private static void SetImageAlpha(Image img, float alpha)
    {
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }
}
