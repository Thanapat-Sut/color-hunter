using UnityEngine;

[DisallowMultipleComponent]
public class RotateZ : MonoBehaviour
{
    [Tooltip("Degrees per second on Z. Positive = counter-clockwise, negative = clockwise.")]
    public float speed = 60f;

    void Update()
    {
        transform.Rotate(0f, 0f, speed * Time.deltaTime);
    }
}
