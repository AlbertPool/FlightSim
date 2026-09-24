using UnityEngine;

public class Besturing1 : MonoBehaviour
{
    public float speed = 0f;
    public float maxSpeed = 100f;

    public float acceleration = 10f;
    public float pitchSpeed = 40f;
    public float rollSpeed = 60f;

    public float maxPitch = 45f;

    void Update()
    {
        // =========================
        // VOORUIT GAAN - Q
        // =========================

        if (Input.GetKey(KeyCode.Q))
        {
            // Versnel langzaam
            speed += acceleration * Time.deltaTime;
        }
        else
        {
            // Als Q niet wordt ingedrukt, rem langzaam af
            speed -= acceleration * 0.5f * Time.deltaTime;
        }

        // Snelheid mag niet onder 0 of boven maxSpeed
        speed = Mathf.Clamp(speed, 0f, maxSpeed);


        // =========================
        // VOORUIT BEWEGEN
        // =========================

        transform.position += transform.forward * speed * Time.deltaTime;


        // =========================
        // PITCH - W / S
        // =========================

        float pitch = 0f;

        if (Input.GetKey(KeyCode.W))
        {
            pitch = -1f;
        }

        if (Input.GetKey(KeyCode.S))
        {
            pitch = 1f;
        }

        // Langzaam omhoog/omlaag draaien
        transform.Rotate(
            pitch * pitchSpeed * Time.deltaTime,
            0f,
            0f
        );


        // =========================
        // ROLL - A / D
        // =========================

        float roll = 0f;

        if (Input.GetKey(KeyCode.A))
        {
            roll = 1f;
        }

        if (Input.GetKey(KeyCode.D))
        {
            roll = -1f;
        }

        // Op hoge snelheid rolt het vliegtuig sneller
        float currentRollSpeed = rollSpeed + (speed / maxSpeed) * rollSpeed;

        transform.Rotate(
            0f,
            0f,
            roll * currentRollSpeed * Time.deltaTime
        );


        // =========================
        // TE STEIL + TE LANGZAAM
        // =========================

        float angle = Vector3.Angle(transform.forward, Vector3.up);

        if (angle < 30f && speed < 30f)
        {
            // Vliegtuig wijst sterk omhoog en is langzaam
            speed -= 20f * Time.deltaTime;
        }

        if (angle < 15f && speed < 15f)
        {
            // Extreem langzaam + extreem omhoog
            speed -= 40f * Time.deltaTime;
        }

        speed = Mathf.Clamp(speed, 0f, maxSpeed);
    }
}
