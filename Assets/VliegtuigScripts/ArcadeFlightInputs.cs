using UnityEngine;

[RequireComponent(typeof(ArcadeFlightController))]
public class ArcadeFlightInputs : MonoBehaviour
{
    public bool invertPitch;
    ArcadeFlightController flight;
    void Awake() { flight = GetComponent<ArcadeFlightController>(); }
    void Update()
    {
        float pitch = 0f, turn = 0f, throttle = 0f;
        bool brake = false;

        pitch = (Input.GetKey(KeyCode.S) ? 1f : 0f) - (Input.GetKey(KeyCode.W) ? 1f : 0f);
        turn = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        throttle = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        brake = Input.GetKey(KeyCode.Space);

        flight.SetInput(
    invertPitch ? -pitch : pitch,
    turn,
    throttle,
    brake
);
    }

    void OnDisable() { if (flight) flight.SetInput(0f, 0f, 0f, false); }
}