using System;
using System.Collections;
using UnityEngine;
public class BlocksGenerated : MonoBehaviour
{
    object Sensor_right = 4f;
    System.Collections.Generic.Dictionary<int, bool> __digitalInputs = new System.Collections.Generic.Dictionary<int, bool>();
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
        if (digitalRead(Sensor_right))
        {
        }

    }
}
