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
    public float value;                // for analogWrite (static value)
    public string valueVar;            // for analogWrite - variable name to resolve at runtime (e.g., "Speed")
    public string functionName;        // for functionCall - the function to call
    public string conditionVar;        // for if/ifElse - the variable name used in condition
    public RuntimeBlockNode[] body;    // main body
    public RuntimeBlockNode[] elseBody;// else body
}

public class RuntimeBlocksRunner : MonoBehaviour
{
    [SerializeField] string runtimeJsonFile = "BlocksRuntime.json";
    
    RuntimeBlockProgram program;
    IRuntimeIO io;
    bool isInitialized = false;
    
    // 함수 정의를 저장하는 딕셔너리 (functionCall 시 body 실행용)
    System.Collections.Generic.Dictionary<string, RuntimeBlockNode[]> functionDefinitions = 
        new System.Collections.Generic.Dictionary<string, RuntimeBlockNode[]>();

    // ============================================================
    // 공개 API (RCCarRuntimeAdapter에서 호출)
    // ============================================================
    
    /// <summary>
    /// 외부에서 IO 인터페이스를 설정하고 프로그램을 로드
    /// </summary>
    public void Initialize(IRuntimeIO runtimeIO)
    {
        io = runtimeIO;
        LoadProgram();
        isInitialized = program != null && program.roots != null;
        Debug.Log($"[RuntimeBlocksRunner] Initialized. IsReady: {isInitialized}");
    }
    
    /// <summary>
    /// 매 FixedUpdate에서 호출. 모든 블록을 한 번 평가합니다.
    /// </summary>
    public void Tick()
    {
        if (!isInitialized) return;
        
        foreach (var node in program.roots)
        {
            EvalSync(node, null);
        }
    }
    
    /// <summary>
    /// 프로그램이 로드되었는지 확인
    /// </summary>
    public bool IsReady => isInitialized;

    // ============================================================
    // 프로그램 로딩
    // ============================================================
    
    void LoadProgram()
    {
        var path = Path.Combine(Application.persistentDataPath, runtimeJsonFile);
        if (!File.Exists(path)) 
        { 
            Debug.LogWarning($"Runtime program not found: {path}"); 
            return; 
        }
        
        try
        {
            var json = File.ReadAllText(path);
            program = ParseJsonProgram(json);
            BuildFunctionDefinitions();
            
            Debug.Log("[RuntimeBlocksRunner] Program loaded successfully.");
            Debug.Log($"[RuntimeBlocksRunner] Function definitions: {functionDefinitions.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeBlocksRunner] Failed to parse JSON: {ex.Message}");
        }
    }
    
    void BuildFunctionDefinitions()
    {
        functionDefinitions.Clear();
        if (program?.roots == null) return;
        
        foreach (var node in program.roots)
            CollectFunctionDefinitions(node);
    }
    
    void CollectFunctionDefinitions(RuntimeBlockNode node)
    {
        if (node == null) return;
        
        if (node.type == "functionDefine" && !string.IsNullOrEmpty(node.functionName))
        {
            functionDefinitions[node.functionName] = node.body ?? new RuntimeBlockNode[0];
        }
        
        if (node.body != null)
            foreach (var child in node.body)
                CollectFunctionDefinitions(child);
        
        if (node.elseBody != null)
            foreach (var child in node.elseBody)
                CollectFunctionDefinitions(child);
    }

    // ============================================================
    // 동기식 블록 평가 (FixedUpdate 호출용)
    // ============================================================
    
