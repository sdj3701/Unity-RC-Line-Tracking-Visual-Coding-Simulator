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
        
        Debug.Log($"[BE2XmlToRuntimeJson] Starting export...");
        Debug.Log($"[BE2XmlToRuntimeJson] XML length: {xmlText.Length}");
        
        var chunks = xmlText.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
        Debug.Log($"[BE2XmlToRuntimeJson] Found {chunks.Length} chunks");

        var allBlocks = new List<XElement>();
        
        // 1단계: 모든 청크를 파싱하여 블록 리스트 구성
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            
            XDocument doc;
            try 
            { 
                doc = XDocument.Parse(chunk.Trim()); 
                Debug.Log($"[BE2XmlToRuntimeJson] Parsed block: {doc.Root?.Element("blockName")?.Value}");
            }
            catch (Exception ex)
            { 
                Debug.LogWarning($"[BE2XmlToRuntimeJson] Failed to parse chunk: {ex.Message}"); 
                Debug.LogWarning($"[BE2XmlToRuntimeJson] Chunk content: {chunk.Substring(0, Math.Min(200, chunk.Length))}...");
                continue; 
            }

            allBlocks.Add(doc.Root);
        }
        
        Debug.Log($"[BE2XmlToRuntimeJson] Total parsed blocks: {allBlocks.Count}");
        
        // 2단계: 변수 정의 처리 (SetVariable 블록들)
        var initBlocks = new List<VariableNode>();
        
        foreach (var block in allBlocks)
        {
            ProcessVariableDefinitions(block, initBlocks);
        }
        
        Debug.Log($"[BE2XmlToRuntimeJson] Variables found: {initBlocks.Count}");
        foreach (var v in initBlocks)
        {
            Debug.Log($"  - {v.name} = {v.value}");
        }

        // JSON 빌드 (변수만)
        var json = BuildJsonVariablesOnly(initBlocks);
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
        Debug.Log($"[BE2XmlToRuntimeJson] Processing block: {name}");
        
        if (name == "Block Ins SetVariable")
        {
            var inputs = block.Descendants("Input").ToList();
            Debug.Log($"[BE2XmlToRuntimeJson] SetVariable has {inputs.Count} inputs");
            
            if (inputs.Count >= 2)
            {
                string varName = inputs[0].Element("value")?.Value;
                string valStr = inputs[1].Element("value")?.Value;
                
                Debug.Log($"[BE2XmlToRuntimeJson] SetVariable: varName={varName}, valStr={valStr}");
                
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
                    
                    Debug.Log($"[BE2XmlToRuntimeJson] SetVariable (headerInputs): varName={varName}, valStr={valStr}");
                    
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
    
    // ===== JSON Building (변수만) =====
    
    static string BuildJsonVariablesOnly(List<VariableNode> variables)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        
        // init 배열 (변수 설정만)
        sb.AppendLine("  \"init\": [");
        for (int i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            sb.Append($"    {{ \"type\": \"setVariable\", \"setVarName\": \"{EscapeJson(v.name)}\", \"setVarValue\": {v.value} }}");
            if (i < variables.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ],");
        
        // 빈 배열들 (나중에 채울 예정)
        sb.AppendLine("  \"loop\": [],");
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
