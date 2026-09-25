using UnityEngine;

public class Flugzeugbesturing : MonoBehaviour
{
    [Header("models")]
    public Transform planeModel;

    [Header("Joystick")]
    public string verticalAxis = "Vertical";

    [Range(0f, 0.5f)]
    public float joystickDeadzone = 0.1f;

    public bool invertVertical = false;

    [Header("Snelheid")]
    public float startSpeed = 0f;
    public float cruiseSpeed = 20f;
    public float acceleration = 4f;
    public float takeoffSpeed = 12f;

    [Header("dturn")]
    public float yawSpeed = 45f;
    public float buttonSmoothness = 4f;

    [Header("pitchs")]
    public float pitchSpeed = 35f;
    public float maximumPitch = 35f;
    public float pitchLevelSpeed = 20f;

    [Header("Visualbs")]
    public float maxRoll = 35f;
    public float rollSmoothness = 4f;

    [Header("grondcheck")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 1.5f;

    public float CurrentSpeed { get; private set; }
    public bool IsGrounded { get; private set; }

    private float currentYaw;
    private float currentPitch;
    private float currentRoll;
    private float currentTurnInput;

    void Start()
    {
        CurrentSpeed = startSpeed;

        currentYaw = transform.eulerAngles.y;
        currentPitch = 0f;
    }

    void Update()
    {
        CheckGround();

        float verticalInput = Input.GetAxis(verticalAxis);
        verticalInput = ApplyDeadzone(verticalInput);

        if (invertVertical)
        {
            verticalInput *= -1f;
        }

        float targetTurnInput = ReadTurnButtons();

        currentTurnInput = Mathf.MoveTowards(
            currentTurnInput,
            targetTurnInput,
            buttonSmoothness * Time.deltaTime
        );

        UpdateSpeed();
        UpdateRotation(currentTurnInput, verticalInput);
        UpdateVisualRoll(currentTurnInput);
        MoveForward();
    }

    float ReadTurnButtons()
    {
        bool turnLeft =
            Input.GetKey(KeyCode.JoystickButton2); // Xbox X

        bool turnRight =
            Input.GetKey(KeyCode.JoystickButton1); // Xbox B

        if (turnLeft && !turnRight)
        {
            return -1f;
        }

        if (turnRight && !turnLeft)
        {
            return 1f;
        }

        return 0f;
    }

    void CheckGround()
    {
        IsGrounded = Physics.Raycast(
            transform.position,
            Vector3.down,
            groundCheckDistance,
            groundLayer
        );

        Debug.DrawRay(
            transform.position,
            Vector3.down * groundCheckDistance,
            IsGrounded ? Color.green : Color.red
        );
    }

    void UpdateSpeed()
    {
        CurrentSpeed = Mathf.MoveTowards(
            CurrentSpeed,
            cruiseSpeed,
            acceleration * Time.deltaTime
        );
    }

    void UpdateRotation(float turnInput, float verticalInput)
    {
        
        currentYaw +=
            turnInput * yawSpeed * Time.deltaTime;

        bool canPitch =
            !IsGrounded || CurrentSpeed >= takeoffSpeed;

        if (canPitch && Mathf.Abs(verticalInput) > 0f)
        {
            
            currentPitch +=
                -verticalInput * pitchSpeed * Time.deltaTime;

            currentPitch = Mathf.Clamp(
                currentPitch,
                -maximumPitch,
                maximumPitch
            );
        }
        else
        {
           
            currentPitch = Mathf.MoveTowards(
                currentPitch,
                0f,
                pitchLevelSpeed * Time.deltaTime
            );
        }

        transform.rotation = Quaternion.Euler(
            currentPitch,
            currentYaw,
            0f
        );
    }

    void UpdateVisualRoll(float turnInput)
    {
        if (planeModel == null)
        {
            return;
        }

        float targetRoll = -turnInput * maxRoll;

        currentRoll = Mathf.Lerp(
            currentRoll,
            targetRoll,
            rollSmoothness * Time.deltaTime
        );

        planeModel.localRotation = Quaternion.Euler(
            0f,
            0f,
            currentRoll
        );
    }

    void MoveForward()
    {
        transform.position +=
            transform.forward * CurrentSpeed * Time.deltaTime;
    }

    float ApplyDeadzone(float input)
    {
        if (Mathf.Abs(input) < joystickDeadzone)
        {
            return 0f;
        }

        return input;
    }
}