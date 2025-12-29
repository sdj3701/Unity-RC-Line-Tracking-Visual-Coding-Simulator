using System;
using System.Collections;
using System.IO;
using UnityEngine;

public interface IRuntimeIO
{
    bool DigitalRead(int pin);
    void AnalogWrite(int pin, float value);
    void MoveForward(float speed01);
    void TurnLeft(float speed01);
    void TurnRight(float speed01);
    void Stop();
}

[Serializable]
public class RuntimeBlockProgram
{
    public RuntimeBlockNode[] roots;
}

[Serializable]
public class RuntimeBlockNode
{
    public string type;                // "forward","turnLeft","turnRight","repeat","forever","if","ifElse","wait","digitalRead","analogWrite","stop"
    public float number;               // speed or repeat count or wait seconds
    public int pin;                    // for digitalRead/analogWrite
    public float value;                // for analogWrite
    public RuntimeBlockNode[] body;    // main body
    public RuntimeBlockNode[] elseBody;// else body
}

public class RuntimeBlocksRunner : MonoBehaviour
{
    [SerializeField] string runtimeJsonFile = "BlocksRuntime.json";
    [SerializeField] MonoBehaviour ioBehaviour; // assign RCCarRuntimeAdapter
    IRuntimeIO io;
    RuntimeBlockProgram program;
    Coroutine loopCo;

    void Start()
    {
        if (ioBehaviour == null) 
        {
            // Auto-detect adapter if not assigned
            ioBehaviour = GetComponent<MonoBehaviour>(); 
            if (!(ioBehaviour is IRuntimeIO)) ioBehaviour = FindObjectOfType<RCCarRuntimeAdapter>();
        }
        Debug.Log($"[RuntimeBlocksRunner] ioBehaviour: {ioBehaviour}");
        io = ioBehaviour as IRuntimeIO;
        if (io == null) 
        { 
            Debug.LogError("IRuntimeIO not set. Please assign RCCarRuntimeAdapter to RuntimeBlocksRunner."); 
            enabled = false; 
            return; 
        }
        LoadProgram();
    }

    void OnEnable()
    {
        if (program != null) loopCo = StartCoroutine(RunLoop());
    }

    void OnDisable()
    {
        if (loopCo != null) StopCoroutine(loopCo);
    }

    void LoadProgram()
    {
        var path = Path.Combine(Application.persistentDataPath, runtimeJsonFile);
        if (!File.Exists(path)) { Debug.LogWarning($"Runtime program not found: {path}"); return; }
        program = JsonUtility.FromJson<RuntimeBlockProgram>(File.ReadAllText(path));
    }

    IEnumerator RunLoop()
    {
        while (true)
        {
            if (program?.roots != null)
            {
                foreach (var node in program.roots)
                    yield return Eval(node);
            }
            else yield return null;
        }
    }

    IEnumerator Eval(RuntimeBlockNode n)
    {
        if (n == null) yield break;
        switch (n.type)
        {
            case "forward":
                io.MoveForward(n.number);
                yield return null; // Add yield to prevent freeze
                break;
            case "turnLeft":
                io.TurnLeft(n.number);
                yield return null;
                break;
            case "turnRight":
                io.TurnRight(n.number);
                yield return null;
                break;
            case "stop":
                io.Stop();
                yield return null;
                break;
            case "analogWrite":
                io.AnalogWrite(n.pin, n.value);
                yield return null;
                break;
            case "digitalRead":
                // side-effect only; could store to a variable if you extend the model
                _ = io.DigitalRead(n.pin);
                break;
            case "repeat":
                for (int i = 0; i < (int)n.number; i++)
                    yield return EvalList(n.body);
                break;
            case "forever":
                while (true)
                    yield return EvalList(n.body);
            case "if":
                if (io.DigitalRead(n.pin))
                    yield return EvalList(n.body);
                break;
            case "ifElse":
                if (io.DigitalRead(n.pin))
                    yield return EvalList(n.body);
                else
                    yield return EvalList(n.elseBody);
                break;
            case "wait":
                yield return new WaitForSeconds(n.number);
                break;
        }
    }

    IEnumerator EvalList(RuntimeBlockNode[] list)
    {
        if (list == null) yield break;
        foreach (var c in list)
            yield return Eval(c);
    }
}
