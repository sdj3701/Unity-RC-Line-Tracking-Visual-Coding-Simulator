using System;
using System.Collections;
using UnityEngine;
public class BlocksGenerated : MonoBehaviour
{
    object Sensor_right = 4f;
    object Sensor_left = 3f;
    object go = 200f;
    object stop = 0f;
    object trun = 150f;
    object pin_wheel_right_forward = 10f;
    object pin_wheel_right_back = 11f;
    object pin_wheel_left_back = 6f;
    object pin_wheel_left_forward = 9f;
    public void Right_turn(object Speed)
    {
            analogWrite(pin_wheel_right_back, stop);
            analogWrite(pin_wheel_left_back, stop);
            analogWrite(pin_wheel_right_forward, Speed);
            analogWrite(pin_wheel_left_forward, stop);
    }
    public void left_turn(object Speed)
    {
            analogWrite(pin_wheel_left_forward, Speed);
            analogWrite(pin_wheel_left_back, stop);
            analogWrite(pin_wheel_right_back, stop);
            analogWrite(pin_wheel_right_forward, stop);
    }
    System.Collections.Generic.Dictionary<int, bool> __digitalInputs = new System.Collections.Generic.Dictionary<int, bool>();
    public void analogWrite(object pin, object value)
    {
        object p = pin;
        object v = value;
        v = Mathf.Clamp(Convert.ToInt32(v), 0, 255);
    }
    public bool digitalRead(object pin)
    {
        int p = (pin is int ip) ? ip : Convert.ToInt32(pin);
        bool v;
        if (__digitalInputs != null && __digitalInputs.TryGetValue(p, out v)) return v;
        return false;
    }
    public void Start()
    {
        __digitalInputs[Convert.ToInt32(Sensor_right)] = true;
        __digitalInputs[Convert.ToInt32(Sensor_left)] = true;
        if (digitalRead(Sensor_right))
        {
            Right_turn(trun);
        }
        if (digitalRead(Sensor_left))
        {
            left_turn(trun);
        }
        if (digitalRead(Sensor_right))
        {
            if (digitalRead(Sensor_left))
            {
                left_turn(trun);
            }
        }

    }
}
