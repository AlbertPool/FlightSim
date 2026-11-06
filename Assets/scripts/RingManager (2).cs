using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public event Action<RingTrigger> RingWasHit;

    public int TotalRings { get; private set; }
    public int RingsHit { get; private set; }
    public bool HasWon { get; private set; }
    public bool HasLanded { get; private set; }

    private bool allRingsEventRaised;

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
        RingWasHit?.Invoke(ring);
        UpdateCounter();

        if (!allRingsEventRaised && TotalRings > 0 && RingsHit >= TotalRings)
        {
            allRingsEventRaised = true;
            OnAllRingsHit?.Invoke();
        }

        TryCompleteLevel();
    }

    public void PlaneLanded()
    {
        HasLanded = true;
        TryCompleteLevel();
    }

    private void TryCompleteLevel()
    {
        if (HasWon || !HasLanded || TotalRings == 0 || RingsHit < TotalRings)
            return;

        CompleteLevel();
    }

    private void UpdateCounter()
    {
        if (counterText != null)
            counterText.text = $"{RingsHit} / {TotalRings}";
    }

    private void CompleteLevel()
    {
        HasWon = true;

        if (winText != null) winText.text = winMessage;
        if (winPanel != null) winPanel.SetActive(true);

        if (winSound != null && Camera.main != null)
            AudioSource.PlayClipAtPoint(winSound, Camera.main.transform.position);

        int nextBuildIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError(
                $"Level completed, but there is no enabled scene at build index {nextBuildIndex}. " +
                "Add the next level to Build Settings.",
                this
            );
            return;
        }

        SceneManager.LoadSceneAsync(nextBuildIndex);
    }
}
