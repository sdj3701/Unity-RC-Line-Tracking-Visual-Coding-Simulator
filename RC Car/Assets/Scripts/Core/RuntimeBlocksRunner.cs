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
    public string type;                // "forward","turnLeft","turnRight","repeat","forever","if","ifElse","wait","digitalRead","analogWrite","stop","functionCall"
    public float number;               // speed or repeat count or wait seconds
    public int pin;                    // for digitalRead/analogWrite
    public float value;                // for analogWrite
    public string functionName;        // for functionCall - the function to call
    public string conditionVar;        // for if/ifElse - the variable name used in condition
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
    
    // 함수 정의를 저장하는 딕셔너리 (functionCall 시 body 실행용)
    System.Collections.Generic.Dictionary<string, RuntimeBlockNode[]> functionDefinitions = 
        new System.Collections.Generic.Dictionary<string, RuntimeBlockNode[]>();

    void Start()
    {
        // Instead of failing only once, start a routine to wait for valid IO
        StartCoroutine(WaitForIOAndStart());
    }

    IEnumerator WaitForIOAndStart()
    {
        while (ioBehaviour == null)
        {
            if (ioBehaviour == null)
            {
                ioBehaviour = GetComponent<MonoBehaviour>();
                if (ioBehaviour != null && !(ioBehaviour is IRuntimeIO)) ioBehaviour = null;
            }

            if (ioBehaviour == null)
            {
                var adapter = FindObjectsOfType<RCCarRuntimeAdapter>(true);
                if (adapter != null && adapter.Length > 0) ioBehaviour = adapter[0];
            }

            if (ioBehaviour == null)
            {
                // Wait for next frame and try again
                yield return null; 
            }
        }

        Debug.Log($"[RuntimeBlocksRunner] ioBehaviour: {ioBehaviour}");
        io = ioBehaviour as IRuntimeIO;
        if (io == null) 
        { 
            Debug.LogError("IRuntimeIO not set. Please assign RCCarRuntimeAdapter to RuntimeBlocksRunner."); 
            enabled = false; 
            yield break;
        }
        LoadProgram();
        if (program != null) loopCo = StartCoroutine(RunLoop());
    }

    public void OnEnable()
    {
        // If the object is disabled and re-enabled, resume the loop if program is loaded.
        if (program != null) 
        {
            if (loopCo != null) StopCoroutine(loopCo);
            loopCo = StartCoroutine(RunLoop());
        }
        Debug.Log($"[RuntimeBlocksRunner] OnEnable: {enabled}");
    }

    public void OnDisable()
    {
        if (loopCo != null) StopCoroutine(loopCo);
        Debug.Log($"[RuntimeBlocksRunner] OnDisable: {enabled}");
    }

    void LoadProgram()
    {
        var path = Path.Combine(Application.persistentDataPath, runtimeJsonFile);
        if (!File.Exists(path)) { Debug.LogWarning($"Runtime program not found: {path}"); return; }
        
        // Use custom parser instead of JsonUtility to avoid depth limit
        try
        {
            var json = File.ReadAllText(path);
            program = ParseJsonProgram(json);
            
            // 함수 정의 수집 (functionCall에서 사용)
            BuildFunctionDefinitions();
            
            Debug.Log("[RuntimeBlocksRunner] Program loaded successfully using custom parser.");
            Debug.Log($"[RuntimeBlocksRunner] Function definitions loaded: {functionDefinitions.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeBlocksRunner] Failed to parse JSON: {ex.Message}");
        }
    }
    
    /// <summary>
    /// roots에서 "functionDefine" 타입 노드를 찾아 함수 정의 딕셔너리에 저장
    /// </summary>
    void BuildFunctionDefinitions()
    {
        functionDefinitions.Clear();
        
        if (program?.roots == null) return;
        
        foreach (var node in program.roots)
        {
            CollectFunctionDefinitions(node);
        }
    }
    
    void CollectFunctionDefinitions(RuntimeBlockNode node)
    {
        if (node == null) return;
        
        // functionDefine 타입이면 딕셔너리에 저장
        if (node.type == "functionDefine" && !string.IsNullOrEmpty(node.functionName))
        {
            functionDefinitions[node.functionName] = node.body ?? new RuntimeBlockNode[0];
            Debug.Log($"[RuntimeBlocksRunner] Registered function: {node.functionName} with {node.body?.Length ?? 0} body nodes");
        }
        
        // 재귀적으로 body와 elseBody도 검사
        if (node.body != null)
        {
            foreach (var child in node.body)
                CollectFunctionDefinitions(child);
        }
        
        if (node.elseBody != null)
        {
            foreach (var child in node.elseBody)
                CollectFunctionDefinitions(child);
        }
    }

    // --- Custom Recursive JSON Parser (Simple & Lightweight) ---
    RuntimeBlockProgram ParseJsonProgram(string json)
    {
        int idx = json.IndexOf("\"roots\"");
        if (idx == -1) return new RuntimeBlockProgram(); 

        idx = json.IndexOf('[', idx);
        if (idx == -1) return new RuntimeBlockProgram();

        var nodeList = ParseNodeArray(json, ref idx);
        return new RuntimeBlockProgram { roots = nodeList.ToArray() };
    }

    System.Collections.Generic.List<RuntimeBlockNode> ParseNodeArray(string json, ref int idx)
    {
        var list = new System.Collections.Generic.List<RuntimeBlockNode>();
        idx++; // skip [
        
        while (idx < json.Length)
        {
            char c = json[idx];
            if (c == ']' ) 
            {
                idx++; 
                break; 
            }
            if (c == '{')
            {
                list.Add(ParseNode(json, ref idx));
            }
            else
            {
                idx++;
            }
        }
        return list;
    }

    RuntimeBlockNode ParseNode(string json, ref int idx)
    {
        var node = new RuntimeBlockNode();
        idx++; 

        while (idx < json.Length)
        {
            char c = json[idx];
            if (c == '}') 
            {
                idx++; 
                break; 
            }
            if (c == '"')
            {
                string key = ParseString(json, ref idx);
                while (json[idx] != ':') idx++;
                idx++; 

                if (key == "type") node.type = ParseString(json, ref idx);
                else if (key == "number") node.number = ParseFloat(json, ref idx);
                else if (key == "pin") node.pin = (int)ParseFloat(json, ref idx);
                else if (key == "value") node.value = ParseFloat(json, ref idx);
                else if (key == "functionName") node.functionName = ParseString(json, ref idx);
                else if (key == "conditionVar") node.conditionVar = ParseString(json, ref idx);
                else if (key == "body") 
                {
                    while(json[idx] != '[') idx++;
                    node.body = ParseNodeArray(json, ref idx).ToArray();
                }
                else if (key == "elseBody") 
                {
                    while(json[idx] != '[') idx++;
                    node.elseBody = ParseNodeArray(json, ref idx).ToArray();
                }
                else
                {
                    SkipValue(json, ref idx);
                }
            }
            else
            {
                idx++;
            }
        }
        return node;
    }

    string ParseString(string json, ref int idx)
    {
        idx++; 
        int start = idx;
        while (json[idx] != '"') idx++; 
        string s = json.Substring(start, idx - start);
        idx++; 
        return s;
    }

    float ParseFloat(string json, ref int idx)
    {
        while(char.IsWhiteSpace(json[idx]) || json[idx] == ':') idx++;
        int start = idx;
        while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '.' || json[idx] == '-' || json[idx] == 'e' || json[idx] == 'E'))
        {
            idx++;
        }
        string numStr = json.Substring(start, idx - start);
        float.TryParse(numStr, out float connect);
        return connect;
    }

    void SkipValue(string json, ref int idx)
    {
        while (idx < json.Length)
        {
            char c = json[idx];
            if (c == ',' || c == '}') return; 
            idx++;
        }
    }
    // --- End Custom Parser ---

    IEnumerator RunLoop()
    {
        Debug.Log("[RuntimeBlocksRunner] RunLoop Started");
        while (true)
        {
            if (program?.roots != null)
            {
                Debug.Log($"[RuntimeBlocksRunner] RunLoop iterating. Roots count: {program.roots.Length}");
                foreach (var node in program.roots)
                    yield return Eval(node);
            }
            else 
            {
                Debug.LogWarning("[RuntimeBlocksRunner] program.roots is null");
                yield return null;
            }
            yield return null; // Safety yield per loop
        }
    }

    IEnumerator Eval(RuntimeBlockNode n)
    {
        if (n == null) yield break;
        Debug.Log($"[RuntimeBlocksRunner] Eval: {n.type}");
        switch (n.type)
        {
            case "forward":
                Debug.Log($"[RuntimeBlocksRunner] Action: Forward {n.number}");
                io.MoveForward(n.number);
                yield return null; 
                break;
            case "turnLeft":
                Debug.Log($"[RuntimeBlocksRunner] Action: TurnLeft {n.number}");
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
                _ = io.DigitalRead(n.pin);
                break;
            case "repeat":
                for (int i = 0; i < (int)n.number; i++)
                    yield return EvalList(n.body);
                break;
            case "forever":
                while (true)
                {
                    yield return EvalList(n.body);
                    yield return null; // Safety break
                }
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
            case "functionCall":
                Debug.Log($"[RuntimeBlocksRunner] FunctionCall: {n.functionName} with arg {n.number}");
                // 함수 정의를 찾아서 body 실행
                if (!string.IsNullOrEmpty(n.functionName) && functionDefinitions.TryGetValue(n.functionName, out var funcBody))
                {
                    yield return EvalList(funcBody);
                }
                else
                {
                    Debug.LogWarning($"[RuntimeBlocksRunner] Function not found: {n.functionName}");
                }
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
