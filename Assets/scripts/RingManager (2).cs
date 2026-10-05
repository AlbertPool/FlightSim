using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Keeps track of how many rings have been flown through,
/// updates the "x / total" counter and shows the win screen.
/// </summary>
public class RingManager : MonoBehaviour
{
    public static RingManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TMP_Text winText;
    [SerializeField] private string winMessage = "You won!";

    [Header("Audio")]
    [SerializeField] private AudioClip winSound;

    /// <summary>Hook for later, e.g. to start the landing or the outro.</summary>
    public event Action OnAllRingsHit;

    public int TotalRings { get; private set; }
    public int RingsHit { get; private set; }
    public bool HasWon { get; private set; }

    private void Awake()
    {
        Instance = this;
        if (winPanel != null) winPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RegisterRing(RingTrigger ring)
    {
        TotalRings++;
        UpdateCounter();
    }

    public void RingHit(RingTrigger ring)
    {
        if (HasWon) return;

        RingsHit++;
        UpdateCounter();

        if (RingsHit >= TotalRings)
            Win();
    }

    private void UpdateCounter()
    {
        if (counterText != null)
            counterText.text = $"{RingsHit} / {TotalRings}";
    }

    private void Win()
    {
        HasWon = true;

        if (winText != null) winText.text = winMessage;
        if (winPanel != null) winPanel.SetActive(true);

        if (winSound != null && Camera.main != null)
            AudioSource.PlayClipAtPoint(winSound, Camera.main.transform.position);

        OnAllRingsHit?.Invoke();
    }
}
