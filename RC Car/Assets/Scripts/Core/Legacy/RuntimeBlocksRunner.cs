// using System;
// using System.Collections;
// using System.IO;
// using UnityEngine;

// public interface IRuntimeIO
// {
//     bool DigitalRead(int pin);
//     void AnalogWrite(int pin, float value);
//     void MoveForward(float speed01);
//     void TurnLeft(float speed01);
//     void TurnRight(float speed01);
//     void Stop();
// }

// [Serializable]
// public class RuntimeBlockProgram
// {
//     public RuntimeBlockNode[] functions;  // 함수 정의
//     public RuntimeBlockNode[] init;       // 초기화 블록 (한 번만 실행)
//     public RuntimeBlockNode[] loop;       // 반복 블록 (매 프레임)
    
//     // 레거시 호환용
//     public RuntimeBlockNode[] roots;
// }

// [Serializable]
// public class RuntimeBlockNode
// {
//     public string type;                // "forward","turnLeft","turnRight","repeat","forever","if","ifElse","wait","digitalRead","analogWrite","stop","functionCall","setVariable"
//     public float number;               // speed or repeat count or wait seconds
//     public int pin;                    // for digitalRead/analogWrite
//     public float value;                // for analogWrite (static value)
//     public string valueVar;            // for analogWrite - variable name to resolve at runtime
//     public string functionName;        // for functionCall - the function to call
//     public string conditionVar;        // for if/ifElse - the variable name used in condition
//     public string setVarName;          // for setVariable - variable name to set
//     public float setVarValue;          // for setVariable - value to set
//     public RuntimeBlockNode[] body;    // main body
//     public RuntimeBlockNode[] elseBody;// else body
// }

// public class RuntimeBlocksRunner : MonoBehaviour
// {
//     [SerializeField] string runtimeJsonFile = "BlocksRuntime.json";
    
//     RuntimeBlockProgram program;
//     IRuntimeIO io;
//     bool isInitialized = false;
//     bool hasRunInit = false;  // mBlock 스타일: init 블록 실행 완료 여부
    
//     // 함수 정의를 저장하는 딕셔너리
//     System.Collections.Generic.Dictionary<string, RuntimeBlockNode[]> functionDefinitions = 
//         new System.Collections.Generic.Dictionary<string, RuntimeBlockNode[]>();
    
//     // mBlock 스타일: 런타임 변수 저장
//     System.Collections.Generic.Dictionary<string, float> runtimeVariables = 
//         new System.Collections.Generic.Dictionary<string, float>();

//     // ============================================================
//     // 공개 API (RCCarRuntimeAdapter에서 호출)
//     // ============================================================
    
//     /// <summary>
//     /// 외부에서 IO 인터페이스를 설정하고 프로그램을 로드
//     /// </summary>
//     public void Initialize(IRuntimeIO runtimeIO)
//     {
//         io = runtimeIO;
//         hasRunInit = false;  // 초기화 블록 다시 실행 필요
//         runtimeVariables.Clear();
//         LoadProgram();
        
//         // mBlock 스타일: init 또는 loop 배열이 있으면 초기화 성공
//         bool hasMBlockFormat = (program?.init != null && program.init.Length > 0) || 
//                                (program?.loop != null && program.loop.Length > 0);
//         bool hasLegacyFormat = program?.roots != null && program.roots.Length > 0;
        
//         isInitialized = hasMBlockFormat || hasLegacyFormat;
//         Debug.Log($"[RuntimeBlocksRunner] Initialized. IsReady: {isInitialized}, mBlock: {hasMBlockFormat}, Legacy: {hasLegacyFormat}");
//     }
    
//     /// <summary>
//     /// 매 FixedUpdate에서 호출. mBlock 스타일 실행.
//     /// </summary>
//     public void Tick()
//     {
//         if (!isInitialized) return;
        
//         // mBlock 스타일 JSON인 경우
//         if (program.init != null || program.loop != null)
//         {
//             // 1. init 블록: 첫 Tick에서만 실행
//             if (!hasRunInit && program.init != null)
//             {
//                 foreach (var node in program.init)
//                 {
//                     EvalSync(node, null);
//                 }
//                 hasRunInit = true;
//                 Debug.Log($"[RuntimeBlocksRunner] Init blocks executed. Variables: {runtimeVariables.Count}");
//             }
            
