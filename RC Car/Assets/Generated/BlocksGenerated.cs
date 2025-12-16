using System;
using System.Collections;
using UnityEngine;
public class BlocksGenerated : MonoBehaviour
{
    RCCarSensor _car;
    int _lfPwm, _lbPwm, _rfPwm, _rbPwm;
    void Awake() => _car = GetComponent<RCCarSensor>();
    public float LeftMotor  { get; private set; }
    public float RightMotor { get; private set; }
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
    public void forward(object Speed)
    {
            analogWrite(pin_wheel_left_forward, Speed);
            analogWrite(pin_wheel_right_forward, Speed);
            analogWrite(pin_wheel_left_back, stop);
            analogWrite(pin_wheel_right_back, stop);
    }
    System.Collections.Generic.Dictionary<int, bool> __digitalInputs = new System.Collections.Generic.Dictionary<int, bool>();
    public void analogWrite(object pin, object value)
    {
        int  p = Convert.ToInt32(pin);
        int  PIN_LB = Convert.ToInt32(pin_wheel_left_back);
        int  PIN_LF = Convert.ToInt32(pin_wheel_left_forward);
        int  PIN_RF = Convert.ToInt32(pin_wheel_right_forward);
        int  PIN_RB = Convert.ToInt32(pin_wheel_right_back);
        int pwm = Mathf.Clamp(Convert.ToInt32(value), 0, 255);
        if (p == PIN_LB) { _lbPwm = pwm; if (pwm > 0) _lfPwm = 0; }
        else if (p == PIN_LF) { _lfPwm = pwm; if (pwm > 0) _lbPwm = 0; }
        else if (p == PIN_RF) { _rfPwm = pwm; if (pwm > 0) _rbPwm = 0; }
        else if (p == PIN_RB) { _rbPwm = pwm; if (pwm > 0) _rfPwm = 0; }
        else { return; }
        LeftMotor  = Mathf.Clamp((_lfPwm - _lbPwm) / 255f, -1f, 1f);
        RightMotor = Mathf.Clamp((_rfPwm - _rbPwm) / 255f, -1f, 1f);
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

    }

    public void Loop()
    {
        if (digitalRead(Sensor_right))
        {
            Right_turn(trun);
        }
        // if (digitalRead(Sensor_left))
        // {
        //     left_turn(trun);
        // }
        // if (digitalRead(Sensor_right))
        // {
        //     if (digitalRead(Sensor_left))
        //     {
        //         forward(go);
        //     }
        // }

    }
}
