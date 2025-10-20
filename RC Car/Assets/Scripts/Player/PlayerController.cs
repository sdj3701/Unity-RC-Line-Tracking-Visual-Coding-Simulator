using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance;
    private void Awake() => Instance = this;

    public void Move(float distance)
    {
        transform.Translate(Vector3.forward * distance);
    }

    public void Rotate(float angle)
    {
        transform.Rotate(Vector3.up * angle);
    }
}