//             // 2. loop 블록: 매 Tick마다 실행
//             if (program.loop != null)
//             {
//                 foreach (var node in program.loop)
//                 {
//                     EvalSync(node, null);
//                 }
//             }
//         }
//         // 레거시 roots 형식
//         else if (program.roots != null)
//         {
//             foreach (var node in program.roots)
//             {
//                 EvalSync(node, null);
//             }
//         }
//     }
    
//     /// <summary>
//     /// 프로그램이 로드되었는지 확인
//     /// </summary>
//     public bool IsReady => isInitialized;

//     // ============================================================
//     // 프로그램 로딩
//     // ============================================================
    
//     void LoadProgram()
//     {
//         var path = Path.Combine(Application.persistentDataPath, runtimeJsonFile);
//         if (!File.Exists(path)) 
//         { 
//             Debug.LogWarning($"Runtime program not found: {path}"); 
//             return; 
//         }
        
//         try
//         {
//             var json = File.ReadAllText(path);
//             program = ParseJsonProgram(json);
//             BuildFunctionDefinitions();
            
//             Debug.Log("[RuntimeBlocksRunner] Program loaded successfully.");
//             Debug.Log($"[RuntimeBlocksRunner] Function definitions: {functionDefinitions.Count}");
//         }
//         catch (Exception ex)
//         {
//             Debug.LogError($"[RuntimeBlocksRunner] Failed to parse JSON: {ex.Message}");
//         }
//     }
    
//     void BuildFunctionDefinitions()
//     {
//         functionDefinitions.Clear();
        
//         // mBlock 스타일: functions 배열에서 수집
//         if (program?.functions != null)
//         {
//             foreach (var node in program.functions)
//             {
//                 if (node.type == "functionDefine" && !string.IsNullOrEmpty(node.functionName))
//                 {
//                     functionDefinitions[node.functionName] = node.body ?? new RuntimeBlockNode[0];
//                 }
//             }
//         }
        
//         // 레거시: roots에서 수집
//         if (program?.roots != null)
//         {
//             foreach (var node in program.roots)
//                 CollectFunctionDefinitions(node);
//         }
//     }
    
//     void CollectFunctionDefinitions(RuntimeBlockNode node)
//     {
//         if (node == null) return;
        
//         if (node.type == "functionDefine" && !string.IsNullOrEmpty(node.functionName))
//         {
//             functionDefinitions[node.functionName] = node.body ?? new RuntimeBlockNode[0];
//         }
        
//         if (node.body != null)
//             foreach (var child in node.body)
//                 CollectFunctionDefinitions(child);
        
//         if (node.elseBody != null)
//             foreach (var child in node.elseBody)
//                 CollectFunctionDefinitions(child);
//     }

//     // ============================================================
//     // 동기식 블록 평가 (FixedUpdate 호출용)
//     // ============================================================
    
//     void EvalSync(RuntimeBlockNode n, System.Collections.Generic.Dictionary<string, float> localVars)
//     {
//         if (n == null) return;
        
//         switch (n.type)
//         {
//             case "functionDefine":
//                 // 함수 정의는 실행하지 않음 (호출 시에만 실행)
//                 break;
            
//             case "setVariable":
//                 // mBlock 스타일: 변수 설정
//                 if (!string.IsNullOrEmpty(n.setVarName))
//                 {
//                     runtimeVariables[n.setVarName] = n.setVarValue;
//                     Debug.Log($"[EvalSync] setVariable: {n.setVarName} = {n.setVarValue}");
//                 }
//                 break;
                
//             case "forward":
//                 io.MoveForward(n.number);
//                 break;
                
//             case "turnLeft":
//                 io.TurnLeft(n.number);
//                 break;
                
//             case "turnRight":
//                 io.TurnRight(n.number);
//                 break;
                
//             case "stop":
//                 io.Stop();
//                 break;
                
