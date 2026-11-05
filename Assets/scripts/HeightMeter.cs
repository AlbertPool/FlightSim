using UnityEngine;
using TMPro;
using UnityEngine.Localization;

// Shows how high the plane is above the ground, in meters (1 Unity unit = 1 meter).
// 0 m = the bottom of the plane's collider touches the ground.
// Put this script on the plane (the same object as PlaneScript and the Box Collider).
public class HeightMeter : MonoBehaviour
{
    [Tooltip("Optional: a TextMeshPro text on your Canvas. If empty, a simple on-screen label is drawn.")]
    [SerializeField] private TMP_Text heightText;
    [Tooltip("Which layers count as ground (e.g. Terrain). Default: everything.")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [Tooltip("How far down the meter looks for ground.")]
    [SerializeField] private float maxCheckDistance = 2000f;
    [SerializeField] private string label = "Hoogte";

    private readonly LocalizedString localizedLabel = new LocalizedString("Level1_Table", "Hoogte_Key");
    private string currentLocalizedLabel;
    private Collider planeCollider;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    public float HeightInMeters { get; private set; }

    void Awake()
    {
        planeCollider = GetComponent<Collider>();
    }

    void OnEnable()
    {
        localizedLabel.StringChanged += OnLocalizedLabelChanged;
    }

    void OnDisable()
    {
        localizedLabel.StringChanged -= OnLocalizedLabelChanged;
    }

    void OnLocalizedLabelChanged(string value)
    {
        currentLocalizedLabel = value;
    }

    void Update()
    {
        // Measure from the bottom of the plane's collider, so 0 means "on the ground"
        Bounds bounds = planeCollider != null ? planeCollider.bounds : new Bounds(transform.position, Vector3.zero);
        Vector3 origin = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
        float bottomY = bounds.min.y;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, maxCheckDistance,
            groundLayers, QueryTriggerInteraction.Ignore);

        // Find the closest hit that is not part of the plane itself
        float groundY = float.NegativeInfinity;
        float closest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].distance < closest)
            {
                closest = hits[i].distance;
                groundY = hits[i].point.y;
            }
        }

        if (!float.IsNegativeInfinity(groundY))
        {
            HeightInMeters = Mathf.Max(0f, bottomY - groundY);
        }

        if (heightText != null)
        {
            heightText.text = $"{DisplayLabel}: {HeightInMeters:0} m";
        }
    }

    private string DisplayLabel => string.IsNullOrEmpty(currentLocalizedLabel) ? label : currentLocalizedLabel;

    // Fallback display when no TextMeshPro text is assigned
    void OnGUI()
    {
        if (heightText != null) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;

        string text = $"{DisplayLabel}: {HeightInMeters:0} m";
        GUI.Label(new Rect(22, 22, 400, 50), text, new GUIStyle(style) { normal = { textColor = Color.black } });
        GUI.Label(new Rect(20, 20, 400, 50), text, style);
    }
}
