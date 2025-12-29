// Assets/Editor/BE2XmlToRuntimeJson.cs
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
        var chunks = xmlText.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);

        var vars = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<RuntimeBlockNode>();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            
            XDocument doc;
            try { doc = XDocument.Parse(chunk.Trim()); }
            catch { continue; }

            var block = doc.Root;
            var name = block.Element("blockName")?.Value?.Trim();

            // 1) 변수 정의: <Block Ins SetVariable>
            if (name == "Block Ins SetVariable")
            {
                var inputs = block.Descendants("Input").ToList();
                if (inputs.Count >= 2)
                {
                    string key = inputs[0].Element("value")?.Value;
                    string valStr = inputs[1].Element("value")?.Value;
                    if (!string.IsNullOrEmpty(key) && float.TryParse(valStr, out var v))
                        vars[key] = v;
                }
                continue;
            }

            // 2) PWM 블록 → analogWrite 노드
            if (name == "Block Cst Block_pWM")
            {
                var inputs = block.Descendants("Input")
                                  .Where(i => (i.Element("isOperation")?.Value ?? "") == "true")
                                  .ToList();
                string pinToken = inputs.ElementAtOrDefault(0)?.Descendants("varName").FirstOrDefault()?.Value;
                string valueToken = inputs.ElementAtOrDefault(1)?.Descendants("varName").FirstOrDefault()?.Value
                                    ?? inputs.ElementAtOrDefault(1)?.Element("value")?.Value;

                int pin = ResolveInt(pinToken, vars);
                float val = ResolveFloat(valueToken, vars);

                roots.Add(new RuntimeBlockNode
                {
                    type = "analogWrite",
                    pin = pin,
                    value = val
                });
                continue;
            }

            // 3) 이동(Move Forward) 블록
            if (name == "Block Ins MoveForward")
            {
                // 입력값 파싱 (Section 0)
                // 보통 BE2에서 Inputs[0]가 속도/거리 값
                var input = block.Descendants("Input").FirstOrDefault();
                string valStr = ExtractValue(input);
                float val = ResolveFloat(valStr, vars);
                
                // 만약 값이 없으면 기본값 (예: 1.0)
                if (val == 0 && string.IsNullOrEmpty(valStr)) val = 1f; 

                roots.Add(new RuntimeBlockNode
                {
                    type = "forward",
                    number = val
                });
                continue;
            }

            // 4) 회전(Turn) 블록
            if (name == "Block Ins TurnDirection")
            {
                var input = block.Descendants("Input").FirstOrDefault();
                string dir = ExtractValue(input); // "Left" or "Right"

                string nodeType = (dir == "Left") ? "turnLeft" : "turnRight";
                float angle = 90f; // 기본 90도 회전이라 가정

                roots.Add(new RuntimeBlockNode
                {
                    type = nodeType,
                    number = angle
                });
                continue;
            }
        }

        var program = new RuntimeBlockProgram { roots = roots.ToArray() };
        var json = BuildJson(program);
        File.WriteAllText(jsonPath, json);
        Debug.Log($"[BE2XmlToRuntimeJson] Exported JSON to {jsonPath}");
    }

    static string BuildJson(RuntimeBlockProgram program)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"roots\":");
        sb.Append(ToJsonArray(program.roots));
        sb.Append("}");
        return sb.ToString();
    }

    static string ToJsonArray(RuntimeBlockNode[] nodes)
    {
        if (nodes == null || nodes.Length == 0) return "[]";
        var sb = new System.Text.StringBuilder();
        sb.Append("[");
        for (int i = 0; i < nodes.Length; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append(ToJsonObject(nodes[i]));
        }
        sb.Append("]");
        return sb.ToString();
    }

    static string ToJsonObject(RuntimeBlockNode node)
    {
        if (node == null) return "null";
        var sb = new System.Text.StringBuilder();
        sb.Append("{");
        sb.Append($"\"type\":\"{node.type}\"");
        sb.Append($",\"number\":{node.number}");
        sb.Append($",\"pin\":{node.pin}");
        sb.Append($",\"value\":{node.value}");
        
        if (node.body != null && node.body.Length > 0)
        {
            sb.Append(",\"body\":");
            sb.Append(ToJsonArray(node.body));
        }

        if (node.elseBody != null && node.elseBody.Length > 0)
        {
            sb.Append(",\"elseBody\":");
            sb.Append(ToJsonArray(node.elseBody));
        }
        
        sb.Append("}");
        return sb.ToString();
    }

    static string ExtractValue(XElement inputElement)
    {
        if (inputElement == null) return null;
        if (inputElement.Element("isOperation")?.Value == "true")
        {
            var opBlock = inputElement.Element("operation")?.Element("Block");
            if (opBlock != null)
            {
                 var varName = opBlock.Element("varName")?.Value;
                 if (!string.IsNullOrEmpty(varName)) return varName;
            }
        }
        return inputElement.Element("value")?.Value;
    }

    static int ResolveInt(string token, Dictionary<string, float> vars)
    {
        if (string.IsNullOrEmpty(token)) return 0;
        if (vars.TryGetValue(token, out var v)) return Mathf.RoundToInt(v);
        int.TryParse(token, out var i);
        return i;
    }

    static float ResolveFloat(string token, Dictionary<string, float> vars)
    {
        if (string.IsNullOrEmpty(token)) return 0f;
        if (vars.TryGetValue(token, out var v)) return v;
        float.TryParse(token, out var f);
        return f;
    }
}
