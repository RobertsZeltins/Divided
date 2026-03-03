using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a brief color flash when a character becomes active after a switch.
///
///   Top character    → true white flash via a stacked white overlay SpriteRenderer
///                       (alpha 1→0). SpriteRenderer.color is clamped 0–1 and white
///                       is the default tint, so a separate overlay is the only way
///                       to render "brighter than normal" without HDR post-processing.
///   Bottom character → black flash via direct sr.color lerp (darkens then returns).
/// </summary>
public class CharacterSwitchFlash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterSwitchManager switchManager;
    [SerializeField] private Movement topCharacter;
    [SerializeField] private Movement bottomCharacter;

    [Header("Flash Settings")]
    [Tooltip("Duration of the flash in seconds.")]
    [SerializeField] private float flashDuration = 0.4f;

    [Tooltip("Flash color for the bottom character. Lerps FROM this TO white.")]
    [SerializeField] private Color bottomFlashColor = Color.black;

    private SpriteRenderer _topOverlay;
    private Coroutine      _topFlash;
    private Coroutine      _botFlash;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (topCharacter != null)
            _topOverlay = CreateWhiteOverlay(topCharacter);
    }

    private void OnEnable()
    {
        if (switchManager != null) switchManager.OnCharacterSwitched += HandleCharacterSwitched;
    }

    private void OnDisable()
    {
        if (switchManager != null) switchManager.OnCharacterSwitched -= HandleCharacterSwitched;
    }

    // ── Switch handler ────────────────────────────────────────────────────────

    private void HandleCharacterSwitched()
    {
        bool topIsActive = switchManager.ActiveCharacter == topCharacter;
        if (topIsActive)
        {
            if (_topFlash != null) StopCoroutine(_topFlash);
            _topFlash = StartCoroutine(WhiteOverlayFlash());
        }
        else
        {
            if (_botFlash != null) StopCoroutine(_botFlash);
            _botFlash = StartCoroutine(ColorFlash(bottomCharacter, bottomFlashColor));
        }
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    /// <summary>Fades the stacked white overlay from fully opaque to transparent.</summary>
    private IEnumerator WhiteOverlayFlash()
    {
        if (_topOverlay == null) yield break;

        // Sync sprite in case it changed since Start.
        SpriteRenderer charSr = topCharacter.GetComponent<SpriteRenderer>();
        if (charSr != null) _topOverlay.sprite = charSr.sprite;

        _topOverlay.color   = Color.white;
        _topOverlay.enabled = true;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            float a             = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / flashDuration));
            _topOverlay.color   = new Color(1f, 1f, 1f, a);
            yield return null;
        }

        _topOverlay.color   = new Color(1f, 1f, 1f, 0f);
        _topOverlay.enabled = false;
    }

    /// <summary>Lerps character's SpriteRenderer RGB from flashColor to white. Alpha is preserved.</summary>
    private IEnumerator ColorFlash(Movement character, Color flashColor)
    {
        SpriteRenderer sr = character != null ? character.GetComponent<SpriteRenderer>() : null;
        if (sr == null) yield break;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            float savedAlpha = sr.color.a;
            Color c          = Color.Lerp(flashColor, Color.white, Mathf.Clamp01(elapsed / flashDuration));
            c.a              = savedAlpha;
            sr.color         = c;
            yield return null;
        }

        Color final = sr.color;
        final.r = 1f; final.g = 1f; final.b = 1f;
        sr.color = final;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an invisible child SpriteRenderer one sorting order above the character,
    /// used exclusively as the white flash overlay.
    /// </summary>
    private static SpriteRenderer CreateWhiteOverlay(Movement character)
    {
        SpriteRenderer original = character.GetComponent<SpriteRenderer>();

        var go = new GameObject("WhiteFlashOverlay");
        go.transform.SetParent(character.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale    = Vector3.one;

        var sr = go.AddComponent<SpriteRenderer>();
        if (original != null)
        {
            sr.sprite         = original.sprite;
            sr.sortingLayerID = original.sortingLayerID;
            sr.sortingOrder   = original.sortingOrder + 1;
        }

        sr.color   = new Color(1f, 1f, 1f, 0f);
        sr.enabled = false;
        return sr;
    }
}
