using UnityEngine;

/// <summary>
/// Zet dit script alleen in de scene van level 1.
/// Het blokkeert links/rechts (toetsenbord en ESP32 roll-sensor),
/// zodat het vliegtuig alleen omhoog en omlaag kan.
/// In andere levels staat dit script er niet, dus daar werkt alles normaal.
/// </summary>
public class VerticalOnlyLock : MonoBehaviour
{
    [Tooltip("Laat leeg als dit script op het vliegtuig zelf staat.")]
    [SerializeField] private PlaneScript plane;

    void Awake()
    {
        if (plane == null)
            plane = GetComponent<PlaneScript>();

        if (plane == null)
            plane = FindFirstObjectByType<PlaneScript>();

        if (plane == null)
            Debug.LogWarning("VerticalOnlyLock: geen PlaneScript gevonden.", this);
    }

    void OnEnable()
    {
        if (plane != null)
            plane.HorizontalLocked = true;
    }

    void OnDisable()
    {
        if (plane != null)
            plane.HorizontalLocked = false;
    }
}
