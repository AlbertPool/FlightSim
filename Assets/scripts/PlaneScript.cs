using UnityEngine;

public class PlaneScript : MonoBehaviour
{
    [SerializeField] private float forwardSpeed = 40f;
    [Tooltip("Acceleration while building vertical speed, in units per second squared.")]
    [SerializeField] private float verticalSpeed = 20f;
    [SerializeField] private float maxVerticalSpeed = 40f;
    [SerializeField] private float bankedClimbSideSpeed = 20f;
    [SerializeField] private float bankedClimbUpwardBonus = -30f;
    [SerializeField, Min(0f)] private float maxBankedClimbUpwardSpeed = 2f;
    [SerializeField] private float turnSpeed = 30f;
    [SerializeField] private float maxBankAngle = 30f;
    [SerializeField] private float bankSpeed = 15f;
    [SerializeField] private float oppositeTurnLevelingMultiplier = 1.5f;
    [SerializeField] private float maxPitchAngle = 20f;
    [SerializeField] private float pitchSpeed = 15f;
    [SerializeField] private float oppositePitchLevelingMultiplier = 1.5f;
    [SerializeField] private Transform frontWing;
    [Tooltip("Maximum front-wing pitch deflection in degrees. Use a negative value to reverse its direction.")]
    [SerializeField] private float frontWingMaxDeflection = 8f;
    [SerializeField] private float frontWingResponseSpeed = 45f;

    [Header("ESP32 Lever")]
    [SerializeField] private ESP32reader espReader;
    [SerializeField] private float leverMin = 4f;
    [SerializeField] private float leverNeutral = 6.5f;
    [SerializeField] private float leverMax = 8.5f;
    [SerializeField] private float leverDeadzone = 0.4f;

    [Range(0f, 1f)]
    [SerializeField] private float maxLeverInput = 0.35f;

    [SerializeField] private bool invertLever = false;

    private float currentBankAngle;
    private float currentPitchAngle;
    private float currentVerticalSpeed;
    private float currentFrontWingAngle;
    private Quaternion frontWingRestRotation;

