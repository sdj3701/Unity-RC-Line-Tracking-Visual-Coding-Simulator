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

        // WhenPlayClicked 블록이 포함된 청크만 찾기
        XElement mainTriggerBlock = null;
        
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

            var blockName = doc.Root?.Element("blockName")?.Value?.Trim();
            
            // WhenPlayClicked 블록 찾기
            if (blockName == "Block Ins WhenPlayClicked" || blockName == "Block Cst WhenPlayClicked")
            {
                mainTriggerBlock = doc.Root;
                Debug.Log($"[BE2XmlToRuntimeJson] Found main trigger block: {blockName}");
                break;
            }
        }
        
        if (mainTriggerBlock == null)
        {
            Debug.LogWarning("[BE2XmlToRuntimeJson] WhenPlayClicked block not found! No blocks will be exported.");
            // 빈 JSON 생성
            var emptyJson = BuildJson(new List<VariableNode>(), new List<LoopBlockNode>());
            File.WriteAllText(jsonPath, emptyJson);
            return;
        }
                
        // 2단계: WhenPlayClicked과 연결된 변수 정의 처리 (SetVariable 블록들)
        var initBlocks = new List<VariableNode>();
        ProcessVariableDefinitions(mainTriggerBlock, initBlocks);
        
        foreach (var v in initBlocks)
        {
            Debug.Log($"  - {v.name} = {v.value}");
        }
        
        // 3단계: WhenPlayClicked과 연결된 Loop 블록 처리
        var loopBlocks = new List<LoopBlockNode>();
        ProcessLoopBlocks(mainTriggerBlock, loopBlocks);
        
        Debug.Log($"[BE2XmlToRuntimeJson] Loop blocks: {loopBlocks.Count}");

        // JSON 빌드
        var json = BuildJson(initBlocks, loopBlocks);
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
    
    // ============================================================
    // Loop 블록 처리 (모든 블록을 재귀적으로 순회)
    // ============================================================
    
    static void ProcessLoopBlocks(XElement block, List<LoopBlockNode> loopBlocks)
    {
        // 현재 블록 처리 시도
        var node = ParseBlockToLoopNode(block);
        if (node != null)
        {
            loopBlocks.Add(node);
            // If 블록 내부는 ParseBlockToLoopNode에서 이미 처리하므로 여기서는 스킵
            // (중복 방지)
        }
        else
        {
            // 현재 블록이 Loop 노드가 아닌 경우 (변수 정의, 트리거 등)
            // 내부 섹션 재귀 처리
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
                            ProcessLoopBlocks(child, loopBlocks);
                        }
                    }
                }
            }
        }
        
        // OuterArea 재귀 처리 (다음 블록으로 이동)
        var outerChildBlocks = block.Element("OuterArea")?.Element("childBlocks")?.Elements("Block");
        if (outerChildBlocks != null)
        {
            foreach (var child in outerChildBlocks)
                ProcessLoopBlocks(child, loopBlocks);
        }
    }
    
    /// <summary>
    /// 단일 블록을 LoopBlockNode로 변환
    /// </summary>
    static LoopBlockNode ParseBlockToLoopNode(XElement block)
    {
        var name = block.Element("blockName")?.Value?.Trim();
        
        // PWM 블록
        if (name == "Block Cst Block_pWM")
        {
            return ParsePwmBlock(block);
        }
        
        // If 블록
        if (name == "Block Cst If" || name == "Block Ins If")
        {
            return ParseIfBlock(block, "if");
        }
        
        // IfElse 블록
        if (name == "Block Cst IfElse" || name == "Block Ins IfElse")
        {
            return ParseIfBlock(block, "ifElse");
        }
        
        return null;
    }
    
    static LoopBlockNode ParsePwmBlock(XElement block)
    {
        var node = new LoopBlockNode { type = "analogWrite" };
        
        var allInputs = block.Element("headerInputs")?.Elements("Input").ToList() 
                       ?? block.Descendants("Input").ToList();
        
        // 핀 번호 (인덱스 1)
        if (allInputs.Count >= 2)
        {
            var pinInput = allInputs[1];
            var pinVarName = pinInput.Descendants("varName").FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(pinVarName))
                node.pin = ResolveInt(pinVarName);
            else
                node.pin = ResolveInt(pinInput.Element("value")?.Value);
        }
        
        // 값 (인덱스 2)
        if (allInputs.Count >= 3)
        {
            var valueInput = allInputs[2];
            var directValue = valueInput.Element("value")?.Value;
            if (!string.IsNullOrEmpty(directValue))
                node.value = ResolveFloat(directValue);
            else
            {
                var opBlock = valueInput.Element("operation")?.Element("Block");
                if (opBlock != null)
                    node.valueVar = opBlock.Element("varName")?.Value;
            }
        }
        
        return node;
    }
    
    static LoopBlockNode ParseIfBlock(XElement block, string type)
    {
        var node = new LoopBlockNode { type = type };
        
        // 1. headerInputs에서 conditionPin 추출 (digitalRead 블록)
        var headerInputs = block.Element("headerInputs")?.Elements("Input").ToList();
        if (headerInputs != null && headerInputs.Count > 0)
        {
            var opBlock = headerInputs[0].Element("operation")?.Element("Block");
            if (opBlock != null)
            {
                var opName = opBlock.Element("blockName")?.Value?.Trim();
                if (opName != null && opName.Contains("digitalRead"))
                {
                    // digitalRead 블록에서 핀 추출
                    var pinVarName = opBlock.Element("varName")?.Value;
                    if (!string.IsNullOrEmpty(pinVarName))
                    {
                        node.conditionPin = ResolveInt(pinVarName);
                    }
                    else
                    {
                        // 내부 inputs에서 핀 값 추출
                        var innerInputs = opBlock.Descendants("Input").ToList();
                        if (innerInputs.Count > 0)
                        {
                            var innerOpBlock = innerInputs[0].Element("operation")?.Element("Block");
                            if (innerOpBlock != null)
                            {
                                var innerVarName = innerOpBlock.Element("varName")?.Value;
                                node.conditionPin = ResolveInt(innerVarName);
                            }
                            else
                            {
                                node.conditionPin = ResolveInt(innerInputs[0].Element("value")?.Value);
                            }
                        }
                    }
                }
            }
        }
        
        // 2. sections 처리 - body와 conditionValue 추출
        var sections = block.Element("sections")?.Elements("Section").ToList();
        if (sections != null)
        {
            // then body (첫 번째 섹션)
            if (sections.Count > 0)
            {
                var firstSection = sections[0];
                
                // childBlocks에서 body 추출
                node.body = ParseSectionBlocks(firstSection);
                
                // inputs에서 conditionValue 추출 (childBlocks 이후에 있는 값)
                var sectionInputs = firstSection.Element("inputs")?.Elements("Input").ToList();
                if (sectionInputs != null && sectionInputs.Count > 0)
                {
                    var lastInput = sectionInputs[sectionInputs.Count - 1];
                    var valueStr = lastInput.Element("value")?.Value;
                    if (!string.IsNullOrEmpty(valueStr))
                    {
                        node.conditionValue = ResolveInt(valueStr);
                        Debug.Log($"[ParseIfBlock] conditionValue extracted: {node.conditionValue}");
                    }
                }
            }
            
            // else body (두 번째 섹션) - ifElse만
            if (type == "ifElse" && sections.Count > 1)
            {
                node.elseBody = ParseSectionBlocks(sections[1]);
            }
        }
        
        return node;
    }
    
    static List<LoopBlockNode> ParseSectionBlocks(XElement section)
    {
        var result = new List<LoopBlockNode>();
        var childBlocks = section.Element("childBlocks")?.Elements("Block");
        if (childBlocks == null) return result;
        
        foreach (var child in childBlocks)
        {
            var node = ParseBlockToLoopNode(child);
            if (node != null)
                result.Add(node);
        }
        return result;
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
    
    static string BuildJson(List<VariableNode> variables, List<LoopBlockNode> loopBlocks)
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
        
        // loop 배열 (순서대로)
        sb.AppendLine("  \"loop\": [");
        for (int i = 0; i < loopBlocks.Count; i++)
        {
            sb.Append("    ");
            sb.Append(LoopBlockNodeToJson(loopBlocks[i]));
            if (i < loopBlocks.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ],");
        
        // functions 배열 (빈 배열)
        sb.AppendLine("  \"functions\": []");
        
        sb.Append("}");
        return sb.ToString();
    }
    
    static string LoopBlockNodeToJson(LoopBlockNode node)
    {
        var parts = new List<string>();
        parts.Add($"\"type\": \"{node.type}\"");
        
        switch (node.type)
        {
            case "analogWrite":
                parts.Add($"\"pin\": {node.pin}");
                if (!string.IsNullOrEmpty(node.valueVar))
                    parts.Add($"\"valueVar\": \"{EscapeJson(node.valueVar)}\"");
                else
                    parts.Add($"\"value\": {node.value}");
                break;
                
            case "if":
            case "ifElse":
                parts.Add($"\"conditionPin\": {node.conditionPin}");
                parts.Add($"\"conditionValue\": {node.conditionValue}");
                
                // body
                var bodyParts = new List<string>();
                if (node.body != null)
                {
                    foreach (var b in node.body)
                        bodyParts.Add(LoopBlockNodeToJson(b));
                }
                parts.Add($"\"body\": [{string.Join(", ", bodyParts)}]");
                
                // elseBody (ifElse만)
                if (node.type == "ifElse")
                {
                    var elseParts = new List<string>();
                    if (node.elseBody != null)
                    {
                        foreach (var b in node.elseBody)
                            elseParts.Add(LoopBlockNodeToJson(b));
                    }
                    parts.Add($"\"elseBody\": [{string.Join(", ", elseParts)}]");
                }
                break;
        }
        
        return $"{{ {string.Join(", ", parts)} }}";
    }
    
    static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
    
    // ===== Data Classes =====
    
    class VariableNode
    {
        public string name;
        public float value;
    }
    
    class LoopBlockNode
    {
        public string type;        // "analogWrite", "if", "ifElse"
        
        // For analogWrite
        public int pin;
        public float value;
        public string valueVar;
        
        // For if/ifElse
        public int conditionPin;
        public int conditionValue;  // Section의 inputs에서 추출한 조건 값
        public List<LoopBlockNode> body;
        public List<LoopBlockNode> elseBody;
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
