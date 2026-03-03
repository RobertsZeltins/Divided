using System.Collections.Generic;
using UnityEngine;

public class DiggableWall : MonoBehaviour
{
    [Header("Collision")]
    [SerializeField] private Collider solidCollider;
    [SerializeField] private Collider burrowZone;

    [Header("VFX")]
    [SerializeField] private ParticleSystem sandParticles;

    // ── Private ───────────────────────────────────────────────────────────────

    private static readonly int HoleMapId = Shader.PropertyToID("_HoleMap");
    private const int MaxHoles = 64;

    private readonly HashSet<Collider> _activeBurrowers = new();
    private readonly Color[]           _holePixels      = new Color[MaxHoles];

    private MeshRenderer _sandRenderer;
    private Material     _originalMaterial;
    private Material     _holeMaterial;
    private Texture2D    _holeTexture;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (solidCollider == null)
            solidCollider = GetComponent<Collider>();

        if (burrowZone != null && !burrowZone.isTrigger)
            burrowZone.isTrigger = true;

        _sandRenderer     = GetComponent<MeshRenderer>();
        _originalMaterial = _sandRenderer != null ? _sandRenderer.sharedMaterial : null;

        BuildHoleMaterial();
    }

    private void OnDestroy()
    {
        if (_holeMaterial != null) Destroy(_holeMaterial);
        if (_holeTexture  != null) Destroy(_holeTexture);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>World-space bounds of the solid sand collider. Used by DiggingAbility for near-edge detection.</summary>
    public Bounds SandBounds => solidCollider != null ? solidCollider.bounds : new Bounds();

    /// <summary>
    /// World-space bounds of the burrow zone trigger. This is the collider that fires
    /// OnTriggerExit, so edge detection must use this — not the solid collider — to
    /// ensure the detection window is in sync with the actual trigger boundary.
    /// Falls back to SandBounds if burrowZone is not assigned.
    /// </summary>
    public Bounds BurrowZoneBounds => burrowZone != null ? burrowZone.bounds : SandBounds;

    public void RegisterBurrower(Collider burrowerCollider)
    {
        if (!_activeBurrowers.Add(burrowerCollider)) return;

        Physics.IgnoreCollision(solidCollider, burrowerCollider, true);

        if (_activeBurrowers.Count == 1)
        {
            ClearHoleTexture();
            if (_sandRenderer != null)
                _sandRenderer.sharedMaterial = _holeMaterial != null ? _holeMaterial : _originalMaterial;
            if (sandParticles != null) sandParticles.Play();
        }
    }

    public void UnregisterBurrower(Collider burrowerCollider)
    {
        if (!_activeBurrowers.Remove(burrowerCollider)) return;

        Physics.IgnoreCollision(solidCollider, burrowerCollider, false);

        if (_activeBurrowers.Count == 0)
            if (sandParticles != null) sandParticles.Stop();
    }

    /// <summary>Called by BurrowVisuals every frame. Each entry: (worldX, worldY, radius, 0).</summary>
    public void UpdateHoles(Vector4[] holes, int count)
    {
        if (_holeMaterial == null || _holeTexture == null) return;

        for (int i = 0; i < MaxHoles; i++)
        {
            if (i < count)
                _holePixels[i] = new Color(holes[i].x, holes[i].y, holes[i].z, 0f);
            else
                _holePixels[i] = Color.clear;
        }

        _holeTexture.SetPixels(_holePixels);
        _holeTexture.Apply(false, false);
    }

    /// <summary>Clears holes and restores the original material. Called by BurrowVisuals when trail closes.</summary>
    public void ClearHoles()
    {
        ClearHoleTexture();
        if (_sandRenderer != null)
            _sandRenderer.sharedMaterial = _originalMaterial;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnTriggerExit(Collider other)
    {
        if (!_activeBurrowers.Contains(other)) return;
        other.GetComponent<DiggingAbility>()?.OnExitedSandVolume(this);
    }

    private void ClearHoleTexture()
    {
        if (_holeTexture == null) return;
        for (int i = 0; i < MaxHoles; i++) _holePixels[i] = Color.clear;
        _holeTexture.SetPixels(_holePixels);
        _holeTexture.Apply(false, false);
    }

    private void BuildHoleMaterial()
    {
        Shader sh = Shader.Find("Custom/BurrowHole");
        if (sh == null)
        {
            Debug.LogWarning("[DiggableWall] Shader 'Custom/BurrowHole' not found. Holes will not appear.", this);
            return;
        }

        // 64x1 float texture — each pixel encodes one hole: rg=worldXY, b=radius
        _holeTexture             = new Texture2D(MaxHoles, 1, TextureFormat.RGBAFloat, false);
        _holeTexture.filterMode  = FilterMode.Point;
        _holeTexture.wrapMode    = TextureWrapMode.Clamp;
        ClearHoleTexture();

        _holeMaterial = new Material(sh) { name = "BurrowHole_Runtime" };

        // Inherit sand colour
        Color c = new Color(0.82f, 0.65f, 0.28f, 1f);
        if (_originalMaterial != null)
        {
            if      (_originalMaterial.HasProperty("_BaseColor")) c = _originalMaterial.GetColor("_BaseColor");
            else if (_originalMaterial.HasProperty("_Color"))     c = _originalMaterial.GetColor("_Color");
        }
        c.a = 1f;
        _holeMaterial.SetColor("_BaseColor", c);

        // Inherit texture
        if (_originalMaterial != null)
        {
            Texture tex = null;
            if      (_originalMaterial.HasProperty("_BaseMap"))  tex = _originalMaterial.GetTexture("_BaseMap");
            else if (_originalMaterial.HasProperty("_MainTex"))  tex = _originalMaterial.GetTexture("_MainTex");
            if (tex != null) _holeMaterial.SetTexture("_MainTex", tex);
        }

        _holeMaterial.SetTexture(HoleMapId, _holeTexture);
    }
}
