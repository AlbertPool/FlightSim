using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ArcadeFlightController : MonoBehaviour
{
    [Header("Bullshit aerodynamics")]
    [Min(0)] public float engineAcceleration = 18f;
    [Min(0)] public float forwardDrag = 0.02f;
    [Min(0)] public float sidewaysDamping = 2f;
    [Min(0)] public float liftPower = 0.0242f;
    [Min(1)] public float stallSpeed = 18f;
    [Min(1)] public float stallAngle = 25f;
    [Min(0)] public float inducedDrag = 0.008f;
    [Range(0, 1)] public float startingThrottle = 0f;
    [Min(0)] public float throttleChangeSpeed = 0.4f;

    [Header("Handling")]
    public float pitchRate = 45f;
    public float maxBank = 40f;
    public float bankResponse = 3f;
    public float maxRollRate = 90f;
    public float maxTurnRate = 40f;
    public float groundTurnRate = 30f;
    public float angularAcceleration = 240f;
    public bool autoTrim = true;

    [Header("ground detect")]
    public Transform groundProbe;
    public float groundProbeDistance = 1f;
    public LayerMask groundLayers;

    public float Speed { get; private set; }
    public float AngleOfAttack { get; private set; }
    public float StallAmount { get; private set; }
    public bool Grounded { get; private set; }
    public float Throttle { get; private set; }

    Rigidbody body;
    float pitchInput, turnInput, throttleInput;
    bool brakeInput;

    Vector3 Velocity
    {
        get { return body.linearVelocity; }
    }



    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.maxAngularVelocity = 10f;
        Throttle = startingThrottle;
    }

    // met pitch boven 0 nose up, turn 0 = right, throttle boven 0 increase.
    public void SetInput(float pitch, float turn, float throttle, bool brake)
    {
        pitchInput = Mathf.Clamp(pitch, -1f, 1f);
        turnInput = Mathf.Clamp(turn, -1f, 1f);
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
        brakeInput = brake;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Quaternion rotation = body.rotation;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation* Vector3.right;
        Vector3 up= rotation * Vector3.up;
        Vector3 velocity = Velocity;
        Vector3 localVelocity = Quaternion.Inverse(rotation) * velocity;
        Speed = velocity.magnitude;
        float airspeed = Mathf.Max(0f, localVelocity.z);
        Vector3 probe = groundProbe ? groundProbe.position : body.position;
        Grounded = Physics.Raycast(probe, Vector3.down, groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore);
        Throttle = Mathf.Clamp01(Throttle + throttleInput * throttleChangeSpeed * dt);

        body.AddForce(forward * (Throttle * engineAcceleration), ForceMode.Acceleration);
        body.AddForce(-velocity * Speed * forwardDrag * (brakeInput ? 4f : 1f), ForceMode.Acceleration);
        body.AddForce(-right * localVelocity.x * sidewaysDamping, ForceMode.Acceleration);

        Vector3 wingVelocity = Vector3.ProjectOnPlane(velocity, right);
        float wingSpeedSquared = wingVelocity.sqrMagnitude;
        AngleOfAttack = airspeed > 0.1f ? Mathf.Atan2(-localVelocity.y, airspeed) * Mathf.Rad2Deg : 0f;
        float speedStall = 1f - Mathf.SmoothStep(0f, 1f, airspeed / stallSpeed);
        float angleStall = Mathf.InverseLerp(stallAngle, stallAngle + 20f, Mathf.Abs(AngleOfAttack));
        StallAmount = Mathf.Max(speedStall, angleStall);

        float coefficient = 0.45f;

        if (autoTrim && !Grounded && Mathf.Abs(pitchInput) < 0.05f)
        {
            float support = Mathf.Max(0.65f, Vector3.Dot(up, Vector3.up));
            coefficient = Mathf.Clamp(Physics.gravity.magnitude / Mathf.Max(0.01f, wingSpeedSquared * liftPower * support), 0f, 0.65f);
        }

        coefficient = Mathf.Clamp(coefficient + AngleOfAttack * 0.06f, -1.5f, 1.5f);
        coefficient *= Mathf.Lerp(1f, 0.2f, angleStall);
        float speedSupport = Mathf.SmoothStep(0f, 1f, airspeed / stallSpeed);
        Vector3 liftDirection = wingSpeedSquared > 0.01f ? Vector3.Cross(wingVelocity.normalized, right) : up;

        //fuck achteruit vliegen
        if (airspeed > 0.1f)
        {
            body.AddForce(liftDirection * (wingSpeedSquared * liftPower * coefficient * speedSupport), ForceMode.Acceleration);
            body.AddForce(-wingVelocity.normalized * (wingSpeedSquared * coefficient * coefficient * inducedDrag), ForceMode.Acceleration);
        }

        float authority = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, airspeed / stallSpeed));
        Vector3 levelUp = Vector3.ProjectOnPlane(Vector3.up, forward);
        float bank = levelUp.sqrMagnitude > 0.001f ? Vector3.SignedAngle(levelUp.normalized, up, forward) : 0f;
        float targetBank = Grounded ? 0f : -turnInput * maxBank;
        float rollRate = Mathf.Clamp(Mathf.DeltaAngle(bank, targetBank) * bankResponse, -maxRollRate, maxRollRate);

        float headingRate = Grounded ? turnInput * groundTurnRate : Mathf.Clamp(-Physics.gravity.magnitude * Mathf.Tan(Mathf.Clamp(bank, -70f, 70f) * Mathf.Deg2Rad) / Mathf.Max(airspeed, stallSpeed) * Mathf.Rad2Deg, -maxTurnRate, maxTurnRate) * authority;
        Vector3 targetAngular = right * (-pitchInput * pitchRate * authority) + forward * (rollRate * authority) + Vector3.up * headingRate;
        Vector3 error = targetAngular * Mathf.Deg2Rad - body.angularVelocity;
        Vector3 change = Vector3.ClampMagnitude(error, angularAcceleration * Mathf.Deg2Rad * dt);
        body.AddTorque(change, ForceMode.VelocityChange);

    }
}
