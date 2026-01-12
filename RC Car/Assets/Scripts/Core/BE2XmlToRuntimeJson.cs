// Assets/Scripts/Core/BE2XmlToRuntimeJson.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public static class BE2XmlToRuntimeJson
{
    // 변수 값을 저장하는 딕셔너리
    private static Dictionary<string, float> variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    public static void Export(string xmlText)
    {
        var jsonPath = Path.Combine(Application.persistentDataPath, "BlocksRuntime.json");
        
        // 초기화
        variables.Clear();
                
        var chunks = xmlText.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);

        var allBlocks = new List<XElement>();
        
        // 1단계: 모든 청크를 파싱하여 블록 리스트 구성
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            
            XDocument doc;
            try 
            { 
                doc = XDocument.Parse(chunk.Trim()); 
            }
            catch (Exception ex)
            { 
                Debug.LogWarning($"[BE2XmlToRuntimeJson] Failed to parse chunk: {ex.Message}"); 
                Debug.LogWarning($"[BE2XmlToRuntimeJson] Chunk content: {chunk.Substring(0, Math.Min(200, chunk.Length))}...");
                continue; 
            }

            allBlocks.Add(doc.Root);
        }
                
        // 2단계: 변수 정의 처리 (SetVariable 블록들)
        var initBlocks = new List<VariableNode>();
        
        foreach (var block in allBlocks)
        {
            ProcessVariableDefinitions(block, initBlocks);
        }
        
        foreach (var v in initBlocks)
        {
            Debug.Log($"  - {v.name} = {v.value}");
        }
        
        // 3단계: PWM 블록 처리
        var pwmBlocks = new List<PwmNode>();
        foreach (var block in allBlocks)
        {
            ProcessPWM(block, pwmBlocks);
        }
        
        foreach (var p in pwmBlocks)
        {
            Debug.Log($"  - pin={p.pin}, value={p.value}, valueVar={p.valueVar}");
        }

        // JSON 빌드
        var json = BuildJson(initBlocks, pwmBlocks);
        File.WriteAllText(jsonPath, json);
        
        Debug.Log($"[BE2XmlToRuntimeJson] Exported to: {jsonPath}");
        Debug.Log($"[BE2XmlToRuntimeJson] JSON content:\n{json}");
    }
    
    /// <summary>
    /// 블록과 그 하위 OuterArea의 모든 SetVariable 블록을 처리하여 변수 리스트에 저장
    /// </summary>
    static void ProcessVariableDefinitions(XElement block, List<VariableNode> initBlocks)
    {
        var name = block.Element("blockName")?.Value?.Trim();
        
        if (name == "Block Ins SetVariable")
        {
            var inputs = block.Descendants("Input").ToList();
            
            if (inputs.Count >= 2)
            {
                string varName = inputs[0].Element("value")?.Value;
                string valStr = inputs[1].Element("value")?.Value;
                                
                if (!string.IsNullOrEmpty(varName))
                {
                    float.TryParse(valStr, out var v);
                    variables[varName] = v;
                    initBlocks.Add(new VariableNode { name = varName, value = v });
                }
            }
            else
            {
                // 다른 방식으로 시도 - headerInputs 확인
                var headerInputs = block.Element("headerInputs")?.Elements("Input").ToList();
                if (headerInputs != null && headerInputs.Count >= 2)
                {
                    string varName = headerInputs[0].Element("value")?.Value;
                    string valStr = headerInputs[1].Element("value")?.Value;
                                        
                    if (!string.IsNullOrEmpty(varName))
                    {
                        float.TryParse(valStr, out var v);
                        variables[varName] = v;
                        initBlocks.Add(new VariableNode { name = varName, value = v });
                    }
                }
            }
        }
        
        // OuterArea의 childBlocks도 재귀적으로 처리
        var outerChildBlocks = block.Element("OuterArea")?.Element("childBlocks")?.Elements("Block");
        if (outerChildBlocks != null)
        {
            foreach (var child in outerChildBlocks)
            {
                ProcessVariableDefinitions(child, initBlocks);
            }
        }
        
        // sections의 childBlocks도 재귀적으로 처리
        var sections = block.Element("sections")?.Elements("Section");
        if (sections != null)
        {
            foreach (var section in sections)
            {
                var childBlocks = section.Element("childBlocks")?.Elements("Block");
                if (childBlocks != null)
                {
                    foreach (var child in childBlocks)
                    {
                        ProcessVariableDefinitions(child, initBlocks);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// PWM 블록 처리
    /// </summary>
    static void ProcessPWM(XElement block, List<PwmNode> pwmBlocks)
    {
        var name = block.Element("blockName")?.Value?.Trim();
        
        if (name == "Block Cst Block_pWM")
        {
            // 모든 Input 찾기 (직접 자식만)
            var allInputs = block.Element("headerInputs")?.Elements("Input").ToList() 
                           ?? block.Descendants("Input").ToList();
            
            int pin = 0;
            float value = 0;
            string valueVar = null;
            
            // 두 번째 Input (인덱스 1): 핀 번호
            if (allInputs.Count >= 2)
            {
                var pinInput = allInputs[1];
                var pinVarName = pinInput.Descendants("varName").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(pinVarName))
                {
                    pin = ResolveInt(pinVarName);
                }
                else
                {
                    var pinValue = pinInput.Element("value")?.Value;
                    pin = ResolveInt(pinValue);
                }
            }
            
            // 세 번째 Input (인덱스 2): 값
            if (allInputs.Count >= 3)
            {
                var valueInput = allInputs[2];
                
                // value 요소에서 직접 값 가져오기
                var directValue = valueInput.Element("value")?.Value;
                if (!string.IsNullOrEmpty(directValue))
                {
                    value = ResolveFloat(directValue);
                }
                else
                {
                    // operation 블록에서 변수 참조 확인
                    var opBlock = valueInput.Element("operation")?.Element("Block");
                    if (opBlock != null)
                    {
                        valueVar = opBlock.Element("varName")?.Value;
                    }
                }
            }
            
            pwmBlocks.Add(new PwmNode { pin = pin, value = value, valueVar = valueVar });
        }
        
        // 재귀 처리: OuterArea
        var outerChildBlocks = block.Element("OuterArea")?.Element("childBlocks")?.Elements("Block");
        if (outerChildBlocks != null)
        {
            foreach (var child in outerChildBlocks)
                ProcessPWM(child, pwmBlocks);
        }
        
        // 재귀 처리: sections
        var sections = block.Element("sections")?.Elements("Section");
        if (sections != null)
        {
            foreach (var section in sections)
            {
                var childBlocks = section.Element("childBlocks")?.Elements("Block");
                if (childBlocks != null)
                {
                    foreach (var child in childBlocks)
                        ProcessPWM(child, pwmBlocks);
                }
            }
        }
    }
    
    // ===== 헬퍼 함수 =====
    
    static int ResolveInt(string token)
    {
        if (string.IsNullOrEmpty(token)) return 0;
        if (variables.TryGetValue(token, out var v)) return Mathf.RoundToInt(v);
        int.TryParse(token, out var i);
        return i;
    }
    
    static float ResolveFloat(string token)
    {
        if (string.IsNullOrEmpty(token)) return 0f;
        if (variables.TryGetValue(token, out var v)) return v;
        float.TryParse(token, out var f);
        return f;
    }
    
    // ===== JSON Building =====
    
    static string BuildJson(List<VariableNode> variables, List<PwmNode> pwmBlocks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        
        // init 배열 (변수 설정)
        sb.AppendLine("  \"init\": [");
        for (int i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            sb.Append($"    {{ \"type\": \"setVariable\", \"setVarName\": \"{EscapeJson(v.name)}\", \"setVarValue\": {v.value} }}");
            if (i < variables.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ],");
        
        // loop 배열 (PWM 등 실행 블록)
        sb.AppendLine("  \"loop\": [");
        for (int i = 0; i < pwmBlocks.Count; i++)
        {
            var p = pwmBlocks[i];
            var parts = new List<string>();
            parts.Add($"\"type\": \"analogWrite\"");
            parts.Add($"\"pin\": {p.pin}");
            if (!string.IsNullOrEmpty(p.valueVar))
                parts.Add($"\"valueVar\": \"{EscapeJson(p.valueVar)}\"");
            else
                parts.Add($"\"value\": {p.value}");
            
            sb.Append($"    {{ {string.Join(", ", parts)} }}");
            if (i < pwmBlocks.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ],");
        
        // functions 배열 (빈 배열)
        sb.AppendLine("  \"functions\": []");
        
        sb.Append("}");
        return sb.ToString();
    }
    
    static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
    
    class VariableNode
    {
        public string name;
        public float value;
    }
    
    class PwmNode
    {
        public int pin;
        public float value;
        public string valueVar;
    }
}

/* ============================================================
 * 주석 처리된 기능들 (나중에 복원 예정)
 * ============================================================
 * 
 * - 함수 정의 처리 (DefineFunction)
 * - Forever/Loop 블록
 * - If/IfElse 블록
 * - Repeat 블록  
 * - PWM/analogWrite 블록
 * - Digital Read 블록
 * - 함수 호출 블록
 * - Wait 블록
 * - MoveForward 블록
 * - TurnDirection 블록
 * - Stop 블록
 * 
 * ============================================================ */
