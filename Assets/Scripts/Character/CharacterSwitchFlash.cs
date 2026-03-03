using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a brief color flash when a character becomes active after a switch.
///
///   Top character    → additive white overlay SpriteRenderer child (alpha 0→1→0).
///                       Uses Custom/WhiteFlash shader with raw atlas UVs and
///                       Blend SrcAlpha One so the white is always additively visible.
///   Bottom character → black flash via direct SpriteRenderer color lerp.
/// </summary>
public class CharacterSwitchFlash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterSwitchManager switchManager;
    [SerializeField] private Movement topCharacter;
    [SerializeField] private Movement bottomCharacter;

    [Header("Flash Settings")]
    [Tooltip("Duration of the full flash cycle (fade in + fade out) in seconds.")]
    [SerializeField] private float flashDuration = 0.4f;

    [Tooltip("Flash color for the bottom character. Lerps FROM this TO white over flashDuration.")]
    [SerializeField] private Color bottomFlashColor = Color.black;

    private SpriteRenderer _topOverlay;
    private Material       _overlayMaterial;
    private Coroutine      _topFlash;
    private Coroutine      _botFlash;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (topCharacter == null) return;

        Shader sh = Shader.Find("Custom/WhiteFlash");
        if (sh == null)
        {
            Debug.LogError("[CharacterSwitchFlash] Shader 'Custom/WhiteFlash' not found.");
            return;
        }

        _overlayMaterial = new Material(sh);
        _topOverlay      = CreateOverlay(topCharacter, _overlayMaterial);
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

    /// <summary>
    /// Triangle-wave alpha on the additive white overlay: 0 → 1 → 0.
    /// The overlay uses Blend SrcAlpha One so white is additively added to the
    /// character — always visible regardless of the character's own material.
    /// </summary>
    private IEnumerator WhiteOverlayFlash()
    {
        if (_topOverlay == null) yield break;

        // Refresh the sprite each time in case the animator changed it.
        SpriteRenderer charSr = topCharacter.GetComponent<SpriteRenderer>();
        if (charSr != null) _topOverlay.sprite = charSr.sprite;

        _topOverlay.enabled = true;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / flashDuration);
            float alpha = 1f - Mathf.Abs(2f * t - 1f); // triangle: 0 → 1 → 0
            _topOverlay.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        _topOverlay.color   = new Color(1f, 1f, 1f, 0f);
        _topOverlay.enabled = false;
    }

    /// <summary>
    /// Lerps the character's SpriteRenderer RGB from flashColor to white.
    /// Alpha is preserved to avoid conflicting with respawn fades.
    /// </summary>
    private IEnumerator ColorFlash(Movement character, Color flashColor)
    {
        SpriteRenderer sr = character != null ? character.GetComponent<SpriteRenderer>() : null;
        if (sr == null) yield break;

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed      += Time.deltaTime;
            float saved   = sr.color.a;
            Color c       = Color.Lerp(flashColor, Color.white, Mathf.Clamp01(elapsed / flashDuration));
            c.a           = saved;
            sr.color      = c;
            yield return null;
        }

        Color final = sr.color;
        final.r = 1f; final.g = 1f; final.b = 1f;
        sr.color = final;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a child SpriteRenderer one sorting order above the character.
    /// Starts disabled and fully transparent — only activated during a flash.
    /// </summary>
    private static SpriteRenderer CreateOverlay(Movement character, Material material)
    {
        SpriteRenderer original = character.GetComponent<SpriteRenderer>();

        var go = new GameObject("WhiteFlashOverlay");
        go.transform.SetParent(character.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale    = Vector3.one;
        go.layer                   = character.gameObject.layer;

        var sr = go.AddComponent<SpriteRenderer>();
        if (original != null)
        {
            sr.sprite         = original.sprite;
            sr.sortingLayerID = original.sortingLayerID;
            sr.sortingOrder   = original.sortingOrder + 1;
        }

        sr.material = material;
        sr.color    = new Color(1f, 1f, 1f, 0f);
        sr.enabled  = false;
        return sr;
    }
}
