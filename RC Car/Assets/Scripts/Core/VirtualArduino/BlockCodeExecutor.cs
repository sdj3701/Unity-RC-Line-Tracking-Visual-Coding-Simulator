using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MG_BlocksEngine2.UI;

/// <summary>
/// 블록 코드 실행기
/// JSON 파일에서 블록 프로그램을 로드하고 변수를 관리합니다.
/// </summary>
public class BlockCodeExecutor : MonoBehaviour
{
    [Header("Program File")]
    [Tooltip("런타임 JSON 파일 이름")]
    [SerializeField] string runtimeJsonFile = "BlocksRuntime.json";
    
    [Header("Debug")]
    [SerializeField] bool showDebugLogs = true;
    
    // 런타임 변수 저장소 (변수 이름 → 값)
    Dictionary<string, float> variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    
    // 프로그램 데이터
    BlockProgram program;
    bool isLoaded = false;
    
    /// <summary>
    /// 프로그램이 로드되었는지 확인
    /// </summary>
    public bool IsLoaded => isLoaded;
    
    /// <summary>
    /// 현재 변수 목록 (읽기 전용)
    /// </summary>
    public IReadOnlyDictionary<string, float> Variables => variables;
    
    // ============================================================
    // Unity 이벤트
    // ============================================================
    
    void Start()
    {
        // 씬 시작 시 자동으로 JSON 로드
        LoadProgram();
    }
    
    void OnEnable()
    {
        // 코드 생성 이벤트 구독
        BE2_UI_ContextMenuManager.OnCodeGenerated += OnCodeGeneratedHandler;
    }
    
    void OnDisable()
    {
        // 코드 생성 이벤트 구독 해제
        BE2_UI_ContextMenuManager.OnCodeGenerated -= OnCodeGeneratedHandler;
    }
    
    void OnCodeGeneratedHandler()
    {
        LogDebug("OnCodeGenerated event received. Reloading program...");
        ReloadProgram();
    }
    
    // ============================================================
    // 공개 API
    // ============================================================
    
    [Header("Connected Components")]
    [Tooltip("가상 아두이노 마이크로 (자동 탐색 가능)")]
    public VirtualArduinoMicro arduino;
    
    bool hasRunInit = false;
    
    /// <summary>
    /// JSON 파일에서 프로그램 로드
    /// </summary>
    public void LoadProgram()
    {
        var path = Path.Combine(Application.persistentDataPath, runtimeJsonFile);
        
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[BlockCodeExecutor] Program file not found: {path}");
            return;
        }
        
