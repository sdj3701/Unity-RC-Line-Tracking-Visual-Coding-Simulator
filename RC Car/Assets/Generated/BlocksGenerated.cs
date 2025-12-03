using System;
using System.Collections;
using UnityEngine;
public class BlocksGenerated : MonoBehaviour
{
    object stop = 0f;
    object right_back = 6f;
    
    public void rc(object speed)
    {
            analogWrite(right_back, stop);
    }
    public void analogWrite(object pin, object value)
    {
        int p = System.Convert.ToInt32(pin);
        int v = System.Convert.ToInt32(value);
        v = Mathf.Clamp(v, 0, 255);
    }
    public void Run()
    {
        

    }
}
