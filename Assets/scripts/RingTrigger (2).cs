using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Put this on every ring. It creates a trigger zone in the hole of the ring
/// and tells the RingManager when the plane flies through it.
/// </summary>
public class RingTrigger : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Tag on the root of the plane (AirplaneRoot).")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Radius of the hit zone inside the ring (local units). Tweak until the yellow gizmo fills the hole.")]
    [SerializeField] private float hitRadius = 1.5f;

    [Tooltip("Offset of the hit zone if the ring's pivot isn't in the centre.")]
    [SerializeField] private Vector3 hitOffset = Vector3.zero;

    [Header("Feedback")]
    [Tooltip("Fade out the ring after it's been hit.")]
    [SerializeField] private bool hideOnHit = true;
    [SerializeField, Min(0.01f)] private float fadeDuration = 1f;
    [SerializeField] private AudioClip hitSound;

    public bool IsHit { get; private set; }

    private sealed class FadeColor
    {
        public Material material;
        public int propertyId;
        public Color initialColor;
    }

    private SphereCollider hitZone;
    private Renderer[] ringRenderers;
    private bool[] initialRendererStates;
    private Material[][] originalMaterials;
    private readonly System.Collections.Generic.List<Material> fadeMaterials = new();
    private readonly System.Collections.Generic.List<FadeColor> fadeColors = new();
    private bool sequenceActive = true;
    private bool isFading;
    private float fadeElapsed;

    private void Awake()
    {
        // Create a dedicated trigger so the ring's own (mesh) collider stays untouched.
        hitZone = gameObject.AddComponent<SphereCollider>();
        hitZone.isTrigger = true;
        hitZone.radius = hitRadius;
        hitZone.center = hitOffset;

        ringRenderers = GetComponentsInChildren<Renderer>(true);
        initialRendererStates = new bool[ringRenderers.Length];
        for (var i = 0; i < ringRenderers.Length; i++)
            initialRendererStates[i] = ringRenderers[i].enabled;
    }

    private void Start()
    {
        if (RingManager.Instance != null)
            RingManager.Instance.RegisterRing(this);
        else
            Debug.LogWarning("No RingManager in the scene!", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!sequenceActive || IsHit) return;
        if (!other.transform.root.CompareTag(playerTag)) return;

        IsHit = true;

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, transform.position);

        if (hideOnHit)
            BeginFadeOut();

        RingManager.Instance?.RingHit(this);
    }

    public void SetSequenceActive(bool active)
    {
        sequenceActive = active;
        hitZone.enabled = active && !IsHit;

        if (!IsHit || !hideOnHit)
            SetRenderersVisible(active);
    }

    private void Update()
    {
        if (!isFading) return;

        fadeElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(fadeElapsed / fadeDuration);
        foreach (FadeColor fadeColor in fadeColors)
        {
            Color color = fadeColor.initialColor;
            color.a *= 1f - progress;
            fadeColor.material.SetColor(fadeColor.propertyId, color);
        }

        if (progress >= 1f)
            CompleteFadeOut();
    }

    private void BeginFadeOut()
    {
        originalMaterials = new Material[ringRenderers.Length][];
        fadeColors.Clear();
        fadeMaterials.Clear();

        for (int rendererIndex = 0; rendererIndex < ringRenderers.Length; rendererIndex++)
        {
            Renderer ringRenderer = ringRenderers[rendererIndex];
            if (ringRenderer == null) continue;

            Material[] sourceMaterials = ringRenderer.sharedMaterials;
            Material[] instances = new Material[sourceMaterials.Length];
            originalMaterials[rendererIndex] = sourceMaterials;

            for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
            {
                Material source = sourceMaterials[materialIndex];
                if (source == null) continue;

                Material instance = new Material(source);
                instances[materialIndex] = instance;
                fadeMaterials.Add(instance);

                int colorProperty = instance.HasProperty("_BaseColor")
                    ? Shader.PropertyToID("_BaseColor")
                    : instance.HasProperty("_Color")
                        ? Shader.PropertyToID("_Color")
                        : -1;

                if (colorProperty < 0)
                {
                    Debug.LogWarning($"Material '{source.name}' has no color property for fading.", source);
                    continue;
                }

                ConfigureTransparentMaterial(instance);
                fadeColors.Add(new FadeColor
                {
                    material = instance,
                    propertyId = colorProperty,
                    initialColor = instance.GetColor(colorProperty)
                });
            }

            ringRenderer.sharedMaterials = instances;
        }

        fadeElapsed = 0f;
        isFading = true;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 0);

        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void CompleteFadeOut()
    {
        isFading = false;
        SetRenderersVisible(false);
        RestoreOriginalMaterials();
    }

    private void RestoreOriginalMaterials()
    {
        if (originalMaterials != null)
        {
            for (int i = 0; i < ringRenderers.Length; i++)
            {
                if (ringRenderers[i] != null && originalMaterials[i] != null)
                    ringRenderers[i].sharedMaterials = originalMaterials[i];
            }
        }

        foreach (Material material in fadeMaterials)
        {
            if (material != null)
                Destroy(material);
        }

        originalMaterials = null;
        fadeMaterials.Clear();
        fadeColors.Clear();
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterials();
    }

    private void SetRenderersVisible(bool visible)
    {
        for (var i = 0; i < ringRenderers.Length; i++)
        {
            if (ringRenderers[i] != null)
                ringRenderers[i].enabled = visible && initialRendererStates[i];
        }
    }

    // Shows the hit zone in the Scene view so you can size it.
    private void OnDrawGizmos()
    {
        Gizmos.color = IsHit ? Color.green : Color.yellow;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireSphere(hitOffset, hitRadius);
    }
}
