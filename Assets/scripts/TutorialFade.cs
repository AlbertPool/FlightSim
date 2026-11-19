using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Laat het tutorial-canvas uitfaden zodra de speler op een knop drukt.
/// Zet dit script op het tutorial-canvas (of een parent ervan).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TutorialFade : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Input Action die de tutorial wegklikt (bijv. de knop op de hendel / gamepad South).")]
    [SerializeField] private InputActionReference dismissAction;

    [Tooltip("Toetsenbord-fallback om te testen zonder hendel.")]
    [SerializeField] private Key fallbackKey = Key.Space;

    [Header("Fade")]
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.75f;

    [Tooltip("Pas na deze tijd kan de tutorial weggeklikt worden (voorkomt per ongeluk overslaan).")]
    [SerializeField, Min(0f)] private float minShowTime = 1f;

    [Tooltip("Zet het GameObject uit als de fade klaar is.")]
    [SerializeField] private bool disableWhenDone = true;

    [Header("Events")]
    [Tooltip("Wordt aangeroepen als de fade klaar is, bijv. om de vlucht te starten.")]
    public UnityEvent onHidden;

    private CanvasGroup canvasGroup;
    private bool isFading;
    private float shownAt;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        isFading = false;
        shownAt = Time.unscaledTime;

        if (dismissAction != null)
        {
            dismissAction.action.performed += OnDismiss;
            dismissAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (dismissAction != null)
            dismissAction.action.performed -= OnDismiss;
    }

    private void Update()
    {
        // Toetsenbord-fallback
        if (Keyboard.current != null && Keyboard.current[fallbackKey].wasPressedThisFrame)
            Hide();
    }

    private void OnDismiss(InputAction.CallbackContext ctx) => Hide();

    /// <summary>
    /// Start de fade. Public, zodat je dit ook vanuit je eigen hendel/Serial-code kunt aanroepen.
    /// </summary>
    public void Hide()
    {
        if (isFading || !isActiveAndEnabled) return;
        if (Time.unscaledTime - shownAt < minShowTime) return;

        StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        isFading = true;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float startAlpha = canvasGroup.alpha;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime; // werkt ook als het spel gepauzeerd is (timeScale = 0)
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        onHidden?.Invoke();

        if (disableWhenDone)
            gameObject.SetActive(false);
    }
}
