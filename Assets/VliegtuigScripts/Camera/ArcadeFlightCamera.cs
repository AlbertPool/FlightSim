using UnityEngine;

public class ArcadeFlightCamera : MonoBehaviour
{
    public Transform cockpitAnchor;

    void LateUpdate()
    {
        if (!cockpitAnchor) return;

        transform.SetPositionAndRotation(
            cockpitAnchor.position, cockpitAnchor.rotation);
    }
}
