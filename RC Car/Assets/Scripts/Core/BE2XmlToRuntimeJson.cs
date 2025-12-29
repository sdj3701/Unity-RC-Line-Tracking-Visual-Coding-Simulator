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
    // 함수 정의를 저장하는 딕셔너리 (functionName -> body nodes)
    private static Dictionary<string, List<RuntimeBlockNode>> functionDefinitions = new Dictionary<string, List<RuntimeBlockNode>>(StringComparer.OrdinalIgnoreCase);
    
    // 변수 값을 저장하는 딕셔너리
    private static Dictionary<string, float> variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

#if UNITY_EDITOR
    [MenuItem("Tools/Blocks/Export BlocksRuntime.json")]
    public static void Export()
    {
        var xmlPath = "Assets/Generated/BlocksGenerated.be2";
        if (!File.Exists(xmlPath))
        {
            Debug.LogWarning($"XML file not found: {xmlPath}");
            return;
        }
        var text = File.ReadAllText(xmlPath);
        Export(text);
    }
#endif

    public static void Export(string xmlText)
    {
        var jsonPath = Path.Combine(Application.persistentDataPath, "BlocksRuntime.json");
        Debug.Log($"[BE2XmlToRuntimeJson] Splitting XML. Total length: {xmlText.Length}");
        
        // 초기화
        functionDefinitions.Clear();
        variables.Clear();
        
        var chunks = xmlText.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
        Debug.Log($"[BE2XmlToRuntimeJson] Chunks found: {chunks.Length}");

        var allBlocks = new List<XElement>();
        
        // 1단계: 모든 청크를 파싱하여 블록 리스트 구성
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            
            XDocument doc;
            try { doc = XDocument.Parse(chunk.Trim()); }
            catch (Exception ex)
            { 
                Debug.LogWarning($"[BE2XmlToRuntimeJson] Failed to parse chunk: {ex.Message}"); 
                continue; 
            }

            allBlocks.Add(doc.Root);
        }
        
        // 2단계: 변수 정의 먼저 처리 (SetVariable 블록들)
        foreach (var block in allBlocks)
        {
            ProcessVariableDefinitions(block);
        }
        Debug.Log($"[BE2XmlToRuntimeJson] Variables found: {variables.Count}");
        foreach (var kv in variables)
        {
            Debug.Log($"  Variable: {kv.Key} = {kv.Value}");
        }
        
        // 3단계: 함수 정의 먼저 수집 (DefineFunction 블록들)
        foreach (var block in allBlocks)
        {
            var name = block.Element("blockName")?.Value?.Trim();
            if (name == "Block Ins DefineFunction")
            {
                ProcessFunctionDefinition(block);
            }
        }
        Debug.Log($"[BE2XmlToRuntimeJson] Function definitions found: {functionDefinitions.Count}");
        foreach (var kv in functionDefinitions)
        {
            Debug.Log($"  Function: {kv.Key} with {kv.Value.Count} body nodes");
        }
        
        // 4단계: Entry Point 블록 찾기 (Forever 블록 또는 실행 가능한 블록들)
        var roots = new List<RuntimeBlockNode>();
        
        foreach (var block in allBlocks)
        {
            var name = block.Element("blockName")?.Value?.Trim();
            
            // 함수 정의는 roots에 포함하지 않음 (호출될 때만 실행)
            if (name == "Block Ins DefineFunction") continue;
            
            // SetVariable은 이미 처리했으므로 건너뜀 (단, OuterArea 연결은 처리해야 함)
            // Forever/Loop 블록을 찾아서 entry point로 처리
            // BE2에서 Forever는 "Block Ins Forever" 또는 "Block Cst Loop"로 표현됨
            if (name == "Block Ins Forever" || name == "Block Cst Loop")
            {
                var node = ProcessBlock(block);
                if (node != null) roots.Add(node);
            }
        }
        
        // Forever 블록이 없으면 다른 실행 가능한 블록들을 찾음
        if (roots.Count == 0)
        {
            Debug.LogWarning("[BE2XmlToRuntimeJson] No Forever block found. Looking for other entry points...");
            foreach (var block in allBlocks)
            {
                var name = block.Element("blockName")?.Value?.Trim();
                if (name == "Block Ins DefineFunction") continue;
                if (name == "Block Ins SetVariable") continue;
                
                var node = ProcessBlock(block);
                if (node != null) roots.Add(node);
            }
        }
        
        // 5단계: 함수 정의를 roots에 추가 (RuntimeBlocksRunner에서 functionCall 시 찾을 수 있도록)
        foreach (var funcDef in functionDefinitions)
        {
            roots.Insert(0, new RuntimeBlockNode
            {
                type = "functionDefine",
                functionName = funcDef.Key,
                body = funcDef.Value.ToArray()
            });
        }

        var program = new RuntimeBlockProgram { roots = roots.ToArray() };
        var json = BuildJson(program);
        File.WriteAllText(jsonPath, json);
        Debug.Log($"[BE2XmlToRuntimeJson] Exported JSON to {jsonPath}");
        Debug.Log($"[BE2XmlToRuntimeJson] JSON content:\n{json}");
    }
    
    /// <summary>
    /// 블록과 그 하위 OuterArea의 모든 SetVariable 블록을 처리하여 변수 사전에 저장
    /// </summary>
    static void ProcessVariableDefinitions(XElement block)
    {
        var name = block.Element("blockName")?.Value?.Trim();
        
        if (name == "Block Ins SetVariable")
        {
            var inputs = block.Descendants("Input").ToList();
            if (inputs.Count >= 2)
            {
                string varName = inputs[0].Element("value")?.Value;
                string valStr = inputs[1].Element("value")?.Value;
                if (!string.IsNullOrEmpty(varName) && float.TryParse(valStr, out var v))
                {
                    variables[varName] = v;
                    Debug.Log($"[BE2XmlToRuntimeJson] SetVariable: {varName} = {v}");
                }
            }
        }
        
        // OuterArea의 childBlocks도 재귀적으로 처리
        var outerChildBlocks = block.Element("OuterArea")?.Element("childBlocks")?.Elements("Block");
        if (outerChildBlocks != null)
        {
            foreach (var child in outerChildBlocks)
            {
                ProcessVariableDefinitions(child);
            }
        }
    }
    
    /// <summary>
    /// 함수 정의 블록을 처리하여 함수 사전에 저장
    /// </summary>
    static void ProcessFunctionDefinition(XElement block)
    {
        var defineID = block.Element("defineID")?.Value?.Trim();
        if (string.IsNullOrEmpty(defineID)) return;
        
        var bodyNodes = new List<RuntimeBlockNode>();
        
        // sections > Section > childBlocks 처리
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
                        var node = ProcessBlock(child);
                        if (node != null) bodyNodes.Add(node);
                    }
                }
            }
        }
        
        functionDefinitions[defineID] = bodyNodes;
        Debug.Log($"[BE2XmlToRuntimeJson] DefineFunction: {defineID} with {bodyNodes.Count} body blocks");
    }
    
    /// <summary>
    /// 블록을 RuntimeBlockNode로 변환
    /// </summary>
    static RuntimeBlockNode ProcessBlock(XElement block)
    {
        if (block == null) return null;
        
        var name = block.Element("blockName")?.Value?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        
        Debug.Log($"[BE2XmlToRuntimeJson] Processing block: {name}");
        
        // Forever/Loop 블록 (BE2에서 "Block Ins Forever" 또는 "Block Cst Loop"로 표현됨)
        if (name == "Block Ins Forever" || name == "Block Cst Loop")
        {
            var bodyNodes = ProcessChildBlocks(block);
            return new RuntimeBlockNode
            {
                type = "forever",
                body = bodyNodes.ToArray()
            };
        }
        
        // If 블록
        if (name == "Block Ins If")
        {
            var condition = ExtractCondition(block);
            var bodyNodes = ProcessChildBlocks(block);
            return new RuntimeBlockNode
            {
                type = "if",
                pin = condition.pin,
                conditionVar = condition.varName,
                body = bodyNodes.ToArray()
            };
        }
        
        // IfElse 블록
        if (name == "Block Ins IfElse")
        {
            var condition = ExtractCondition(block);
            var sections = block.Element("sections")?.Elements("Section").ToList();
            
            var bodyNodes = new List<RuntimeBlockNode>();
            var elseBodyNodes = new List<RuntimeBlockNode>();
            
            if (sections != null && sections.Count > 0)
            {
                // 첫 번째 섹션은 if body
                var ifChildBlocks = sections[0].Element("childBlocks")?.Elements("Block");
                if (ifChildBlocks != null)
                {
                    foreach (var child in ifChildBlocks)
                    {
                        var node = ProcessBlock(child);
                        if (node != null) bodyNodes.Add(node);
                    }
                }
                
                // 두 번째 섹션은 else body
                if (sections.Count > 1)
                {
                    var elseChildBlocks = sections[1].Element("childBlocks")?.Elements("Block");
                    if (elseChildBlocks != null)
                    {
                        foreach (var child in elseChildBlocks)
                        {
                            var node = ProcessBlock(child);
                            if (node != null) elseBodyNodes.Add(node);
                        }
                    }
                }
            }
            
            return new RuntimeBlockNode
            {
                type = "ifElse",
                pin = condition.pin,
                conditionVar = condition.varName,
                body = bodyNodes.ToArray(),
                elseBody = elseBodyNodes.ToArray()
            };
        }
        
        // Repeat 블록
        if (name == "Block Ins Repeat")
        {
            var input = block.Descendants("Input").FirstOrDefault();
            float repeatCount = ResolveFloat(ExtractValue(input));
            var bodyNodes = ProcessChildBlocks(block);
            
            return new RuntimeBlockNode
            {
                type = "repeat",
                number = repeatCount,
                body = bodyNodes.ToArray()
            };
        }
        
        // PWM 블록 (analogWrite)
        if (name == "Block Cst Block_pWM")
        {
            var inputs = block.Descendants("Input")
                              .Where(i => (i.Element("isOperation")?.Value ?? "") == "true")
                              .ToList();
            
            string pinToken = inputs.ElementAtOrDefault(0)?.Descendants("varName").FirstOrDefault()?.Value;
            string valueToken = inputs.ElementAtOrDefault(1)?.Descendants("varName").FirstOrDefault()?.Value
                                ?? inputs.ElementAtOrDefault(1)?.Element("value")?.Value;

            int pin = ResolveInt(pinToken);
            float val = ResolveFloat(valueToken);

            return new RuntimeBlockNode
            {
                type = "analogWrite",
                pin = pin,
                value = val
            };
        }
        
        // Digital Read 블록
        if (name == "Block Cst Block_Read")
        {
            var inputs = block.Descendants("Input")
                              .Where(i => (i.Element("isOperation")?.Value ?? "") == "true")
                              .ToList();
            
            string pinToken = inputs.ElementAtOrDefault(0)?.Descendants("varName").FirstOrDefault()?.Value;
            int pin = ResolveInt(pinToken);
            
            return new RuntimeBlockNode
            {
                type = "digitalRead",
                pin = pin
            };
        }
        
        // 함수 호출 블록
        if (name == "Block Ins FunctionBlock")
        {
            var defineID = block.Element("defineID")?.Value?.Trim();
            
            // 함수 인자 추출
            var inputs = block.Descendants("Input")
                              .Where(i => (i.Element("isOperation")?.Value ?? "") == "true")
                              .ToList();
            
            float arg = 0;
            if (inputs.Count > 0)
            {
                string argToken = inputs[0].Descendants("varName").FirstOrDefault()?.Value
                                  ?? inputs[0].Element("value")?.Value;
                arg = ResolveFloat(argToken);
            }
            
            return new RuntimeBlockNode
            {
                type = "functionCall",
                functionName = defineID,
                number = arg
            };
        }
        
        // Wait 블록
        if (name == "Block Ins Wait")
        {
            var input = block.Descendants("Input").FirstOrDefault();
            float seconds = ResolveFloat(ExtractValue(input));
            
            return new RuntimeBlockNode
            {
                type = "wait",
                number = seconds
            };
        }
        
        // MoveForward 블록
        if (name == "Block Ins MoveForward")
        {
            var input = block.Descendants("Input").FirstOrDefault();
            float speed = ResolveFloat(ExtractValue(input));
            if (speed == 0) speed = 1f;
            
            return new RuntimeBlockNode
            {
                type = "forward",
                number = speed
            };
        }
        
        // TurnDirection 블록
        if (name == "Block Ins TurnDirection")
        {
            var input = block.Descendants("Input").FirstOrDefault();
            string dir = ExtractValue(input);
            
            string nodeType = (dir?.ToLower() == "left") ? "turnLeft" : "turnRight";
            
            return new RuntimeBlockNode
            {
                type = nodeType,
                number = 90f
            };
        }
        
        // Stop 블록
        if (name == "Block Ins Stop")
        {
            return new RuntimeBlockNode
            {
                type = "stop"
            };
        }
        
        Debug.LogWarning($"[BE2XmlToRuntimeJson] Unhandled block type: {name}");
        return null;
    }
    
    /// <summary>
    /// 블록의 sections > Section > childBlocks 내 모든 자식 블록 처리
    /// </summary>
    static List<RuntimeBlockNode> ProcessChildBlocks(XElement block)
    {
        var result = new List<RuntimeBlockNode>();
        
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
                        var node = ProcessBlock(child);
                        if (node != null) result.Add(node);
                    }
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// If 조건 추출 (Block_Read 또는 Variable)
    /// </summary>
    static (int pin, string varName) ExtractCondition(XElement block)
    {
        // sections > Section > inputs > Input > operation > Block 에서 조건 추출
        var input = block.Element("sections")?.Element("Section")?.Element("inputs")?.Element("Input");
        
        if (input != null && input.Element("isOperation")?.Value == "true")
        {
            var opBlock = input.Element("operation")?.Element("Block");
            if (opBlock != null)
            {
                var opBlockName = opBlock.Element("blockName")?.Value?.Trim();
                
                // Block_Read인 경우 - 센서 읽기
                if (opBlockName == "Block Cst Block_Read")
                {
                    var innerInput = opBlock.Descendants("Input")
                                            .FirstOrDefault(i => i.Element("isOperation")?.Value == "true");
                    if (innerInput != null)
                    {
                        var varName = innerInput.Descendants("varName").FirstOrDefault()?.Value;
                        int pin = ResolveInt(varName);
                        return (pin, varName);
                    }
                }
                
                // Variable인 경우
                var directVarName = opBlock.Element("varName")?.Value;
                return (ResolveInt(directVarName), directVarName);
            }
        }
        
        return (0, null);
    }
    
    static string ExtractValue(XElement inputElement)
    {
        if (inputElement == null) return null;
        
        if (inputElement.Element("isOperation")?.Value == "true")
        {
            var opBlock = inputElement.Element("operation")?.Element("Block");
            if (opBlock != null)
            {
                // 먼저 varName 확인
                var varName = opBlock.Element("varName")?.Value;
                if (!string.IsNullOrEmpty(varName)) return varName;
                
                // FunctionLocalVariable인 경우
                var blockName = opBlock.Element("blockName")?.Value?.Trim();
                if (blockName == "Block Op FunctionLocalVariable")
                {
                    return opBlock.Element("varName")?.Value;
                }
            }
        }
        
        return inputElement.Element("value")?.Value;
    }

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
    
    // ===== JSON Building (Pretty Print) =====
    
    static string BuildJson(RuntimeBlockProgram program)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.Append("  \"roots\": ");
        sb.Append(ToJsonArray(program.roots, 1));
        sb.AppendLine();
        sb.Append("}");
        return sb.ToString();
    }

    static string ToJsonArray(RuntimeBlockNode[] nodes, int indent)
    {
        if (nodes == null || nodes.Length == 0) return "[]";
        
        var sb = new System.Text.StringBuilder();
        string indentStr = new string(' ', indent * 2);
        string innerIndent = new string(' ', (indent + 1) * 2);
        
        sb.AppendLine("[");
        for (int i = 0; i < nodes.Length; i++)
        {
            sb.Append(innerIndent);
            sb.Append(ToJsonObject(nodes[i], indent + 1));
            if (i < nodes.Length - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.Append(indentStr);
        sb.Append("]");
        return sb.ToString();
    }

    static string ToJsonObject(RuntimeBlockNode node, int indent)
    {
        if (node == null) return "null";
        
        var sb = new System.Text.StringBuilder();
        string indentStr = new string(' ', indent * 2);
        string innerIndent = new string(' ', (indent + 1) * 2);
        
        sb.AppendLine("{");
        
        // type
        sb.Append(innerIndent);
        sb.Append($"\"type\": \"{EscapeJson(node.type)}\"");
        
        // number (0이 아닌 경우만)
        if (node.number != 0)
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append($"\"number\": {node.number}");
        }
        
        // pin (0이 아닌 경우만)
        if (node.pin != 0)
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append($"\"pin\": {node.pin}");
        }
        
        // value (0이 아닌 경우만)
        if (node.value != 0)
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append($"\"value\": {node.value}");
        }
        
        // functionName
        if (!string.IsNullOrEmpty(node.functionName))
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append($"\"functionName\": \"{EscapeJson(node.functionName)}\"");
        }
        
        // conditionVar
        if (!string.IsNullOrEmpty(node.conditionVar))
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append($"\"conditionVar\": \"{EscapeJson(node.conditionVar)}\"");
        }

        // body
        if (node.body != null && node.body.Length > 0)
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append("\"body\": ");
            sb.Append(ToJsonArray(node.body, indent + 1));
        }

        // elseBody
        if (node.elseBody != null && node.elseBody.Length > 0)
        {
            sb.AppendLine(",");
            sb.Append(innerIndent);
            sb.Append("\"elseBody\": ");
            sb.Append(ToJsonArray(node.elseBody, indent + 1));
        }
        
        sb.AppendLine();
        sb.Append(indentStr);
        sb.Append("}");
        return sb.ToString();
    }
    
    static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