        try
        {
            var json = File.ReadAllText(path);
            program = ParseJsonProgram(json);
            
            // 초기화 블록에서 변수 수집
            CollectVariablesFromInit();
            
            // Arduino 자동 탐색
            if (arduino == null)
                arduino = FindObjectOfType<VirtualArduinoMicro>();
            
            isLoaded = true;
            hasRunInit = false;
            LogDebug($"Program loaded. Variables: {variables.Count}, Loop blocks: {program?.loop?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlockCodeExecutor] Failed to load program: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 매 프레임 실행 (FixedUpdate에서 호출)
    /// </summary>
    public void Tick()
    {
        if (!isLoaded || program == null) return;
        
        // init 블록 한 번만 실행
        if (!hasRunInit && program.init != null)
        {
            Debug.Log("<color=orange>[1] BlockCodeExecutor.Tick() - Running INIT blocks</color>");
            foreach (var node in program.init)
            {
                ExecuteNode(node);
            }
            hasRunInit = true;
        }
        
        // loop 블록 매번 실행
        if (program.loop != null && program.loop.Count > 0)
        {
            Debug.Log($"<color=yellow>[2] BlockCodeExecutor.Tick() - Running LOOP ({program.loop.Count} blocks)</color>");
            foreach (var node in program.loop)
            {
                ExecuteNode(node);
            }
        }
    }
    
    /// <summary>
    /// 단일 블록 노드 실행
    /// </summary>
    void ExecuteNode(BlockNode node)
    {
        if (node == null) return;
        
        switch (node.type)
        {
            case "setVariable":
                if (!string.IsNullOrEmpty(node.setVarName))
                {
                    variables[node.setVarName] = node.setVarValue;
                    Debug.Log($"<color=lime>[3] ExecuteNode: setVariable {node.setVarName} = {node.setVarValue}</color>");
                }
                break;
                
            case "analogWrite":
                if (arduino != null)
                {
                    // 값이 변수 참조인 경우
                    float value = node.value;
                    if (!string.IsNullOrEmpty(node.valueVar))
                    {
                        value = GetVariable(node.valueVar, 0f);
                        Debug.Log($"<color=cyan>[3] ExecuteNode: analogWrite pin={node.pin}, valueVar={node.valueVar} → {value}</color>");
                    }
                    else
                    {
                        Debug.Log($"<color=cyan>[3] ExecuteNode: analogWrite pin={node.pin}, value={value}</color>");
                    }
                    arduino.AnalogWrite(node.pin, value);
                }
                else
                {
                    Debug.LogWarning("<color=red>[3] ExecuteNode: arduino is NULL!</color>");
                }
                break;
                
            case "if":
                ExecuteIfBlock(node, false);
                break;
                
            case "ifElse":
                ExecuteIfBlock(node, true);
                break;
                
            default:
                Debug.Log($"<color=gray>[3] ExecuteNode: Unknown type '{node.type}'</color>");
                break;
        }
    }
    
    /// <summary>
    /// If/IfElse 블록 실행
    /// </summary>
    void ExecuteIfBlock(BlockNode node, bool hasElse)
    {
        // conditionPin으로 digitalRead 수행
        bool condition = false;
        if (arduino != null)
        {
            condition = arduino.DigitalRead(node.conditionPin);
            Debug.Log($"<color=magenta>[3] ExecuteNode: {node.type} conditionPin={node.conditionPin} → {condition}</color>");
        }
        
        if (condition)
        {
            // then body 실행
            if (node.body != null)
            {
                foreach (var childNode in node.body)
                {
                    ExecuteNode(childNode);
                }
            }
        }
        else if (hasElse)
        {
            // else body 실행
            if (node.elseBody != null)
            {
                foreach (var childNode in node.elseBody)
                {
                    ExecuteNode(childNode);
                }
            }
        }
    }
    
    /// <summary>
    /// 변수 값 가져오기
    /// </summary>
    public float GetVariable(string name, float defaultValue = 0f)
    {
        if (variables.TryGetValue(name, out float value))
            return value;
        return defaultValue;
    }
    
    /// <summary>
    /// 변수 값 설정하기
    /// </summary>
    public void SetVariable(string name, float value)
    {
        variables[name] = value;
        LogDebug($"Variable set: {name} = {value}");
    }
    
    /// <summary>
    /// 변수 존재 여부 확인
    /// </summary>
    public bool HasVariable(string name)
    {
        return variables.ContainsKey(name);
    }
    
    /// <summary>
    /// 모든 변수 초기화
    /// </summary>
    public void ClearVariables()
    {
        variables.Clear();
        LogDebug("All variables cleared.");
    }
    
    /// <summary>
    /// 프로그램 다시 로드 (변수 초기화 포함)
    /// </summary>
    public void ReloadProgram()
    {
        ClearVariables();
        isLoaded = false;
        hasRunInit = false;
        program = null;
        LoadProgram();
    }
    
    // ============================================================
    // JSON 파싱
    // ============================================================
    
    BlockProgram ParseJsonProgram(string json)
    {
        var prog = new BlockProgram();
        
        // init 배열 파싱
        int initIdx = json.IndexOf("\"init\"");
        if (initIdx != -1)
        {
            initIdx = json.IndexOf('[', initIdx);
            if (initIdx != -1)
                prog.init = ParseNodeArray(json, ref initIdx);
        }
        
        // loop 배열 파싱
        int loopIdx = json.IndexOf("\"loop\"");
        if (loopIdx != -1)
        {
            loopIdx = json.IndexOf('[', loopIdx);
            if (loopIdx != -1)
                prog.loop = ParseNodeArray(json, ref loopIdx);
        }
        
        // functions 배열 파싱
        int funcIdx = json.IndexOf("\"functions\"");
        if (funcIdx != -1)
        {
            funcIdx = json.IndexOf('[', funcIdx);
            if (funcIdx != -1)
                prog.functions = ParseNodeArray(json, ref funcIdx);
        }
        
        return prog;
    }
    
    List<BlockNode> ParseNodeArray(string json, ref int idx)
    {
        var list = new List<BlockNode>();
        idx++; // skip '['
        
        while (idx < json.Length)
        {
            char c = json[idx];
            if (c == ']') { idx++; break; }
            if (c == '{') list.Add(ParseNode(json, ref idx));
            else idx++;
        }
        return list;
    }
    
    BlockNode ParseNode(string json, ref int idx)
    {
        var node = new BlockNode();
        idx++; // skip '{'
        
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
                
                switch (key)
                {
                    case "type": node.type = ParseString(json, ref idx); break;
                    case "setVarName": node.setVarName = ParseString(json, ref idx); break;
                    case "setVarValue": node.setVarValue = ParseFloat(json, ref idx); break;
                    case "number": node.number = ParseFloat(json, ref idx); break;
                    case "pin": node.pin = (int)ParseFloat(json, ref idx); break;
                    case "value": node.value = ParseFloat(json, ref idx); break;
                    case "valueVar": node.valueVar = ParseString(json, ref idx); break;
                    case "functionName": node.functionName = ParseString(json, ref idx); break;
                    case "conditionVar": node.conditionVar = ParseString(json, ref idx); break;
                    case "body":
                        while (idx < json.Length && json[idx] != '[') idx++;
                        if (idx < json.Length) node.body = ParseNodeArray(json, ref idx);
                        break;
                    case "elseBody":
                        while (idx < json.Length && json[idx] != '[') idx++;
                        if (idx < json.Length) node.elseBody = ParseNodeArray(json, ref idx);
                        break;
                    default:
                        SkipValue(json, ref idx);
                        break;
                }
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
        while (idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == ':')) idx++;
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
                if (c == '"' && (idx == 0 || json[idx - 1] != '\\')) inString = false;
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
    
    // ============================================================
    // 변수 수집
    // ============================================================
    
    void CollectVariablesFromInit()
    {
        if (program?.init == null) return;
        
        foreach (var node in program.init)
        {
            CollectVariablesFromNode(node);
        }
    }
    
    void CollectVariablesFromNode(BlockNode node)
    {
        if (node == null) return;
        
        // setVariable 타입의 블록에서 변수 이름과 값 수집
        if (node.type == "setVariable" && !string.IsNullOrEmpty(node.setVarName))
        {
            variables[node.setVarName] = node.setVarValue;
            LogDebug($"Variable collected: {node.setVarName} = {node.setVarValue}");
        }
        
        // 하위 노드 재귀 탐색
        if (node.body != null)
        {
            foreach (var child in node.body)
                CollectVariablesFromNode(child);
        }
        
        if (node.elseBody != null)
        {
            foreach (var child in node.elseBody)
                CollectVariablesFromNode(child);
        }
    }
    
    void LogDebug(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[BlockCodeExecutor] {message}");
    }
    
    // ============================================================
    // 내부 데이터 클래스
    // ============================================================
    
    [Serializable]
    class BlockProgram
    {
        public List<BlockNode> functions;
        public List<BlockNode> init;
        public List<BlockNode> loop;
    }
    
    [Serializable]
    class BlockNode
    {
        public string type;
        public float number;
        public int pin;
        public float value;
        public string valueVar;
        public string functionName;
        public string conditionVar;
        public int conditionPin;      // if/ifElse용 조건 핀
        public string setVarName;
        public float setVarValue;
        public List<BlockNode> body;
        public List<BlockNode> elseBody;
    }
}
