using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the horizontal divider line at screen centre.
/// Fades in slowly when characters diverge (more dramatic) and
/// fades out faster when they reunite (more satisfying).
/// Uses Update instead of coroutines so there are zero heap allocations
/// at split/merge time — eliminating GC-related frame spikes.
/// </summary>
public class SplitScreenEffect : MonoBehaviour
{
    [SerializeField] private Image dividerLine;
    [SerializeField] private float lineThicknessPixels = 6f;

    /// <summary>Current visible alpha of the divider (0 = fully hidden, 1 = fully visible).</summary>
    public float VisualAlpha { get; private set; }

    [Tooltip("How long the divider takes to fade IN. Slower feels more dramatic.")]
    [SerializeField] private float fadeInDuration = 0.55f;

    [Tooltip("How long the divider takes to fade OUT. Faster feels more satisfying.")]
    [SerializeField] private float fadeOutDuration = 0.28f;

    private float _fadeStartAlpha;
    private float _fadeTargetAlpha;
    private float _fadeDuration;
    private float _fadeElapsed;

    private void Awake()
    {
        ConfigureDividerLine();
        SetAlpha(0f);
    }

    private void Update()
    {
        if (_fadeElapsed >= _fadeDuration) return;

        _fadeElapsed += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_fadeElapsed / _fadeDuration));
        SetAlpha(Mathf.Lerp(_fadeStartAlpha, _fadeTargetAlpha, t));
    }

    /// <summary>Smoothly fades the divider line in or out with asymmetric durations.</summary>
    public void FadeDivider(bool visible)
    {
        _fadeStartAlpha  = VisualAlpha;
        _fadeTargetAlpha = visible ? 1f : 0f;
        _fadeDuration    = visible ? fadeInDuration : fadeOutDuration;
        _fadeElapsed     = 0f;
    }

    private void ConfigureDividerLine()
    {
        RectTransform rt    = dividerLine.rectTransform;
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(1f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(0f, lineThicknessPixels);
    }

    private void SetAlpha(float alpha)
    {
        VisualAlpha = alpha;
        Color c     = dividerLine.color;
        c.a         = alpha;
        dividerLine.color = c;
    }
}
