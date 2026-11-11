using UnityEngine;
using System.Globalization;

[RequireComponent(typeof(Rigidbody))]
public class PlaneScript : MonoBehaviour
{
    [Header("Vliegtuig")]
    [SerializeField] private float forwardSpeed = 40f;
    [SerializeField] private float forwardAcceleration = 5f;

    [SerializeField] private float verticalSpeed = 20f;
    [SerializeField] private float maxVerticalSpeed = 40f;
    [SerializeField] private float maxHeightMeters = 70f;

    [SerializeField] private float bankedClimbSideSpeed = 20f;
    [SerializeField] private float turnSpeed = 40f;
    [SerializeField] private float maxBankAngle = 30f;
    [SerializeField] private float bankSpeed = 15f;
    [SerializeField] private float oppositeTurnLevelingMultiplier = 1.5f;

    [SerializeField] private float maxPitchAngle = 20f;
    [SerializeField] private float pitchSpeed = 15f;
    [SerializeField] private float oppositePitchLevelingMultiplier = 1.5f;

    [SerializeField] private Transform frontWing;
    [SerializeField] private float frontWingMaxDeflection = 8f;
    [SerializeField] private float frontWingResponseSpeed = 45f;

    [Header("ESP32 Besturing")]
    [SerializeField] private ESP32reader esp32Reader;

    [Header("Hoogte sensor (sensor 1)")]
    [SerializeField] private float sensorMinCm = 3f;
    [SerializeField] private float sensorMaxCm = 6f;

    [Header("Roll sensor (sensor 2)")]
    [SerializeField] private float rollSensorMinCm = 3f;
    [SerializeField] private float rollSensorMaxCm = 6f;

    [Tooltip("Kleine marge tegen sensorruis.")]
    [SerializeField] private float sensorMarginCm = 0.05f;

    [Header("Landing")]
    [SerializeField] private float groundCheckDistance = 4f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float landingHoldTime = 1f;
    [SerializeField] private float landingBrakeDeceleration = 15f;
    [SerializeField] private float landingSinkSpeed = 3f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [Tooltip("Hoe vaak (in seconden) de status in de Console komt.")]
    [SerializeField] private float debugInterval = 0.5f;

    private float currentBankAngle;
    private float currentPitchAngle;
    private float currentForwardSpeed;
    private float currentVerticalSpeed;
    private float currentFrontWingAngle;

    private bool engineStarted;
    private bool isLanding;
    private float timeNearGround;

    private Quaternion frontWingRestRotation;
    private Rigidbody rb;
    private HeightMeter heightMeter;
    private float currentYaw;

    private readonly RaycastHit[] groundHits = new RaycastHit[8];

    // Debug
    private float nextDebugTime;
    private bool prevEspActive;
    private float dbgVertNorm;
    private float dbgRollNorm;

    private bool AllRingsHit => RingManager.Instance != null && RingManager.Instance.AllRingsHit;
    public bool CanLand => engineStarted && AllRingsHit && timeNearGround >= landingHoldTime;
    public bool IsLanding => isLanding;

