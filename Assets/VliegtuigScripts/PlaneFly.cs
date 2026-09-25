using UnityEngine;

public class PlaneFly
{


    void CalculateState(float dt, bool firstThisFrame)
    {
        var invRotation = Quaternion.Inverse(Rigidbody.rotation);
        Velocity = Rigidbody.velocity;
        localVelocity = invRotation * Velocity;

        localAngularVelocity = invRotation * Rigidbody.angularVelocity;
    }

    void UpdateThrust()
    {
        Rigidbody.AddRelativeForce(Throttle * maxThrust * Vector3.forward);
    }

    void UpdateDrag()
    {
        var lv = LocalVelocity;
        var lv2 = lv.sqrMagnitude;

        //drag berekening coefficient
        var coefficient = Utilities.Scale6(
            lv.normalized,
            dragRight, dragLeft,
            dragTop, drawBottom,
            dragForward, dragBack
           );

        var drag = coefficient.magnitude * lv2 * -lv.normalized; //velocity - drag
        Rigidbody.addRelativeForce(drag);
    }

    float CalculateSteering(float dt, float angularVelocity, float targetVelocity, float acceleration)
    {
        var error = targetVelocity - angularVelocity;
        var accel = acceleration * dt;
        return Mathf.Clamp(error, -accel, accel);

        //torque voor 1 axis
    }

    void UpdateSteering(float dt)
    {
        var speed = Mathf.Max(0, LocalVelocity.z);
        var steeringPower = steeringCurve.Evaluate(speed);

        var targetAV = Vector3.Scale(controlInput, turnSpeed * steeringPower);
        var av = LocalAngularVelocity * Mathf.Rad2Deg;

        var correction = new Vector3(
            CalculateSteering(dt, av.x, targetAV.x, turnAcceleration.x * steeringPower),
            CalculateSteering(dt, av.y, targetAV.y, turnAcceleration.y * steeringPower),
            CalculateSteering(dt, av.z, targetAV.z, turnAcceleration.z * steeringPower)
        );

        Rigidbody.AddRelativeTorque(correction * Mathf.Deg2Rad, ForceMode.VelocityChange); //zodat rigidbody mass niet boeit

    }


}