    void EvalSync(RuntimeBlockNode n, System.Collections.Generic.Dictionary<string, float> localVars)
    {
        if (n == null) return;
        
        switch (n.type)
        {
            case "functionDefine":
                // 함수 정의는 실행하지 않음 (호출 시에만 실행)
                break;
                
            case "forward":
                io.MoveForward(n.number);
                break;
                
            case "turnLeft":
                io.TurnLeft(n.number);
                break;
                
            case "turnRight":
                io.TurnRight(n.number);
                break;
                
            case "stop":
                io.Stop();
                break;
                
            case "analogWrite":
                {
                    float writeValue = n.value;
                    if (!string.IsNullOrEmpty(n.valueVar) && localVars != null && localVars.TryGetValue(n.valueVar, out float localVal))
                    {
                        writeValue = localVal;
                    }
                    Debug.Log($"[EvalSync] analogWrite: pin={n.pin}, value={writeValue}, valueVar={n.valueVar}");
                    io.AnalogWrite(n.pin, writeValue);
                }
                break;
                
            case "digitalRead":
                _ = io.DigitalRead(n.pin);
                break;
                
            case "repeat":
                for (int i = 0; i < (int)n.number; i++)
                    EvalListSync(n.body, localVars);
                break;
                
            case "forever":
                // forever의 body를 매 Tick마다 한 번씩 실행
                EvalListSync(n.body, localVars);
                break;
                
            case "if":
                if (io.DigitalRead(n.pin))
                    EvalListSync(n.body, localVars);
                break;
                
            case "ifElse":
                if (io.DigitalRead(n.pin))
                    EvalListSync(n.body, localVars);
                else
                    EvalListSync(n.elseBody, localVars);
                break;
                
            case "wait":
                // FixedUpdate 기반에서는 wait 블록을 무시 (또는 별도 타이머 구현 필요)
                // TODO: 필요시 waitTimer 상태 변수로 구현 가능
                break;
                
            case "functionCall":
                Debug.Log($"[EvalSync] functionCall: {n.functionName}, arg={n.number}");
                if (!string.IsNullOrEmpty(n.functionName) && functionDefinitions.TryGetValue(n.functionName, out var funcBody))
                {
                    var funcLocalVars = new System.Collections.Generic.Dictionary<string, float>
                    {
                        { "Speed", n.number }
                    };
                    EvalListSync(funcBody, funcLocalVars);
                }
                else
                {
                    Debug.LogWarning($"[EvalSync] Function not found: {n.functionName}");
                }
                break;
        }
    }
    
    void EvalListSync(RuntimeBlockNode[] list, System.Collections.Generic.Dictionary<string, float> localVars)
    {
        if (list == null) return;
        foreach (var node in list)
            EvalSync(node, localVars);
    }

    // ============================================================
    // JSON 파싱 (기존 코드 유지)
    // ============================================================
    
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
            if (c == ']') { idx++; break; }
            if (c == '{') list.Add(ParseNode(json, ref idx));
            else idx++;
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
            if (c == '}') { idx++; break; }
            if (c == '"')
            {
                string key = ParseString(json, ref idx);
                
                while (idx < json.Length && json[idx] != ':') idx++;
                if (idx >= json.Length) break;
                idx++; 
                
                while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
                if (idx >= json.Length) break;

                if (key == "type") node.type = ParseString(json, ref idx);
                else if (key == "number") node.number = ParseFloat(json, ref idx);
                else if (key == "pin") node.pin = (int)ParseFloat(json, ref idx);
                else if (key == "value") node.value = ParseFloat(json, ref idx);
                else if (key == "valueVar") node.valueVar = ParseString(json, ref idx);
                else if (key == "functionName") node.functionName = ParseString(json, ref idx);
                else if (key == "conditionVar") node.conditionVar = ParseString(json, ref idx);
                else if (key == "body") 
                {
                    while(idx < json.Length && json[idx] != '[') idx++;
                    if (idx < json.Length) node.body = ParseNodeArray(json, ref idx).ToArray();
                }
                else if (key == "elseBody") 
                {
                    while(idx < json.Length && json[idx] != '[') idx++;
                    if (idx < json.Length) node.elseBody = ParseNodeArray(json, ref idx).ToArray();
                }
                else SkipValue(json, ref idx);
            }
            else idx++;
        }
        return node;
    }

    string ParseString(string json, ref int idx)
    {
        while (idx < json.Length && json[idx] != '"') idx++;
        if (idx >= json.Length) return "";
        
        idx++; 
        int start = idx;
        while (idx < json.Length && json[idx] != '"') idx++; 
        if (idx >= json.Length) return "";
        
        string s = json.Substring(start, idx - start);
        idx++;
        return s;
    }

    float ParseFloat(string json, ref int idx)
    {
        while(idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == ':')) idx++;
        if (idx >= json.Length) return 0f;
        
        int start = idx;
        while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '.' || json[idx] == '-' || json[idx] == 'e' || json[idx] == 'E'))
            idx++;
        
        if (start == idx) return 0f;
        
        string numStr = json.Substring(start, idx - start);
        float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result);
        return result;
    }

    void SkipValue(string json, ref int idx)
    {
        int depth = 0;
        bool inString = false;
        
        while (idx < json.Length)
        {
            char c = json[idx];
            
            if (inString)
            {
                if (c == '"' && (idx == 0 || json[idx-1] != '\\')) inString = false;
                idx++;
                continue;
            }
            
            if (c == '"') { inString = true; idx++; continue; }
            if (c == '{' || c == '[') { depth++; idx++; continue; }
            if (c == '}' || c == ']') 
            { 
                if (depth > 0) { depth--; idx++; continue; }
                return; 
            }
            if (c == ',' && depth == 0) return;
            
            idx++;
        }
    }
}