    void Awake()
    {
        if (frontWing == null)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Cube.035")
                {
                    frontWing = child;
                    break;
                }
            }
        }

        if (frontWing != null)
        {
            frontWingRestRotation = frontWing.localRotation;
        }
        else
        {
            Debug.LogWarning("PlaneScript could not find the front wing named Cube.035.", this);
        }
    }

    void Update()
    {
        float espInput = 0f;

        if (espReader != null)
        {
            float value = espReader.usbValue;

            // Deadzone rond 6.5
            if (Mathf.Abs(value - leverNeutral) <= leverDeadzone)
            {
                espInput = 0f;
            }
            else if (value < leverNeutral)
            {
                float amount = Mathf.InverseLerp(
                    leverNeutral - leverDeadzone,
                    leverMin,
                    value
                );

                espInput = -amount * maxLeverInput;
            }
            else
            {
                float amount = Mathf.InverseLerp(
                    leverNeutral + leverDeadzone,
                    leverMax,
                    value
                );

                espInput = amount * maxLeverInput;
            }

            if (invertLever)
            {
                espInput = -espInput;
            }
        }

        // Oude W/S besturing
        float keyboardPitch =
            (Input.GetKey(KeyCode.S) ? 1f : 0f) -
            (Input.GetKey(KeyCode.W) ? 1f : 0f);

        float verticalInput;

        // Als W of S wordt ingedrukt: keyboard heeft voorrang.
        // Anders gebruiken we de ESP-hendel.
        if (Mathf.Abs(keyboardPitch) > 0.01f)
        {
            verticalInput = keyboardPitch;
        }
        else
        {
            verticalInput = espInput;
        }

        // A/D blijft hetzelfde
        float turnInput =
            (Input.GetKey(KeyCode.D) ? 1f : 0f) -
            (Input.GetKey(KeyCode.A) ? 1f : 0f);

        float targetPitchAngle = -verticalInput * maxPitchAngle;
        float targetBankAngle = -turnInput * maxBankAngle;

        bool pitchingToOppositeSide =
            !Mathf.Approximately(currentPitchAngle, 0f) &&
            !Mathf.Approximately(targetPitchAngle, 0f) &&
            Mathf.Sign(currentPitchAngle) != Mathf.Sign(targetPitchAngle);

        if (pitchingToOppositeSide)
        {
            targetPitchAngle = 0f;
        }

        bool turningToOppositeSide =
            !Mathf.Approximately(currentBankAngle, 0f) &&
            !Mathf.Approximately(targetBankAngle, 0f) &&
            Mathf.Sign(currentBankAngle) != Mathf.Sign(targetBankAngle);

        if (turningToOppositeSide)
        {
            targetBankAngle = 0f;
        }

        float previousBankAngle = currentBankAngle;
        float previousPitchAngle = currentPitchAngle;
        float currentBankSpeed = bankSpeed;
        float currentPitchSpeed = pitchSpeed;

        if (turningToOppositeSide)
        {
            currentBankSpeed *= oppositeTurnLevelingMultiplier;
        }

        if (pitchingToOppositeSide)
        {
            currentPitchSpeed *= oppositePitchLevelingMultiplier;
        }

        currentBankAngle = Mathf.MoveTowards(
            currentBankAngle,
            targetBankAngle,
            currentBankSpeed * Time.deltaTime
        );

        currentPitchAngle = Mathf.MoveTowards(
            currentPitchAngle,
            targetPitchAngle,
            currentPitchSpeed * Time.deltaTime
        );

        float targetFrontWingAngle = maxPitchAngle > 0f
            ? Mathf.Clamp(currentPitchAngle / maxPitchAngle, -1f, 1f) * frontWingMaxDeflection
            : 0f;

        currentFrontWingAngle = Mathf.MoveTowards(
            currentFrontWingAngle,
            targetFrontWingAngle,
            frontWingResponseSpeed * Time.deltaTime
        );

        float turnAmount = maxBankAngle > 0f
            ? Mathf.Clamp(-currentBankAngle / maxBankAngle, -1f, 1f)
            : 0f;

        float bankAmount = maxBankAngle > 0f
            ? Mathf.Clamp01(Mathf.Abs(currentBankAngle) / maxBankAngle)
            : 0f;

        float bankedClimbInput = Mathf.Max(0f, verticalInput) * bankAmount;

        Vector3 forwardMovement = transform.forward * forwardSpeed;
        float forwardVerticalSpeed = forwardMovement.y;

        float bankedClimbVerticalBonus =
            bankedClimbInput * bankedClimbUpwardBonus;

        float maxUnbankedClimbSpeed =
            verticalInput * maxVerticalSpeed +
            Mathf.Max(0f, forwardVerticalSpeed);

        float maxClimbSpeed = Mathf.Lerp(
            maxUnbankedClimbSpeed,
            maxBankedClimbUpwardSpeed,
            bankAmount
        );

        float targetVerticalSpeed =
            verticalInput * maxVerticalSpeed;

        if (bankedClimbInput > 0f)
        {
            targetVerticalSpeed = Mathf.Min(
                targetVerticalSpeed,
                maxClimbSpeed -
                forwardVerticalSpeed -
                bankedClimbVerticalBonus
            );
        }

        currentVerticalSpeed = Mathf.MoveTowards(
            currentVerticalSpeed,
            targetVerticalSpeed,
            verticalSpeed * Time.deltaTime
        );

        float totalVerticalSpeed =
            forwardVerticalSpeed +
            currentVerticalSpeed +
            bankedClimbVerticalBonus;

        if (bankedClimbInput > 0f)
        {
            totalVerticalSpeed =
                Mathf.Min(totalVerticalSpeed, maxClimbSpeed);
        }

        forwardMovement.y = 0f;

        Vector3 bankDirection =
            Vector3.ProjectOnPlane(
                transform.right,
                Vector3.up
            ).normalized
            * -Mathf.Sign(currentBankAngle);

        transform.position +=
            forwardMovement * Time.deltaTime;

        transform.position +=
            Vector3.up *
            totalVerticalSpeed *
            Time.deltaTime;

        transform.position +=
            bankDirection *
            (bankedClimbInput *
             bankedClimbSideSpeed *
             Time.deltaTime);

        transform.Rotate(
            Vector3.up,
            turnAmount * turnSpeed * Time.deltaTime,
            Space.World
        );

        transform.Rotate(
            Vector3.forward,
            currentBankAngle - previousBankAngle,
            Space.Self
        );

        transform.Rotate(
            Vector3.right,
            currentPitchAngle - previousPitchAngle,
            Space.Self
        );

        if (frontWing != null)
        {
            frontWing.localRotation =
                frontWingRestRotation *
                Quaternion.AngleAxis(
                    currentFrontWingAngle,
                    Vector3.right
                );
        }
    }
}