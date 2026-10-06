using UnityEngine;

public class DisplayManager : MonoBehaviour
{
    [SerializeField] private Camera spectatorCamera;

    private void Start()
    {
        if (Display.displays.Length > 1)
        {
            Display.displays[1].Activate();   // zonder dit blijft scherm 2 zwart
            spectatorCamera.targetDisplay = 1;
        }
        else
        {
            spectatorCamera.gameObject.SetActive(false);
        }
    }
}
