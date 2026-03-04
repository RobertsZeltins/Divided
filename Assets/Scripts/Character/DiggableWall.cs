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

        // Apply the hole material immediately and keep it forever.
        // Switching materials at burrow start/end caused a visible color pop
        // because URP can produce different GI/shadow results per material instance.
        // With a permanent single material there is nothing to switch — holes are
        // controlled purely through the _HoleMap texture.
        if (_sandRenderer != null && _holeMaterial != null)
            _sandRenderer.sharedMaterial = _holeMaterial;
    }

    private void OnDestroy()
    {
        // Restore the original so the asset is not left pointing at our runtime material
        // in the Editor after play mode ends.
        if (_sandRenderer != null && _originalMaterial != null)
            _sandRenderer.sharedMaterial = _originalMaterial;

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

    /// <summary>Clears holes. Called by BurrowVisuals when the trail closes.</summary>
    public void ClearHoles()
    {
        ClearHoleTexture();
        // No material restore — wall stays on _holeMaterial permanently.
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

    /// <summary>
    /// Copies a texture and its tiling/offset from the original material to the
    /// hole material, translating the source property name to the destination one.
    /// No-ops silently if the source property doesn't exist or has no texture.
    /// </summary>
    private void CopyTexture(Material source, string srcProp, string dstProp)
    {
        if (!source.HasProperty(srcProp)) return;
        Texture tex = source.GetTexture(srcProp);
        if (tex == null) return;
        _holeMaterial.SetTexture(dstProp, tex);
        _holeMaterial.SetVector(dstProp + "_ST", source.GetVector(srcProp + "_ST"));
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

        if (_originalMaterial == null)
        {
            _holeMaterial.SetTexture(HoleMapId, _holeTexture);
            return;
        }

        // ── Base colour ───────────────────────────────────────────────────────
        Color c = new Color(0.82f, 0.65f, 0.28f, 1f);
        if      (_originalMaterial.HasProperty("_BaseColor")) c = _originalMaterial.GetColor("_BaseColor");
        else if (_originalMaterial.HasProperty("_Color"))     c = _originalMaterial.GetColor("_Color");
        c.a = 1f;
        _holeMaterial.SetColor("_BaseColor", c);

        // ── Albedo texture + tiling ───────────────────────────────────────────
        CopyTexture(_originalMaterial, "_BaseMap", "_BaseMap");
        CopyTexture(_originalMaterial, "_MainTex", "_BaseMap"); // fallback

        // ── Normal map ────────────────────────────────────────────────────────
        CopyTexture(_originalMaterial, "_BumpMap", "_BumpMap");
        if (_originalMaterial.HasProperty("_BumpScale"))
            _holeMaterial.SetFloat("_BumpScale", _originalMaterial.GetFloat("_BumpScale"));

        // ── Occlusion ─────────────────────────────────────────────────────────
        CopyTexture(_originalMaterial, "_OcclusionMap", "_OcclusionMap");
        if (_originalMaterial.HasProperty("_OcclusionStrength"))
            _holeMaterial.SetFloat("_OcclusionStrength", _originalMaterial.GetFloat("_OcclusionStrength"));

        // ── Smoothness / Metallic ─────────────────────────────────────────────
        if (_originalMaterial.HasProperty("_Smoothness"))
            _holeMaterial.SetFloat("_Smoothness", _originalMaterial.GetFloat("_Smoothness"));
        if (_originalMaterial.HasProperty("_Metallic"))
            _holeMaterial.SetFloat("_Metallic",   _originalMaterial.GetFloat("_Metallic"));

        _holeMaterial.SetTexture(HoleMapId, _holeTexture);
    }
}
