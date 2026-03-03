using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages all visuals for the burrowing ability:
///   HOLE TRAIL  — Pushes shrinking world-space circles to DiggableWall every frame,
///                 creating a fully transparent tunnel that closes behind the character.
///   DRILL SHAPE — Shows a procedural arrowhead (ZTest Always) while burrowing.
/// </summary>
public class BurrowVisuals : MonoBehaviour
{
    [Header("Hole Trail")]
    [Tooltip("World-space radius of the hole at full size.")]
    [SerializeField] private float holeRadius   = 0.42f;

    [Tooltip("Seconds for each hole point to fully close.")]
    [SerializeField] private float holeLifetime = 0.70f;

    [Tooltip("Min travel distance before a new hole point is added.")]
    [SerializeField] private float holeSpacing  = 0.10f;

    [Header("Drill Visual")]
    [SerializeField] private Color drillColor    = new Color(1f, 0.87f, 0.3f, 1f);
    [SerializeField] private float drillLength   = 0.65f;
    [SerializeField] private float drillRadius   = 0.28f;
    [SerializeField] private float spinSpeed     = 520f;

    [Tooltip("Smooth time for the drill rotating to face the burrowing direction. Lower = snappier.")]
    [SerializeField] private float drillTurnTime = 0.04f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private SpriteRenderer _sprite;
    private Rigidbody      _rb;
    private Transform      _drillTransform;
    private MeshRenderer   _drillMr;
    private float          _spinAngle;
    private float          _drillAngle;
    private float          _drillAngleVel;
    private Vector2        _dir    = Vector2.right;
    private bool           _active;

