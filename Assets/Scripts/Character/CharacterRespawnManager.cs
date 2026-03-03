using System.Collections;
using UnityEngine;

// ── DIAGNOSTIC LOGGING IS ACTIVE ────────────────────────────────────────────
// Remove all Debug.Log / Debug.LogWarning / Debug.LogError calls in this file
// once the fade is confirmed working in the console.
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stores each character's world position at Start() as the respawn point.
/// When Respawn() is called (e.g. by SpikeObstacle), the character fades out,
/// teleports to its spawn point, and fades back in. Input and physics are locked
/// for the duration of the sequence.
/// </summary>
public class CharacterRespawnManager : MonoBehaviour
{
    public static CharacterRespawnManager Instance { get; private set; }

    [Header("Characters")]
    [SerializeField] private Movement topCharacter;
    [SerializeField] private Movement bottomCharacter;

    [Tooltip("DiggingAbility on the bottom character — used to force-exit the burrow on death.")]
    [SerializeField] private DiggingAbility bottomDigging;

    [Header("Timing")]
    [Tooltip("Time in seconds for the character to fade out before teleporting.")]
    [SerializeField] private float fadeOutDuration = 0.4f;

    [Tooltip("Time in seconds for the character to fade back in after teleporting.")]
    [SerializeField] private float fadeInDuration  = 0.5f;

    // Cached at Start — avoids any runtime GetComponent ambiguity.
    private Vector3        _topSpawnPos;
    private Vector3        _bottomSpawnPos;
    private SpriteRenderer _topSr;
    private SpriteRenderer _bottomSr;
    private BurrowVisuals  _bottomBurrowVisuals;

    private bool _topRespawning;
    private bool _botRespawning;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (topCharacter != null)
        {
            _topSpawnPos = topCharacter.transform.position;
            _topSr       = topCharacter.GetComponent<SpriteRenderer>();
        }

        if (bottomCharacter != null)
        {
            _bottomSpawnPos      = bottomCharacter.transform.position;
            _bottomSr            = bottomCharacter.GetComponent<SpriteRenderer>();
            _bottomBurrowVisuals = bottomCharacter.GetComponent<BurrowVisuals>();
        }

        // ── Diagnostic — remove once fade is confirmed ────────────────────────
        Debug.Log($"[Respawn] topSr={(  _topSr    != null ? _topSr.gameObject.name    : "NULL")}, " +
                  $"bottomSr={(_bottomSr != null ? _bottomSr.gameObject.name : "NULL")}");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Triggers the death-and-respawn sequence for the matching character.</summary>
    public void Respawn(GameObject characterRoot)
    {
        if (topCharacter != null
            && characterRoot == topCharacter.gameObject
            && !_topRespawning)
        {
            Debug.Log("[Respawn] TOP triggered.");
            StartCoroutine(RespawnRoutine(topCharacter, _topSr, null, null, _topSpawnPos, isTop: true));
        }
        else if (bottomCharacter != null
                 && characterRoot == bottomCharacter.gameObject
                 && !_botRespawning)
        {
            Debug.Log("[Respawn] BOTTOM triggered.");
            StartCoroutine(RespawnRoutine(bottomCharacter, _bottomSr, bottomDigging, _bottomBurrowVisuals, _bottomSpawnPos, isTop: false));
        }
        else
        {
            Debug.LogWarning($"[Respawn] Ignored for '{characterRoot.name}' " +
                             $"(topRespawning={_topRespawning}, botRespawning={_botRespawning})");
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private IEnumerator RespawnRoutine(
        Movement character, SpriteRenderer sr,
        DiggingAbility digging, BurrowVisuals burrowVisuals,
        Vector3 spawnPos, bool isTop)
    {
        if (isTop) _topRespawning = true;
        else       _botRespawning = true;

        character.inputEnabled = false;

        bool dyingWhileBurrowing = digging != null
                                && digging.IsBurrowing
                                && burrowVisuals != null;

        Rigidbody rb = character.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Temporarily disable kinematic so PhysX accepts the velocity zero-out,
            // then re-enable. Movement.FreezeOnSpawn() may leave the body kinematic,
            // which causes "Setting linear velocity of a kinematic body" warnings otherwise.
            rb.isKinematic     = false;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
        }

        Debug.Log($"[Respawn] Routine start — sr={(sr != null ? sr.name : "NULL")}, burrowing={dyingWhileBurrowing}");

        if (dyingWhileBurrowing)
        {
            yield return StartCoroutine(burrowVisuals.BurrowDeathFade(fadeOutDuration));
            digging.ForceExitBurrow();
            if (sr != null) sr.enabled = true;
        }
        else
        {
            digging?.ForceExitBurrow();
            yield return StartCoroutine(FadeSprite(sr, from: 1f, to: 0f, fadeOutDuration));
        }

        if (rb != null) rb.isKinematic = false;
        character.transform.position = spawnPos;

        yield return StartCoroutine(FadeSprite(sr, from: 0f, to: 1f, fadeInDuration));

        character.inputEnabled = true;

        if (isTop) _topRespawning = false;
        else       _botRespawning = false;

        Debug.Log($"[Respawn] Routine complete for {character.gameObject.name}.");
    }

    /// <summary>
    /// Tweens the alpha of <paramref name="sr"/> between <paramref name="from"/> and
    /// <paramref name="to"/> over <paramref name="duration"/> seconds.
    /// Always enables the renderer and snaps alpha to <paramref name="from"/> first.
    /// Only alpha is modified — RGB is untouched so CharacterSwitchFlash is unaffected.
    /// </summary>
    private IEnumerator FadeSprite(SpriteRenderer sr, float from, float to, float duration)
    {
        if (sr == null)
        {
            Debug.LogError("[Respawn] FadeSprite — SpriteRenderer is NULL, skipping fade.");
            yield break;
        }

        Debug.Log($"[Respawn] FadeSprite '{sr.gameObject.name}' {from:F2}→{to:F2} ({duration}s)");

        sr.enabled = true;
        Color c = sr.color; c.a = from; sr.color = c;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            c = sr.color;
            c.a = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            sr.color = c;
            yield return null;
        }

        c = sr.color; c.a = to; sr.color = c;
    }
}
