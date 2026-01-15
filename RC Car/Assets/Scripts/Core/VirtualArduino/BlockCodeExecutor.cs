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
                    // 핀이 변수 참조인 경우 (pinVar 우선, 없으면 pin 사용)
                    int targetPin = node.pin;
                    if (!string.IsNullOrEmpty(node.pinVar))
                    {
                        targetPin = (int)GetVariable(node.pinVar, node.pin);
                        Debug.Log($"<color=cyan>[3] ExecuteNode: analogWrite pinVar={node.pinVar} → pin={targetPin}</color>");
                    }
                    
                    // 값이 변수 참조인 경우 (valueVar 우선, 없으면 value 사용)
                    float value = node.value;
                    if (!string.IsNullOrEmpty(node.valueVar))
                    {
                        value = GetVariable(node.valueVar, 0f);
                        Debug.Log($"<color=cyan>[3] ExecuteNode: analogWrite pin={targetPin}, valueVar={node.valueVar} → {value}</color>");
                    }
                    else
                    {
                        Debug.Log($"<color=cyan>[3] ExecuteNode: analogWrite pin={targetPin}, value={value}</color>");
                    }
                    arduino.AnalogWrite(targetPin, value);
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
                
            case "callFunction":
                ExecuteFunction(node);
                break;
                
            case "analogRead":
                ExecuteAnalogRead(node);
                break;
                
            default:
                Debug.Log($"<color=gray>[3] ExecuteNode: Unknown type '{node.type}'</color>");
                break;
        }
    }
    
    /// <summary>
    /// 함수 호출 실행
    /// </summary>
    void ExecuteFunction(BlockNode callNode)
    {
        if (string.IsNullOrEmpty(callNode.functionName))
        {
            Debug.LogWarning("<color=red>[3] ExecuteFunction: functionName is empty!</color>");
            return;
        }
        
        // functions 배열에서 해당 함수 찾기
        BlockNode funcDef = null;
        if (program.functions != null)
        {
            foreach (var func in program.functions)
            {
                if (func.name == callNode.functionName)
                {
                    funcDef = func;
                    break;
                }
            }
        }
        
        if (funcDef == null)
        {
            Debug.LogWarning($"<color=red>[3] ExecuteFunction: Function '{callNode.functionName}' not found!</color>");
            return;
        }
        
        Debug.Log($"<color=yellow>[3] ExecuteFunction: Calling '{callNode.functionName}' with {callNode.args?.Count ?? 0} args, {funcDef.parameters?.Count ?? 0} params</color>");
        
        // args를 params 이름으로 매핑
        // params: ["s"], args: [150] → variables["s"] = 150
        if (callNode.args != null && funcDef.parameters != null)
        {
            int count = Math.Min(callNode.args.Count, funcDef.parameters.Count);
            for (int i = 0; i < count; i++)
            {
                string paramName = funcDef.parameters[i];
                float argValue = callNode.args[i];
                variables[paramName] = argValue;
                Debug.Log($"<color=yellow>[3] ExecuteFunction: Set {paramName} = {argValue}</color>");
            }
        }
        
        // args가 더 많은 경우 arg0, arg1...으로 저장 (fallback)
        if (callNode.args != null)
        {
            for (int i = 0; i < callNode.args.Count; i++)
            {
                variables[$"arg{i}"] = callNode.args[i];
            }
        }
        
        // 함수 body 실행
        if (funcDef.body != null)
        {
            Debug.Log($"<color=yellow>[3] ExecuteFunction: Executing body ({funcDef.body.Count} blocks)</color>");
            foreach (var childNode in funcDef.body)
            {
                ExecuteNode(childNode);
            }
        }
    }
    
    /// <summary>
    /// If/IfElse 블록 실행
    /// </summary>
    void ExecuteIfBlock(BlockNode node, bool hasElse)
    {
        bool condition;
        
        // conditionSensorFunction이 있으면 센서 값을 읽어서 조건 판단
        if (!string.IsNullOrEmpty(node.conditionSensorFunction))
        {
            if (arduino == null)
            {
                Debug.LogWarning("<color=red>[3] ExecuteIfBlock: arduino is NULL for sensor condition!</color>");
                condition = false;
            }
            else
            {
                // 센서 값 읽기 (VirtualLineSensor.OnFunctionAnalogRead 호출)
                float sensorValue = arduino.FunctionAnalogRead(node.conditionSensorFunction);
                
                // 센서 값과 conditionValue 비교
                // sensorValue == conditionValue이면 true (예: 센서 1이고 conditionValue 1이면 true)
                condition = Mathf.Approximately(sensorValue, node.conditionValue);
                    
                Debug.Log($"<color=magenta>[3] ExecuteIfBlock: {node.type} sensor={node.conditionSensorFunction}, sensorValue={sensorValue}, conditionValue={node.conditionValue} → {condition}</color>");
            }
        }
        else
        {
            // 기존 로직: conditionValue를 Boolean으로 변환
            condition = ConvertToBoolean(node.conditionValue);
            Debug.Log($"<color=magenta>[3] ExecuteNode: {node.type} conditionValue={node.conditionValue} → {condition}</color>");
        }
        
        if (condition)
        {
            // then body 실행
            if (node.body != null)
            {
                Debug.Log($"<color=magenta>[3] ExecuteIfBlock: Executing body ({node.body.Count} blocks)</color>");
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
                Debug.Log($"<color=magenta>[3] ExecuteIfBlock: Executing elseBody ({node.elseBody.Count} blocks)</color>");
                foreach (var childNode in node.elseBody)
                {
                    ExecuteNode(childNode);
                }
            }
        }
        else
        {
            Debug.Log($"<color=gray>[3] ExecuteIfBlock: Condition is false, skipping body</color>");
        }
    }
    
    /// <summary>
    /// 값을 Boolean으로 변환
    /// int/float: 0보다 크면 true, 아니면 false
    /// </summary>
    bool ConvertToBoolean(float value)
    {
        return value > 0;
    }
    
    /// <summary>
    /// AnalogRead 블록 실행 (센서 읽기)
    /// VirtualLineSensor.OnFunctionAnalogRead를 호출합니다.
    /// </summary>
    void ExecuteAnalogRead(BlockNode node)
    {
        if (arduino == null)
        {
            Debug.LogWarning("<color=red>[3] ExecuteAnalogRead: arduino is NULL!</color>");
            return;
        }
        
        if (string.IsNullOrEmpty(node.sensorFunction))
        {
            Debug.LogWarning("<color=red>[3] ExecuteAnalogRead: sensorFunction is empty!</color>");
            return;
        }
        
        // VirtualArduinoMicro.FunctionAnalogRead를 호출하여 센서 값 읽기
        float sensorValue = arduino.FunctionAnalogRead(node.sensorFunction);
        Debug.Log($"<color=cyan>[3] ExecuteAnalogRead: {node.sensorFunction} → {sensorValue}</color>");
        
        // targetVar가 있으면 변수에 저장
        if (!string.IsNullOrEmpty(node.targetVar))
        {
            variables[node.targetVar] = sensorValue;
            Debug.Log($"<color=cyan>[3] ExecuteAnalogRead: Stored in variable '{node.targetVar}' = {sensorValue}</color>");
        }
    }
    
    /// <summary>
    /// 문자열을 Boolean으로 변환
    /// "true" -> true, 그 외 -> false
    /// 숫자 문자열이면 숫자로 변환 후 > 0 판단
    /// </summary>
    bool ConvertToBoolean(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        
        // "true"/"false" 문자열 판단
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        
        // 숫자로 변환 시도
        if (float.TryParse(value, out float numValue))
            return numValue > 0;
        
        // 그 외의 문자열은 false
        return false;
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
                    case "pinVar": node.pinVar = ParseString(json, ref idx); break;
                    case "value": node.value = ParseFloat(json, ref idx); break;
                    case "valueVar": node.valueVar = ParseString(json, ref idx); break;
                    case "functionName": node.functionName = ParseString(json, ref idx); break;
                    case "conditionVar": node.conditionVar = ParseString(json, ref idx); break;
                    case "conditionPin": node.conditionPin = (int)ParseFloat(json, ref idx); break;
                    case "conditionValue": node.conditionValue = ParseFloat(json, ref idx); break;
                    case "conditionSensorFunction": node.conditionSensorFunction = ParseString(json, ref idx); break;
                    case "sensorFunction": node.sensorFunction = ParseString(json, ref idx); break;
                    case "targetVar": node.targetVar = ParseString(json, ref idx); break;
                    case "name": node.name = ParseString(json, ref idx); break;
                    case "args":
                        // args 배열 파싱
                        while (idx < json.Length && json[idx] != '[') idx++;
                        if (idx < json.Length) node.args = ParseFloatArray(json, ref idx);
                        break;
                    case "params":
                        // params 배열 파싱 (파라미터 이름 목록)
                        while (idx < json.Length && json[idx] != '[') idx++;
                        if (idx < json.Length) node.parameters = ParseStringArray(json, ref idx);
                        break;
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
    
    List<float> ParseFloatArray(string json, ref int idx)
    {
        var list = new List<float>();
        idx++; // skip '['
        
        while (idx < json.Length)
        {
            char c = json[idx];
            if (c == ']') { idx++; break; }
            if (char.IsDigit(c) || c == '-' || c == '.')
            {
                list.Add(ParseFloat(json, ref idx));
            }
            else
            {
                idx++;
            }
        }
        return list;
    }
    
    List<string> ParseStringArray(string json, ref int idx)
    {
        var list = new List<string>();
        idx++; // skip '['
        
        while (idx < json.Length)
        {
            char c = json[idx];
            if (c == ']') { idx++; break; }
            if (c == '"')
            {
                list.Add(ParseString(json, ref idx));
            }
            else
            {
                idx++;
            }
        }
        return list;
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
        public string name;           // 함수 정의에서 함수 이름
        public List<string> parameters;  // 함수 파라미터 이름 목록
        public float number;
        public int pin;
        public string pinVar;     // 핀 변수 참조 (pinVar 우선, 없으면 pin 사용)
        public float value;
        public string valueVar;
        public string functionName;   // 함수 호출에서 호출할 함수 이름
        public List<float> args;      // 함수 호출 시 전달할 인자들
        public string conditionVar;
        public int conditionPin;      // if/ifElse용 조건 핀
        public string conditionSensorFunction; // if/ifElse용 센서 기반 조건 (예: "leftSensor")
        public float conditionValue;  // if/ifElse용 조건 값 (숫자)
        public string sensorFunction; // analogRead용 센서 기능 이름
        public string targetVar;      // analogRead 결과를 저장할 변수 이름
        public string setVarName;
        public float setVarValue;
        public List<BlockNode> body;
        public List<BlockNode> elseBody;
    }
}
