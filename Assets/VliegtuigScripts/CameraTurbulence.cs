using UnityEngine;

public class CameraTurbulence : MonoBehaviour
{
    [Header("Vliegtuig")]
    public Flugzeugbesturing airplane;

    [Header("Turbulentie")]
    public float positionStrength = 0.025f;
    public float rotationStrength = 0.6f;
    public float frequency = 2.5f;
    public float smoothness = 8f;

    [Header("Snelheidsinvloed")]
    public float fullTurbulenceSpeed = 20f;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float randomOffset;

    void Start()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        randomOffset = Random.Range(0f, 1000f);
    }

    void LateUpdate()
    {
        if (airplane == null)
        {
            return;
        }

        float speedMultiplier = Mathf.Clamp01(
            airplane.CurrentSpeed / fullTurbulenceSpeed
        );

        // Minder turbulentie terwijl het vliegtuig nog op de grond staat.
        if (airplane.IsGrounded)
        {
            speedMultiplier *= 0.35f;
        }

        float time = Time.time * frequency;

        float noiseX =
            Mathf.PerlinNoise(randomOffset, time) * 2f - 1f;

        float noiseY =
            Mathf.PerlinNoise(randomOffset + 10f, time) * 2f - 1f;

        float noiseZ =
            Mathf.PerlinNoise(randomOffset + 20f, time) * 2f - 1f;

        Vector3 positionOffset = new Vector3(
            noiseX,
            noiseY,
            noiseZ
        ) * positionStrength * speedMultiplier;

        Quaternion rotationOffset = Quaternion.Euler(
            noiseY * rotationStrength * speedMultiplier,
            noiseX * rotationStrength * speedMultiplier,
            noiseZ * rotationStrength * speedMultiplier
        );

        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            startPosition + positionOffset,
            smoothness * Time.deltaTime
        );

        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            startRotation * rotationOffset,
            smoothness * Time.deltaTime
        );
    }
}