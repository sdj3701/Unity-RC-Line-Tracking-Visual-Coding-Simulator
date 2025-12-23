using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RCCarRuntimeAdapter : MonoBehaviour, IRuntimeIO
{
    [Header("Sensors")]
    public GameObject[] sensors; // 0=left,1=right
    public int leftPin = 3;
    public int rightPin = 4;
    public float rayDistance = 2f;
    [Range(0f,1f)] public float blackThreshold = 0.2f;
    public bool whiteMeansTrue = true;

    [Header("Motor Pins")]
    public int pinLeftF = 9;
    public int pinLeftB = 6;
    public int pinRightF = 10;
    public int pinRightB = 11;

    [Header("Motion")]
    public float maxLinearSpeed = 5f;
    public float maxAngularSpeed = 120f;
    public float wheelVisualSpeed = 360f;
    public Vector3 wheelRotateAxis = Vector3.up;
    public GameObject[] wheels;

    Rigidbody rb;
    float leftMotor, rightMotor;

    void Awake() { rb = GetComponent<Rigidbody>(); }

    void FixedUpdate()
    {
        ApplyWheelVisualRotation(leftMotor, rightMotor);
        Vector3 move = transform.forward * (leftMotor + rightMotor) * 0.5f * maxLinearSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
        float angular = (rightMotor - leftMotor) * maxAngularSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, angular, 0f));
    }

    // IRuntimeIO
    public bool DigitalRead(int pin)
    {
        if (pin == leftPin) return SampleSensor(0);
        if (pin == rightPin) return SampleSensor(1);
        return false;
    }

    public void AnalogWrite(int pin, float value)
    {
        float pwm = Mathf.Clamp01(value / 255f);
        if (pin == pinLeftF) { leftMotor = pwm; }
        else if (pin == pinLeftB) { leftMotor = -pwm; }
        else if (pin == pinRightF) { rightMotor = pwm; }
        else if (pin == pinRightB) { rightMotor = -pwm; }
    }

    public void MoveForward(float speed01)
    {
        float s = Mathf.Clamp01(speed01);
        leftMotor = rightMotor = s;
    }

    public void TurnLeft(float speed01)
    {
        float s = Mathf.Clamp01(speed01);
        leftMotor = -s; rightMotor = s;
    }

    public void TurnRight(float speed01)
    {
        float s = Mathf.Clamp01(speed01);
        leftMotor = s; rightMotor = -s;
    }

    public void Stop()
    {
        leftMotor = rightMotor = 0f;
    }

    bool SampleSensor(int index)
    {
        if (sensors == null || index >= sensors.Length || sensors[index] == null)
            return whiteMeansTrue;
        var sensor = sensors[index];
        Vector3 origin = sensor.transform.position;
        Vector3 dir = -sensor.transform.up;
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, rayDistance))
            return whiteMeansTrue;
        var rend = hit.collider ? hit.collider.GetComponent<Renderer>() : null;
        var mat = rend ? rend.sharedMaterial : null;
        var tex = mat ? mat.mainTexture as Texture2D : null;
        if (tex == null) return whiteMeansTrue;
        var uv = hit.textureCoord;
        uv = Vector2.Scale(uv, mat.mainTextureScale) + mat.mainTextureOffset;
        uv.x -= Mathf.Floor(uv.x); uv.y -= Mathf.Floor(uv.y);
        float gray = tex.GetPixelBilinear(uv.x, uv.y).grayscale;
        bool isBlack = gray <= blackThreshold;
        return whiteMeansTrue ? !isBlack : isBlack;
    }

    void ApplyWheelVisualRotation(float left, float right)
    {
        if (wheels == null || wheels.Length == 0) return;
        float dt = Time.fixedDeltaTime;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            if (!w) continue;
            float m = (i % 2 == 0) ? left : right;
            w.transform.Rotate(wheelRotateAxis, m * wheelVisualSpeed * dt, Space.Self);
        }
    }
}