//             case "analogWrite":
//                 {
//                     float writeValue = n.value;
//                     if (!string.IsNullOrEmpty(n.valueVar) && localVars != null && localVars.TryGetValue(n.valueVar, out float localVal))
//                     {
//                         writeValue = localVal;
//                     }
//                     // 런타임 변수 확인 (mBlock 스타일)
//                     else if (!string.IsNullOrEmpty(n.valueVar) && runtimeVariables.TryGetValue(n.valueVar, out float runtimeVal))
//                     {
//                         writeValue = runtimeVal;
//                     }
//                     Debug.Log($"[EvalSync] analogWrite: pin={n.pin}, value={writeValue}, valueVar={n.valueVar}");
//                     io.AnalogWrite(n.pin, writeValue);
//                 }
//                 break;
                
//             case "digitalRead":
//                 _ = io.DigitalRead(n.pin);
//                 break;
                
//             case "repeat":
//                 for (int i = 0; i < (int)n.number; i++)
//                     EvalListSync(n.body, localVars);
//                 break;
                
//             case "forever":
//                 // forever의 body를 매 Tick마다 한 번씩 실행
//                 EvalListSync(n.body, localVars);
//                 break;
                
//             case "if":
//                 if (io.DigitalRead(n.pin))
//                     EvalListSync(n.body, localVars);
//                 break;
                
//             case "ifElse":
//                 if (io.DigitalRead(n.pin))
//                     EvalListSync(n.body, localVars);
//                 else
//                     EvalListSync(n.elseBody, localVars);
//                 break;
                
//             case "wait":
//                 // FixedUpdate 기반에서는 wait 블록을 무시 (또는 별도 타이머 구현 필요)
//                 // TODO: 필요시 waitTimer 상태 변수로 구현 가능
//                 break;
                
//             case "functionCall":
//                 Debug.Log($"[EvalSync] functionCall: {n.functionName}, arg={n.number}");
//                 if (!string.IsNullOrEmpty(n.functionName) && functionDefinitions.TryGetValue(n.functionName, out var funcBody))
//                 {
//                     var funcLocalVars = new System.Collections.Generic.Dictionary<string, float>
//                     {
//                         { "Speed", n.number }
//                     };
//                     EvalListSync(funcBody, funcLocalVars);
//                 }
//                 else
//                 {
//                     Debug.LogWarning($"[EvalSync] Function not found: {n.functionName}");
//                 }
//                 break;
//         }
//     }
    
//     void EvalListSync(RuntimeBlockNode[] list, System.Collections.Generic.Dictionary<string, float> localVars)
//     {
//         if (list == null) return;
//         foreach (var node in list)
//             EvalSync(node, localVars);
//     }

//     // ============================================================
//     // JSON 파싱 (기존 코드 유지)
//     // ============================================================
    
//     RuntimeBlockProgram ParseJsonProgram(string json)
//     {
//         var program = new RuntimeBlockProgram();
        
//         // mBlock 스타일: functions 배열 파싱
//         int funcIdx = json.IndexOf("\"functions\"");
//         if (funcIdx != -1)
//         {
//             funcIdx = json.IndexOf('[', funcIdx);
//             if (funcIdx != -1)
//                 program.functions = ParseNodeArray(json, ref funcIdx).ToArray();
//         }
        
//         // mBlock 스타일: init 배열 파싱
//         int initIdx = json.IndexOf("\"init\"");
//         if (initIdx != -1)
//         {
//             initIdx = json.IndexOf('[', initIdx);
//             if (initIdx != -1)
//                 program.init = ParseNodeArray(json, ref initIdx).ToArray();
//         }
        
//         // mBlock 스타일: loop 배열 파싱
//         int loopIdx = json.IndexOf("\"loop\"");
//         if (loopIdx != -1)
//         {
//             loopIdx = json.IndexOf('[', loopIdx);
//             if (loopIdx != -1)
//                 program.loop = ParseNodeArray(json, ref loopIdx).ToArray();
//         }
        
//         // 레거시: roots 배열 파싱
//         int rootsIdx = json.IndexOf("\"roots\"");
//         if (rootsIdx != -1)
//         {
//             rootsIdx = json.IndexOf('[', rootsIdx);
//             if (rootsIdx != -1)
//                 program.roots = ParseNodeArray(json, ref rootsIdx).ToArray();
//         }
        
