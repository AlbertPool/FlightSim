using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ResetSceneScript : MonoBehaviour
{
    [Header("Popup")]
    [SerializeField] private GameObject popupPanel;
    [SerializeField] private TMP_Text popupText;
    [SerializeField] private string popupMessage = "You have to land the plane to complete the level.";
    [SerializeField, Min(0f)] private float restartDelay = 2f;

    private bool hasTriggered;

    private void Awake()
    {
        if (popupPanel != null)
            popupPanel.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleBoundaryHit(other.transform);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleBoundaryHit(collision.collider.transform);
    }

    private void HandleBoundaryHit(Transform other)
    {
        if (hasTriggered || !IsPlayer(other))
            return;

        hasTriggered = true;

        if (popupText != null)
            popupText.text = popupMessage;
        if (popupPanel != null)
            popupPanel.SetActive(true);

        StartCoroutine(RestartAfterBoundaryHit());
    }

    private static bool IsPlayer(Transform other)
    {
        while (other != null)
        {
            if (other.CompareTag("Player"))
                return true;

            other = other.parent;
        }

        return false;
    }

    private IEnumerator RestartAfterBoundaryHit()
    {
        yield return new WaitForSecondsRealtime(restartDelay);

        int currentBuildIndex = SceneManager.GetActiveScene().buildIndex;
        if (currentBuildIndex < 0)
        {
            Debug.LogError("Cannot restart the level because the active scene is not in Build Settings.", this);
            yield break;
        }

        SceneManager.LoadSceneAsync(currentBuildIndex);
    }

    private void OnGUI()
    {
        if (!hasTriggered || (popupPanel != null && popupText != null))
            return;

        Rect messageRect = new Rect(Screen.width / 2f - 250f, Screen.height / 2f - 60f, 500f, 120f);
        GUI.Box(messageRect, GUIContent.none);

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 32,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        GUI.Label(messageRect, popupMessage, style);
    }
}