    private struct HolePoint { public Vector2 Pos; public float BirthTime; }
    private readonly HolePoint[] _ring       = new HolePoint[64];
    private int                  _ringHead   = 0;
    private int                  _ringCount  = 0;
    private Vector2              _lastAddPos;
    private DiggableWall         _wall;
    private readonly Vector4[]   _holeBuf    = new Vector4[64];

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _sprite = GetComponent<SpriteRenderer>();
        _rb     = GetComponent<Rigidbody>();
        BuildDrillMesh();
        SetDrillActive(false);
    }

    private void Update()       { UpdateHoleTrail(); }
    private void LateUpdate()   { UpdateDrillTransform(); }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Called by DiggingAbility on burrow entry.</summary>
    public void OnBurrowStart(Vector2 entryDir, DiggableWall wall)
    {
        _active     = true;
        _spinAngle  = 0f;
        _dir        = entryDir.sqrMagnitude > 0.01f ? entryDir.normalized : Vector2.right;
        _drillAngle = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        _drillAngleVel = 0f;
        _wall       = wall;
        _ringCount  = 0;
        _ringHead   = 0;
        _lastAddPos = new Vector2(transform.position.x, transform.position.y);
        AddPoint(_lastAddPos);

        if (_sprite != null) _sprite.enabled = false;
        SetDrillActive(true);
    }

    /// <summary>Called by DiggingAbility on burrow exit.</summary>
    public void OnBurrowEnd()
    {
        _active = false;
        if (_sprite != null) _sprite.enabled = true;
        SetDrillActive(false);
        // _wall kept — trail continues to close via UpdateHoleTrail.
    }

    /// <summary>
    /// Starts the burrow-death sequence without immediately tearing down state:
    /// <list type="bullet">
    ///   <item>Sets _active = false so existing hole points close naturally over holeLifetime.</item>
    ///   <item>Scales the drill visual to zero over <paramref name="duration"/> seconds.</item>
    /// </list>
    /// UpdateDrillTransform() returns early when _active is false, so this coroutine
    /// owns the drill scale for the full duration without any conflict.
    /// Intended to be yielded by CharacterRespawnManager before ForceExitBurrow().
    /// </summary>
    public System.Collections.IEnumerator BurrowDeathFade(float duration)
    {
        _active = false; // Stop trail; existing holes close on their own over holeLifetime.

        // Even if the drill is inactive (e.g. character was in Entering phase and drill
        // had not yet rendered), we still wait the full duration so the caller's timing
        // is consistent. Skip only the scale animation, not the wait.
        bool drillVisible = _drillTransform != null
                         && _drillTransform.gameObject.activeSelf;

        Vector3 startScale = drillVisible ? _drillTransform.localScale : Vector3.zero;
        float   elapsed    = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (drillVisible)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                _drillTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            }
            yield return null;
        }

        if (drillVisible) SetDrillActive(false);
    }

    /// <summary>Called by DiggingAbility each FixedUpdate.</summary>
    public void SetBurrowDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude > 0.01f) _dir = dir;
    }

    // ── Hole trail ────────────────────────────────────────────────────────────

    private void UpdateHoleTrail()
    {
        if (_wall == null) return;

        if (_active)
        {
            Vector2 cur = new Vector2(transform.position.x, transform.position.y);
            if (Vector2.Distance(cur, _lastAddPos) >= holeSpacing)
            {
                AddPoint(cur);
                _lastAddPos = cur;
            }
        }

        float now     = Time.time;
        int   outN    = 0;

        // Walk ring from newest → oldest.
        for (int i = 0; i < _ringCount; i++)
        {
            int   idx    = (_ringHead - 1 - i + 64) % 64;
            float age    = now - _ring[idx].BirthTime;
            if (age >= holeLifetime) { _ringCount = i; break; }

            float r = Mathf.SmoothStep(holeRadius, 0f, age / holeLifetime);
            if (r < 0.005f) continue;
            if (outN < 64)
                _holeBuf[outN++] = new Vector4(_ring[idx].Pos.x, _ring[idx].Pos.y, r, 0f);
        }

        // Pin the character's current position at full radius while active.
        if (_active && outN < 64)
        {
            Vector2 p = new Vector2(transform.position.x, transform.position.y);
            _holeBuf[outN++] = new Vector4(p.x, p.y, holeRadius, 0f);
        }

        _wall.UpdateHoles(_holeBuf, outN);

        if (!_active && outN == 0)
        {
            _wall.ClearHoles();
            _wall = null;
        }
    }

    private void AddPoint(Vector2 pos)
    {
        _ring[_ringHead] = new HolePoint { Pos = pos, BirthTime = Time.time };
        _ringHead        = (_ringHead + 1) % 64;
        if (_ringCount < 64) _ringCount++;
    }

    // ── Drill visual ──────────────────────────────────────────────────────────

    private void UpdateDrillTransform()
    {
        if (!_active || _drillTransform == null) return;

        // Read from velocity for frame-accurate direction (not FixedUpdate-lagged).
        // Fall back to the stored _dir if the character is nearly stopped.
        if (_rb != null)
        {
            Vector2 vel = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.y);
            if (vel.sqrMagnitude > 0.1f)
                _dir = vel.normalized;
        }

        _spinAngle += spinSpeed * Time.deltaTime;
        float widthMul = Mathf.Abs(Mathf.Sin(_spinAngle * Mathf.Deg2Rad));

        // Smooth the target angle so rapid input reversals don't snap.
        float targetAngle = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        _drillAngle = Mathf.SmoothDampAngle(_drillAngle, targetAngle, ref _drillAngleVel, drillTurnTime);

        // World-space rotation avoids parent scale inversion (Movement flips
        // the character transform's X scale to face left/right).
        _drillTransform.rotation = Quaternion.Euler(0f, 0f, _drillAngle);

        // localScale.x must counteract the parent's X-axis sign so the mesh
        // vertices are not mirrored when the character is facing left.
        float parentXSign = transform.lossyScale.x >= 0f ? 1f : -1f;
        _drillTransform.localScale = new Vector3(parentXSign, Mathf.Lerp(0.08f, 1f, widthMul), 1f);
    }

    private void BuildDrillMesh()
    {
        var go = new GameObject("DrillVisual");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        _drillTransform = go.transform;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.mesh = BuildArrowheadMesh();

        Shader sh  = Shader.Find("Custom/DrillCharacter");
        var    mat = sh != null ? new Material(sh) : new Material(Shader.Find("Sprites/Default"));
        if (sh != null) mat.SetColor("_BaseColor", drillColor);
        else            mat.color = drillColor;

        mr.sharedMaterial    = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        _drillMr             = mr;
    }

    private Mesh BuildArrowheadMesh()
    {
        float shoulder = drillLength * 0.42f;
        float neckR    = drillRadius * 0.45f;

        var mesh = new Mesh { name = "DrillArrowhead" };
        mesh.vertices = new Vector3[]
        {
            new(0f,          drillRadius,  0f),
            new(0f,         -drillRadius,  0f),
            new(shoulder,    neckR,         0f),
            new(shoulder,   -neckR,         0f),
            new(drillLength, 0f,            0f),
        };
        mesh.triangles = new int[]
        {
            0,2,1, 1,2,3, 2,4,3,   // front face
            1,2,0, 3,2,1, 3,4,2,   // back face
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    private void SetDrillActive(bool active)
    {
        if (_drillTransform != null)
            _drillTransform.gameObject.SetActive(active);
    }
}