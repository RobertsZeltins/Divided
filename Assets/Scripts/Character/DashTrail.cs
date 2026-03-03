using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns ghost SpriteRenderer snapshots behind the character during a dash.
/// Each ghost is placed at the character's current world position, rendered in
/// solid white (using the Custom/WhiteFlash shader), and faded out over ghostLifetime.
///
/// Setup:
///   Attach to the top character's root GameObject alongside Movement.
///   The shader 'Custom/WhiteFlash' must exist in Assets/Materials.
/// </summary>
[RequireComponent(typeof(Movement))]
public class DashTrail : MonoBehaviour
{
    [Header("Trail Settings")]
    [Tooltip("Seconds between each ghost snapshot during a dash.")]
    [SerializeField] private float spawnInterval = 0.02f;

    [Tooltip("How long each ghost takes to fully fade out after spawning.")]
    [SerializeField] private float ghostLifetime = 0.18f;

    [Tooltip("Alpha of the ghost at spawn (before fading). 1 = fully opaque white.")]
    [SerializeField] private float startAlpha = 0.8f;

    [Tooltip("Scale multiplier applied to each ghost. 1 = same size as character.")]
    [SerializeField] private float ghostScale = 1.05f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Movement       _movement;
    private SpriteRenderer _characterSr;
    private Material       _ghostMaterial;

    private float _spawnTimer;
    private bool  _wasDAashing;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _movement    = GetComponent<Movement>();
        _characterSr = GetComponent<SpriteRenderer>();

        Shader sh = Shader.Find("Custom/WhiteFlash");
        if (sh != null)
            _ghostMaterial = new Material(sh);
        else
            Debug.LogWarning("[DashTrail] Shader 'Custom/WhiteFlash' not found.");
    }

    private void Update()
    {
        if (!_movement.IsDashing)
        {
            _spawnTimer  = 0f;
            _wasDAashing = false;
            return;
        }

        // Spawn first ghost immediately on dash start.
        if (!_wasDAashing)
        {
            SpawnGhost();
            _wasDAashing = true;
            _spawnTimer  = spawnInterval;
            return;
        }

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
        {
            SpawnGhost();
            _spawnTimer = spawnInterval;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void SpawnGhost()
    {
        if (_characterSr == null || _ghostMaterial == null) return;

        var go = new GameObject("DashGhost");
        go.layer = gameObject.layer;

        // Position and scale match the character exactly at this moment.
        go.transform.position   = transform.position;
        go.transform.rotation   = transform.rotation;
        go.transform.localScale = transform.lossyScale * ghostScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = _characterSr.sprite;
        sr.sortingLayerID = _characterSr.sortingLayerID;
        sr.sortingOrder   = _characterSr.sortingOrder - 1; // render behind character
        sr.material       = _ghostMaterial;
        sr.color          = new Color(1f, 1f, 1f, startAlpha);

        StartCoroutine(FadeAndDestroy(sr, go));
    }

    /// <summary>Fades a ghost SpriteRenderer to transparent then destroys its GameObject.</summary>
    private IEnumerator FadeAndDestroy(SpriteRenderer sr, GameObject go)
    {
        float elapsed = 0f;
        while (elapsed < ghostLifetime && sr != null)
        {
            elapsed  += Time.deltaTime;
            float a   = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / ghostLifetime));
            sr.color  = new Color(1f, 1f, 1f, a);
            yield return null;
        }

        if (go != null) Destroy(go);
    }
}
