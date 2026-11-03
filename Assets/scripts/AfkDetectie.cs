
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

        ControleerScene();

        if (afkPanel != null)
            afkPanel.SetActive(false);

        if (jaKnop != null)
        {
            jaKnop.onClick.RemoveListener(SpelerIsErNog);
            jaKnop.onClick.AddListener(SpelerIsErNog);
        }

        if (hoofdmenuKnop != null)
        {
            hoofdmenuKnop.onClick.RemoveListener(TerugNaarHoofdmenu);
            hoofdmenuKnop.onClick.AddListener(TerugNaarHoofdmenu);
        }
    }

    private void ControleerScene()
    {
        isMenuScene =
            SceneManager.GetActiveScene().name == hoofdmenuSceneNaam;
    }

    private void Update()
    {
        if (isAfk)
            return;

        // Controleer toetsenbordactiviteit.
        bool toetsenbord = Input.anyKeyDown;

        // Controleer muisactiviteit.
        bool muis =
            Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2) ||
            Input.mouseScrollDelta.sqrMagnitude > 0f ||
            Input.GetAxisRaw("Mouse X") != 0f ||
            Input.GetAxisRaw("Mouse Y") != 0f;

        if (toetsenbord || muis)
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
                TerugNaarHoofdmenu();
            }
            else
            {
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