//         return program;
//     }

//     System.Collections.Generic.List<RuntimeBlockNode> ParseNodeArray(string json, ref int idx)
//     {
//         var list = new System.Collections.Generic.List<RuntimeBlockNode>();
//         idx++; // skip [
        
//         while (idx < json.Length)
//         {
//             char c = json[idx];
//             if (c == ']') { idx++; break; }
//             if (c == '{') list.Add(ParseNode(json, ref idx));
//             else idx++;
//         }
//         return list;
//     }

//     RuntimeBlockNode ParseNode(string json, ref int idx)
//     {
//         var node = new RuntimeBlockNode();
//         idx++; 

//         while (idx < json.Length)
//         {
//             char c = json[idx];
//             if (c == '}') { idx++; break; }
//             if (c == '"')
//             {
//                 string key = ParseString(json, ref idx);
                
//                 while (idx < json.Length && json[idx] != ':') idx++;
//                 if (idx >= json.Length) break;
//                 idx++; 
                
//                 while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
//                 if (idx >= json.Length) break;

//                 if (key == "type") node.type = ParseString(json, ref idx);
//                 else if (key == "number") node.number = ParseFloat(json, ref idx);
//                 else if (key == "pin") node.pin = (int)ParseFloat(json, ref idx);
//                 else if (key == "value") node.value = ParseFloat(json, ref idx);
//                 else if (key == "valueVar") node.valueVar = ParseString(json, ref idx);
//                 else if (key == "functionName") node.functionName = ParseString(json, ref idx);
//                 else if (key == "conditionVar") node.conditionVar = ParseString(json, ref idx);
//                 else if (key == "setVarName") node.setVarName = ParseString(json, ref idx);
//                 else if (key == "setVarValue") node.setVarValue = ParseFloat(json, ref idx);
//                 else if (key == "body") 
//                 {
//                     while(idx < json.Length && json[idx] != '[') idx++;
//                     if (idx < json.Length) node.body = ParseNodeArray(json, ref idx).ToArray();
//                 }
//                 else if (key == "elseBody") 
//                 {
//                     while(idx < json.Length && json[idx] != '[') idx++;
//                     if (idx < json.Length) node.elseBody = ParseNodeArray(json, ref idx).ToArray();
//                 }
//                 else SkipValue(json, ref idx);
//             }
//             else idx++;
//         }
//         return node;
//     }

//     string ParseString(string json, ref int idx)
//     {
//         while (idx < json.Length && json[idx] != '"') idx++;
//         if (idx >= json.Length) return "";
        
//         idx++; 
//         int start = idx;
//         while (idx < json.Length && json[idx] != '"') idx++; 
//         if (idx >= json.Length) return "";
        
//         string s = json.Substring(start, idx - start);
//         idx++;
//         return s;
//     }

//     float ParseFloat(string json, ref int idx)
//     {
//         while(idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == ':')) idx++;
//         if (idx >= json.Length) return 0f;
        
//         int start = idx;
//         while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '.' || json[idx] == '-' || json[idx] == 'e' || json[idx] == 'E'))
//             idx++;
        
//         if (start == idx) return 0f;
        
//         string numStr = json.Substring(start, idx - start);
//         float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result);
//         return result;
//     }

//     void SkipValue(string json, ref int idx)
//     {
//         int depth = 0;
//         bool inString = false;
        
//         while (idx < json.Length)
//         {
//             char c = json[idx];
            
//             if (inString)
//             {
//                 if (c == '"' && (idx == 0 || json[idx-1] != '\\')) inString = false;
//                 idx++;
//                 continue;
//             }
            
//             if (c == '"') { inString = true; idx++; continue; }
//             if (c == '{' || c == '[') { depth++; idx++; continue; }
//             if (c == '}' || c == ']') 
//             { 
//                 if (depth > 0) { depth--; idx++; continue; }
//                 return; 
//             }
//             if (c == ',' && depth == 0) return;
            
//             idx++;
//         }
//     }
// }
