using UnityEngine;

public class flugzeugbesturing : MonoBehaviour
{
    public Transform planeModel;

    public float flySpeed = 10f;
    public float yawSpeed = 60f;
    public float pitchSpeed = 40f;

   //visueel
   public float maxRoll = 30f;
    public float rollSmoothness = 5f;

    private float currentRoll;

    void Update()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        //richting veranderen
        transform.Rotate(
            -vertical * pitchSpeed * Time.deltaTime,
            horizontal * yawSpeed * Time.deltaTime,
            0f,
            Space.Self
        );

        // Flyspeed constant houden inplats van fuckass inputs
        transform.position += transform.forward * flySpeed * Time.deltaTime;

        //roll alleen de body
        float targetRoll = -horizontal * maxRoll;

        currentRoll = Mathf.Lerp(
            currentRoll,
            targetRoll,
            rollSmoothness * Time.deltaTime
        );

        planeModel.localRotation = Quaternion.Euler(0f, 0f, currentRoll);
               
    }
}