    // Wordt aangezet door VerticalOnlyLock (alleen in level 1)
    public bool HorizontalLocked { get; set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        heightMeter = GetComponent<HeightMeter>();

        if (heightMeter == null)
            heightMeter = gameObject.AddComponent<HeightMeter>();

        currentYaw = transform.eulerAngles.y;

        rb.isKinematic = false;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Reader automatisch zoeken als hij niet in de Inspector is gesleept
        if (esp32Reader == null)
        {
            esp32Reader = FindFirstObjectByType<ESP32reader>();

            if (esp32Reader != null)
                Debug.Log("[Plane] esp32Reader stond leeg, automatisch gevonden op '" + esp32Reader.name + "'.", this);
            else
                Debug.LogError("[Plane] Geen ESP32reader gevonden in de scene! Sleep hem in het veld 'Esp32 Reader' of voeg het script aan een GameObject toe.", this);
        }

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
            Debug.LogWarning("[Plane] Front wing Cube.035 niet gevonden.", this);
        }
    }

    void Start()
    {
        currentForwardSpeed = 0f;
        Debug.Log("[Plane] Klaar. Druk op T om de motor te starten.", this);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (!engineStarted && !isLanding)
            {
                engineStarted = true;
                Debug.Log("[Plane] Motor GESTART.", this);
            }
            else if (CanLand)
            {
                engineStarted = false;
                isLanding = true;
                Debug.Log("[Plane] Landing gestart.", this);
            }
            else
            {
                Debug.Log("[Plane] T ingedrukt, maar landen kan nog niet. Tijd bij de grond: "
                    + timeNearGround.ToString("F2", CultureInfo.InvariantCulture)
                    + " / " + landingHoldTime.ToString("F2", CultureInfo.InvariantCulture) + " s", this);
            }
        }
    }

    void FixedUpdate()
    {
        heightMeter?.RefreshHeight();

        // Ground check
        if (engineStarted && AllRingsHit && IsNearGround())
        {
            timeNearGround += Time.fixedDeltaTime;
        }
        else
        {
            timeNearGround = 0f;
        }

        // Forward movement
        float forwardChangeRate = isLanding
            ? landingBrakeDeceleration
            : forwardAcceleration;

        currentForwardSpeed = Mathf.MoveTowards(
            currentForwardSpeed,
            engineStarted ? forwardSpeed : 0f,
            forwardChangeRate * Time.fixedDeltaTime
        );

        bool espActive = engineStarted &&
                         esp32Reader != null &&
                         esp32Reader.HasReceivedValue;

        if (espActive != prevEspActive)
        {
            prevEspActive = espActive;
            Debug.Log(espActive
                ? "[Plane] ESP32-besturing is nu ACTIEF (hoogte + roll)."
                : "[Plane] ESP32-besturing is nu UIT, toetsenbord actief.", this);
        }

        // Keyboard vertical control
        float verticalInput = engineStarted
            ? Input.GetAxis("Vertical")
            : 0f;

        // ESP32 vertical control (sensor 1)
        if (espActive)
        {
            dbgVertNorm = Mathf.InverseLerp(
                sensorMinCm + sensorMarginCm,
                sensorMaxCm - sensorMarginCm,
                esp32Reader.usbValue
            );

            verticalInput = Mathf.Lerp(-1f, 1f, dbgVertNorm);
        }

        // Keyboard horizontal control
        float turnInput = engineStarted
            ? Input.GetAxisRaw("Horizontal")
            : 0f;

        // ESP32 roll control (sensor 2)
        // Draait de roll de verkeerde kant op? Wissel -1f en 1f hieronder om.
        if (espActive)
        {
            dbgRollNorm = Mathf.InverseLerp(
                rollSensorMinCm + sensorMarginCm,
                rollSensorMaxCm - sensorMarginCm,
                esp32Reader.usbValue2
            );

            turnInput = Mathf.Lerp(-1f, 1f, dbgRollNorm);
        }

        // Links/rechts geblokkeerd (level 1)
        if (HorizontalLocked)
            turnInput = 0f;

        float targetPitchAngle = -verticalInput * maxPitchAngle;
        float targetBankAngle = -turnInput * maxBankAngle;

        // Pitch leveling
        bool pitchingToOppositeSide =
            !Mathf.Approximately(currentPitchAngle, 0f) &&
            !Mathf.Approximately(targetPitchAngle, 0f) &&
            Mathf.Sign(currentPitchAngle) != Mathf.Sign(targetPitchAngle);

        if (pitchingToOppositeSide)
            targetPitchAngle = 0f;

        // Bank leveling
        bool turningToOppositeSide =
            !Mathf.Approximately(currentBankAngle, 0f) &&
            !Mathf.Approximately(targetBankAngle, 0f) &&
            Mathf.Sign(currentBankAngle) != Mathf.Sign(targetBankAngle);

        if (turningToOppositeSide)
            targetBankAngle = 0f;

        float currentBankSpeed = bankSpeed;
        float currentPitchSpeed = pitchSpeed;

        if (turningToOppositeSide)
            currentBankSpeed *= oppositeTurnLevelingMultiplier;

        if (pitchingToOppositeSide)
            currentPitchSpeed *= oppositePitchLevelingMultiplier;

        currentBankAngle = Mathf.MoveTowards(
            currentBankAngle,
            targetBankAngle,
            currentBankSpeed * Time.fixedDeltaTime
        );

        currentPitchAngle = Mathf.MoveTowards(
            currentPitchAngle,
            targetPitchAngle,
            currentPitchSpeed * Time.fixedDeltaTime
        );

        // Vertical movement
        float targetVerticalSpeed = isLanding
            ? -landingSinkSpeed
            : verticalInput * maxVerticalSpeed;

        currentVerticalSpeed = Mathf.MoveTowards(
            currentVerticalSpeed,
            targetVerticalSpeed,
            verticalSpeed * Time.fixedDeltaTime
        );

        // Front wing
        float targetFrontWingAngle = maxPitchAngle > 0f
            ? Mathf.Clamp(currentPitchAngle / maxPitchAngle, -1f, 1f)
                * frontWingMaxDeflection
            : 0f;

        currentFrontWingAngle = Mathf.MoveTowards(
            currentFrontWingAngle,
            targetFrontWingAngle,
            frontWingResponseSpeed * Time.fixedDeltaTime
        );

        // Banking
        float turnAmount = maxBankAngle > 0f
            ? Mathf.Clamp(-currentBankAngle / maxBankAngle, -1f, 1f)
            : 0f;

        float bankAmount = maxBankAngle > 0f
            ? Mathf.Clamp01(Mathf.Abs(currentBankAngle) / maxBankAngle)
            : 0f;

        float bankedClimbInput = Mathf.Max(0f, verticalInput) * bankAmount;

        Vector3 bankDirection =
            Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized
            * -Mathf.Sign(currentBankAngle);

        // Velocity
        Vector3 velocity =
            transform.forward * currentForwardSpeed +
            Vector3.up * currentVerticalSpeed +
            bankDirection * (bankedClimbInput * bankedClimbSideSpeed);

        if (heightMeter != null)
        {
            float remainingHeight = maxHeightMeters - heightMeter.HeightInMeters;
            float maxUpwardSpeed = Mathf.Max(0f, remainingHeight / Time.fixedDeltaTime);
            velocity.y = Mathf.Min(velocity.y, maxUpwardSpeed);
        }

        // Landing
        if (isLanding && Mathf.Approximately(currentForwardSpeed, 0f))
        {
            isLanding = false;
            currentVerticalSpeed = 0f;
            velocity = Vector3.zero;
            Debug.Log("[Plane] Geland en gestopt.", this);
            RingManager.Instance?.PlaneLanded();
        }

        rb.linearVelocity = velocity;

        // Rotation
        currentYaw += turnAmount * turnSpeed * Time.fixedDeltaTime;

        Quaternion rotation =
            Quaternion.AngleAxis(currentYaw, Vector3.up) *
            Quaternion.AngleAxis(currentBankAngle, Vector3.forward) *
            Quaternion.AngleAxis(currentPitchAngle, Vector3.right);

        rb.MoveRotation(rotation);

        rb.angularVelocity = Vector3.zero;

        if (frontWing != null)
        {
            frontWing.localRotation =
                frontWingRestRotation *
                Quaternion.AngleAxis(currentFrontWingAngle, Vector3.right);
        }

        if (debugLogs && Time.time >= nextDebugTime)
        {
            nextDebugTime = Time.time + debugInterval;
            LogStatus(espActive, verticalInput, turnInput);
        }
    }

    void LogStatus(bool espActive, float verticalInput, float turnInput)
    {
        string reason = "";

        if (!engineStarted) reason = " (motor staat uit, druk T)";
        else if (esp32Reader == null) reason = " (esp32Reader is LEEG)";
        else if (!esp32Reader.HasReceivedValue) reason = " (nog geen geldige ESP32-data)";

        string msg = "[Plane] motor=" + engineStarted
            + " | ESP actief=" + espActive + reason
            + " | speed=" + currentForwardSpeed.ToString("F1", CultureInfo.InvariantCulture);

        if (espActive)
        {
            msg += " | hoogte sensor=" + esp32Reader.usbValue.ToString("F2", CultureInfo.InvariantCulture)
                + " cm -> norm=" + dbgVertNorm.ToString("F2", CultureInfo.InvariantCulture)
                + " -> vertical=" + verticalInput.ToString("F2", CultureInfo.InvariantCulture)
                + " | roll sensor=" + esp32Reader.usbValue2.ToString("F2", CultureInfo.InvariantCulture)
                + " cm -> norm=" + dbgRollNorm.ToString("F2", CultureInfo.InvariantCulture)
                + " -> turn=" + turnInput.ToString("F2", CultureInfo.InvariantCulture);
        }
        else
        {
            msg += " | toetsenbord vertical=" + verticalInput.ToString("F2", CultureInfo.InvariantCulture)
                + " turn=" + turnInput.ToString("F2", CultureInfo.InvariantCulture);
        }

        msg += " | pitch=" + currentPitchAngle.ToString("F1", CultureInfo.InvariantCulture)
            + " bank=" + currentBankAngle.ToString("F1", CultureInfo.InvariantCulture);

        Debug.Log(msg, this);
    }

    private bool IsNearGround()
    {
        int hitCount = Physics.RaycastNonAlloc(
            transform.position,
            Vector3.down,
            groundHits,
            groundCheckDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            if (groundHits[i].collider.attachedRigidbody != rb)
                return true;
        }

        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Application.isPlaying && CanLand
            ? Color.green
            : Color.yellow;

        Gizmos.DrawLine(
            transform.position,
            transform.position + Vector3.down * groundCheckDistance
        );
    }
}
