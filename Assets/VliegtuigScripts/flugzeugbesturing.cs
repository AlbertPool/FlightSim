
using UnityEngine;

public class Flugzeugbesturing : MonoBehaviour
{
    public Rigidbody rb;

    public bool moterAan = false;

    public float acceleratie = 10f;
    public float maxSpeed = 50f;

    void Update()
    {
        // Motor aan/uit met één druk op Space
        if (Input.GetKeyDown(KeyCode.E))
        {
            moterAan = !moterAan;

            Debug.Log("Motor aan: " + moterAan);
        }
    }

    void FixedUpdate()
    {
        
        if (moterAan)
        {
            
            // Vliegtuig versnelt naar voren
            rb.AddForce(
                transform.right * acceleratie,
                ForceMode.Acceleration
            );

            // Maximum snelheid
            if (rb.linearVelocity.magnitude > maxSpeed)
            {
                rb.linearVelocity =
                    rb.linearVelocity.normalized * maxSpeed;
            }
        }
    }
}
