// Assets/Editor/BE2XmlToRuntimeJson.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

public static class BE2XmlToRuntimeJson
{
    [MenuItem("Tools/Blocks/Export BlocksRuntime.json")]
    public static void Export()
    {
        var xmlPath = "Assets/Generated/BlocksGenerated.be2"; // BE2가 내보낸 XML
        var jsonPath = Path.Combine(Application.persistentDataPath, "BlocksRuntime.json");

        var text = File.ReadAllText(xmlPath);
        var chunks = text.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);

        var vars = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<RuntimeBlockNode>();

        foreach (var chunk in chunks)
        {
            var doc = XDocument.Parse(chunk.Trim());
            var block = doc.Root;
            var name = block.Element("blockName")?.Value?.Trim();

            // 변수 정의: <Block Ins SetVariable>
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

            // PWM 블록 → analogWrite 노드
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
            }

            // 여기에 다른 블록 이름→RuntimeBlockNode 변환 로직을 추가하세요.
        }

        var program = new RuntimeBlockProgram { roots = roots.ToArray() };
        File.WriteAllText(jsonPath, JsonUtility.ToJson(program, true));
        Debug.Log($"Exported BlocksRuntime.json => {jsonPath}");
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
