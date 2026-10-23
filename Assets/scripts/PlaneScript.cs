using UnityEngine;

public class PlaneScript : MonoBehaviour
{
    [SerializeField] private float forwardSpeed = 40f;
    [SerializeField] private float forwardAcceleration = 5f;
    [Tooltip("Acceleration while building vertical speed, in units per second squared.")]
    [SerializeField] private float verticalSpeed = 20f;
    [SerializeField] private float maxVerticalSpeed = 40f;
    [SerializeField] private float bankedClimbSideSpeed = 20f;
    [SerializeField] private float turnSpeed = 40f;
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

    private float currentBankAngle;
    private float currentPitchAngle;
    private float currentForwardSpeed;
    private float currentVerticalSpeed;
    private float currentFrontWingAngle;
    private bool engineStarted;
    private Quaternion frontWingRestRotation;

    void Start()
    {
        currentForwardSpeed = 0f;
    }

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
        if (Input.GetKeyDown(KeyCode.T))
        {
            engineStarted = true;
        }

        currentForwardSpeed = Mathf.MoveTowards(
            currentForwardSpeed,
            engineStarted ? forwardSpeed : 0f,
            forwardAcceleration * Time.deltaTime);

        float verticalInput = engineStarted ? Input.GetAxis("Vertical") : 0f;
        float turnInput = engineStarted ? Input.GetAxisRaw("Horizontal") : 0f;
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

        currentBankAngle = Mathf.MoveTowards(currentBankAngle, targetBankAngle, currentBankSpeed * Time.deltaTime);
        currentPitchAngle = Mathf.MoveTowards(currentPitchAngle, targetPitchAngle, currentPitchSpeed * Time.deltaTime);
        currentVerticalSpeed = Mathf.MoveTowards(
            currentVerticalSpeed,
            verticalInput * maxVerticalSpeed,
            verticalSpeed * Time.deltaTime);
        float targetFrontWingAngle = maxPitchAngle > 0f
            ? Mathf.Clamp(currentPitchAngle / maxPitchAngle, -1f, 1f) * frontWingMaxDeflection
            : 0f;
        currentFrontWingAngle = Mathf.MoveTowards(
            currentFrontWingAngle,
            targetFrontWingAngle,
            frontWingResponseSpeed * Time.deltaTime);
        float turnAmount = maxBankAngle > 0f
            ? Mathf.Clamp(-currentBankAngle / maxBankAngle, -1f, 1f)
            : 0f;
        float bankAmount = maxBankAngle > 0f
            ? Mathf.Clamp01(Mathf.Abs(currentBankAngle) / maxBankAngle)
            : 0f;
        float bankedClimbInput = Mathf.Max(0f, verticalInput) * bankAmount;
        Vector3 bankDirection = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized
            * -Mathf.Sign(currentBankAngle);

        transform.position += transform.forward * currentForwardSpeed * Time.deltaTime;
        transform.position += Vector3.up * currentVerticalSpeed * Time.deltaTime;
        transform.position += bankDirection * (bankedClimbInput * bankedClimbSideSpeed * Time.deltaTime);
        transform.Rotate(Vector3.up, turnAmount * turnSpeed * Time.deltaTime, Space.World);
        transform.Rotate(Vector3.forward, currentBankAngle - previousBankAngle, Space.Self);
        transform.Rotate(Vector3.right, currentPitchAngle - previousPitchAngle, Space.Self);

        if (frontWing != null)
        {
            frontWing.localRotation = frontWingRestRotation * Quaternion.AngleAxis(currentFrontWingAngle, Vector3.right);
        }
    }
}
