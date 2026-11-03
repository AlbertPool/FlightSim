using UnityEngine;

public class ringetjedraai : MonoBehaviour
{
    public float draaisnelheid = 20f;

    void Update()
    {
        transform.Rotate(0f, 0f, draaisnelheid * Time.deltaTime);
    }
}
