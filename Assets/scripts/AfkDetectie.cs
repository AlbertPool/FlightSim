
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class AfkDetectie : MonoBehaviour
{
    [Header("AFK Instellingen")]
    [SerializeField] private float gameAfkTijd = 300f;
    [SerializeField] private float menuAfkTijd = 120f;

    [Header("Scene Instellingen")]
    [SerializeField] private string hoofdmenuSceneNaam = "MainMenu";

    [Header("UI")]
    [SerializeField] private GameObject afkPanel;
    [SerializeField] private Button jaKnop;
    [SerializeField] private Button hoofdmenuKnop;

    private float laatsteActiviteit;
    private bool isAfk;
    private bool isMenuScene;

    private void Start()
    {
        laatsteActiviteit = Time.unscaledTime;

        // Controleer of we in het hoofdmenu zitten.
        isMenuScene =
            SceneManager.GetActiveScene().name == hoofdmenuSceneNaam;

        if (afkPanel != null)
            afkPanel.SetActive(false);

        if (jaKnop != null)
            jaKnop.onClick.AddListener(SpelerIsErNog);

        if (hoofdmenuKnop != null)
            hoofdmenuKnop.onClick.AddListener(TerugNaarHoofdmenu);
    }

    private void Update()
    {
        if (isAfk)
            return;

        // Registreer muis- en toetsenbordactiviteit.
        if (Input.anyKeyDown ||
            Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2) ||
            Input.mouseScrollDelta.sqrMagnitude > 0f ||
            Input.GetAxisRaw("Mouse X") != 0f ||
            Input.GetAxisRaw("Mouse Y") != 0f)
        {
            laatsteActiviteit = Time.unscaledTime;
        }

        float ingesteldeTijd = isMenuScene
            ? menuAfkTijd
            : gameAfkTijd;

        if (Time.unscaledTime - laatsteActiviteit >= ingesteldeTijd)
        {
            if (isMenuScene)
            {
                // In het menu ga je altijd terug naar het hoofdmenu.
                TerugNaarHoofdmenu();
            }
            else
            {
                // Tijdens gameplay verschijnt de AFK-melding.
                ToonAfkPanel();
            }
        }
    }

    private void ToonAfkPanel()
    {
        isAfk = true;

        if (afkPanel != null)
            afkPanel.SetActive(true);

        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void SpelerIsErNog()
    {
        isAfk = false;
        laatsteActiviteit = Time.unscaledTime;

        if (afkPanel != null)
            afkPanel.SetActive(false);

        Time.timeScale = 1f;
    }

    public void TerugNaarHoofdmenu()
    {
        Time.timeScale = 1f;

        if (!string.IsNullOrWhiteSpace(hoofdmenuSceneNaam))
        {
            SceneManager.LoadScene(hoofdmenuSceneNaam);
        }
        else
        {
            SceneManager.LoadScene(0);
        }
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
    }
}
