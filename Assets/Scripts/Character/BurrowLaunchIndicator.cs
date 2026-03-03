using UnityEngine;

/// <summary>
/// Renders a pulsing ring around the bottom character when a shift-launch out of
/// the sand wall is available. Attach to a child GameObject of BottomCharacter;
/// wire the DiggingAbility reference in the Inspector.
///
/// Requires a LineRenderer on the same GameObject. A URP Unlit or Sprites/Default
/// material should be assigned to the LineRenderer's Material slot.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class BurrowLaunchIndicator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("DiggingAbility on the BottomCharacter.")]
    [SerializeField] private DiggingAbility diggingAbility;

    [Header("Ring Shape")]
    [Tooltip("Base radius of the ring in world units.")]
    [SerializeField] private float radius = 0.5f;

    [Tooltip("Number of line segments used to draw the circle. 32–48 is smooth enough.")]
    [SerializeField] private int segments = 40;

    [Tooltip("Width of the ring line in world units.")]
    [SerializeField] private float lineWidth = 0.07f;

    [Header("Appearance")]
    [Tooltip("Color of the ring when a launch is available (burrowing and near an edge).")]
    [SerializeField] private Color ringColor = new Color(1f, 0.92f, 0.25f, 1f);

    [Tooltip("Color of the ring when the character is near a sand wall but not yet burrowing.")]
    [SerializeField] private Color proximityColor = new Color(0.6f, 0.9f, 1f, 1f);

    [Header("Animation")]
    [Tooltip("How fast the ring pulses in and out (radians per second).")]
    [SerializeField] private float pulseSpeed = 5f;

    [Tooltip("Fractional radius change per pulse cycle. 0.1 = ±10 %% of radius.")]
    [SerializeField] [Range(0f, 0.3f)] private float pulseAmplitude = 0.12f;

    [Tooltip("Speed at which the ring fades in and out when availability changes.")]
    [SerializeField] private float fadeSpeed = 10f;

    // ── Private ───────────────────────────────────────────────────────────────

    private LineRenderer _lr;
    private float        _alpha;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
        _lr.enabled = false;
    }

    // Disable the LineRenderer in the editor so it doesn't show in scene view.
    private void OnValidate()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr != null) lr.enabled = false;
    }

    private void Update()
    {
        bool launchReady = diggingAbility != null && diggingAbility.LaunchAvailable;
        bool nearWall    = diggingAbility != null && diggingAbility.IsNearSandWall;
        bool available   = launchReady || nearWall;

        float target = available ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, fadeSpeed * Time.deltaTime);

        bool show = _alpha > 0.005f;
        _lr.enabled = show;

        if (!show) return;

        // Pulse the radius.
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude;
        float r     = radius * pulse;

        // Rebuild circle positions.
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            _lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f));
        }

        // Use launch color when a launch is available; proximity color otherwise.
        Color baseColor = launchReady ? ringColor : proximityColor;
        baseColor.a     = _alpha;
        _lr.startColor  = baseColor;
        _lr.endColor    = baseColor;
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void ConfigureLineRenderer()
    {
        _lr.useWorldSpace   = false;
        _lr.loop            = true;
        _lr.positionCount   = segments;
        _lr.startWidth      = lineWidth;
        _lr.endWidth        = lineWidth;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows  = false;

        // Pre-fill positions so the array is the right size.
        for (int i = 0; i < segments; i++)
            _lr.SetPosition(i, Vector3.zero);
    }
}
