using System;
using System.IO;
using System.Text;
using UnityEngine;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.Block.Instruction;
using MG_BlocksEngine2.Serializer;

public class BE2_CodeExporter : MonoBehaviour
{
    // 변수 선언 여부를 추적하기 위한 집합 (중복 선언 방지)
    System.Collections.Generic.HashSet<string> _declaredVars = new System.Collections.Generic.HashSet<string>();
    public string LastSavedPath;
    public string GenerateCSharpFromAllEnvs()
    {
        var envs = GameObject.FindObjectsOfType<MG_BlocksEngine2.Environment.BE2_ProgrammingEnv>();
        var sb = new StringBuilder();
        for (int i = 0; i < envs.Length; i++)
        {
            var env = envs[i];
            if (env == null) continue;
            env.UpdateBlocksList();
            var blocks = env.BlocksList;
            if (blocks == null) continue;
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (block == null) continue;
                sb.Append(GenerateForBlock(block, 0));
            }
        }
        return sb.ToString();
    }

    public bool SaveXmlToAssets(MG_BlocksEngine2.Environment.I_BE2_ProgrammingEnv targetEnv, string relativeAssetPath = "Assets/Generated/BlocksGenerated.be2")
    {
        if (targetEnv == null) return false;
        string xml = BE2_BlocksSerializer.BlocksCodeToXML(targetEnv);
        if (string.IsNullOrEmpty(xml)) return false;

        string fullPath;
        bool isPlayMode = Application.isPlaying;
        if (isPlayMode)
        {
            string fileName = Path.GetFileName(relativeAssetPath);
            if (string.IsNullOrEmpty(fileName)) fileName = "BlocksGenerated.be2";
            var nonAssetsDir = Path.Combine(Application.persistentDataPath, "Generated");
            if (!Directory.Exists(nonAssetsDir)) Directory.CreateDirectory(nonAssetsDir);
            fullPath = Path.Combine(nonAssetsDir, fileName);
        }
        else
        {
            fullPath = relativeAssetPath;
            if (!Path.IsPathRooted(fullPath))
            {
                if (relativeAssetPath.StartsWith("Assets/") || relativeAssetPath.StartsWith("Assets\\"))
                {
                    string sub = relativeAssetPath.Substring(7);
                    fullPath = Path.Combine(Application.dataPath, sub);
                }
                else
                {
                    fullPath = Path.Combine(Application.dataPath, relativeAssetPath);
                }
            }
            var dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, xml);
        LastSavedPath = fullPath;
        Debug.Log($"[BE2_CodeExporter] Saved generated blocks XML (single env) to: {fullPath} (PlayMode={isPlayMode})");

#if UNITY_EDITOR
        if (isPlayMode)
        {
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastTempXmlPath", fullPath);
            string relXml = relativeAssetPath;
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastRelXmlAssetPath", relXml);
        }
        else
        {
            UnityEditor.AssetDatabase.Refresh();
        }
#endif
        return true;
    }

    string GenerateXmlFromAllEnvs()
    {
        var envs = GameObject.FindObjectsOfType<MG_BlocksEngine2.Environment.BE2_ProgrammingEnv>();
        var sb = new StringBuilder();
        Debug.Log($"[BE2_CodeExporter] XML export scanning envs: {envs.Length}");
        for (int i = 0; i < envs.Length; i++)
        {
            var env = envs[i];
            if (env == null) continue;
            env.UpdateBlocksList();
            int count = env.BlocksList != null ? env.BlocksList.Count : 0;
            Debug.Log($"[BE2_CodeExporter] Env '{env.name}' has top-level blocks: {count}");
            sb.Append(BE2_BlocksSerializer.BlocksCodeToXML(env));
            if (i < envs.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    string Indent(int level)
    {
        if (level <= 0) return string.Empty;
        return new string(' ', level * 4);
    }

    string GenerateForBlock(I_BE2_Block block, int indent)
    {
        var ins = block.Instruction;
        if (ins == null) return string.Empty;
        var baseIns = block.Transform.GetComponent<I_BE2_InstructionBase>();
        var type = ins.GetType().Name;
        var sb = new StringBuilder();
        switch (type)
        {
            case nameof(BE2_Ins_MoveForward):
            {
                // 이동 블록: 입력값이 블록(변수/연산)일 수 있으므로 표현식으로 변환
                var inputs = baseIns.Section0Inputs;
                string expr = inputs != null && inputs.Length > 0 ? BuildValueExpression(inputs[0]) : "0f";
                sb.AppendLine(Indent(indent) + "transform.position += transform.forward * (" + expr + ");");
                break;
            }
            case nameof(BE2_Ins_TurnDirection):
            {
                // 회전 방향 블록: 조건식 안에 변수/연산 사용 가능하도록 표현식 생성 후 비교
                var inputs = baseIns.Section0Inputs;
                string dirExpr = inputs != null && inputs.Length > 0 ? BuildValueExpression(inputs[0]) : QuoteString("");
                string dirVar = "__dir" + indent;
                sb.AppendLine(Indent(indent) + "var " + dirVar + " = " + dirExpr + ";");
                sb.AppendLine(Indent(indent) + "if (" + dirVar + " == \"Left\")");
                sb.AppendLine(Indent(indent) + "{");
                sb.AppendLine(Indent(indent + 1) + "transform.Rotate(Vector3.up, -90);");
                sb.AppendLine(Indent(indent) + "}");
                sb.AppendLine(Indent(indent) + "else if (" + dirVar + " == \"Right\")");
                sb.AppendLine(Indent(indent) + "{");
                sb.AppendLine(Indent(indent + 1) + "transform.Rotate(Vector3.up, 90);");
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_Wait):
            {
                // 대기 블록: 현재 Run()이 void이므로 동기 대기 미지원. 주석으로 안내만 출력
                var inputs = baseIns.Section0Inputs;
                string expr = inputs != null && inputs.Length > 0 ? BuildValueExpression(inputs[0]) : "0f";
                sb.AppendLine(Indent(indent) + "// TODO: " + "Wait " + "(" + expr + ") 초 대기는 void 메서드에서 직접 지원되지 않습니다.");
                break;
            }
            case nameof(BE2_Ins_Repeat):
            {
                // 반복 블록: 반복 횟수에 변수/연산 사용 가능
                var inputs = baseIns.Section0Inputs;
                string countExpr = inputs != null && inputs.Length > 0 ? BuildValueExpression(inputs[0]) : "0";
                string loopVar = "i" + indent.ToString();
                sb.AppendLine(Indent(indent) + "for (int " + loopVar + " = 0; " + loopVar + " < (int)(" + countExpr + "); " + loopVar + "++)");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_RepeatForever):
            {
                sb.AppendLine(Indent(indent) + "while (true)");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                // 무한 루프는 주의해서 사용하세요.
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_RepeatUntil):
            {
                // 조건이 참이 될 때까지 반복: 조건식에 변수/연산 사용 가능
                var inputs = baseIns.Section0Inputs;
                string condExpr = inputs != null && inputs.Length > 0 ? BuildBooleanExpression(inputs[0]) : "false";
                sb.AppendLine(Indent(indent) + "while (!(" + condExpr + "))");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_If):
            {
                // If 블록: 조건식에 변수/연산 사용 가능
                var inputs = baseIns.Section0Inputs;
                string condExpr = inputs != null && inputs.Length > 0 ? BuildBooleanExpression(inputs[0]) : "false";
                sb.AppendLine(Indent(indent) + "if (" + condExpr + ")");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_IfElse):
            {
                // If/Else 블록: 조건식에 변수/연산 사용 가능
                var inputs = baseIns.Section0Inputs;
                string condExpr = inputs != null && inputs.Length > 0 ? BuildBooleanExpression(inputs[0]) : "false";
                sb.AppendLine(Indent(indent) + "if (" + condExpr + ")");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                sb.AppendLine(Indent(indent) + "else");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 1, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_SetVariable):
            {
                // 변수 설정 블록: var 선언 후 대입 (최초 1회 선언)
                var inputs = baseIns.Section0Inputs;
                if (inputs != null && inputs.Length >= 2)
                {
                    string varName = SanitizeVarName(inputs[0].StringValue);
                    string valueExpr = BuildValueExpression(inputs[1]);
                    if (!_declaredVars.Contains(varName))
                    {
                        sb.AppendLine(Indent(indent) + "var " + varName + " = " + valueExpr + ";");
                        _declaredVars.Add(varName);
                    }
                    else
                    {
                        sb.AppendLine(Indent(indent) + varName + " = " + valueExpr + ";");
                    }
                }
                break;
            }
            case nameof(BE2_Ins_AddVariable):
            {
                // 변수 더하기 블록: 문자열/숫자 형식은 런타임에 결정되므로 단순 +로 생성
                var inputs = baseIns.Section0Inputs;
                if (inputs != null && inputs.Length >= 2)
                {
                    string varName = SanitizeVarName(inputs[0].StringValue);
                    string addExpr = BuildValueExpression(inputs[1]);
                    if (!_declaredVars.Contains(varName))
                    {
                        // 선언이 안되어 있으면 기본값으로 초기화 후 연산
                        sb.AppendLine(Indent(indent) + "var " + varName + " = " + addExpr + ";");
                        _declaredVars.Add(varName);
                    }
                    else
                    {
                        sb.AppendLine(Indent(indent) + varName + " = " + varName + " + (" + addExpr + ");");
                    }
                }
                break;
            }
            default:
            {
                sb.Append(GenerateSequentialChildren(block, indent));
                break;
            }
        }
        return sb.ToString();
    }

    string GenerateSequentialChildren(I_BE2_Block block, int indent)
    {
        var layout = block.Layout;
        if (layout == null || layout.SectionsArray == null) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < layout.SectionsArray.Length; i++)
        {
            var section = layout.SectionsArray[i];
            if (section == null || section.Body == null) continue;
            section.Body.UpdateChildBlocksList();
            var children = section.Body.ChildBlocksArray;
            if (children == null) continue;
            for (int c = 0; c < children.Length; c++)
            {
                var child = children[c];
                // 단독으로 놓인 연산/변수 블록은 결과를 콘솔에 출력하도록 처리
                if (child != null && child.Type == MG_BlocksEngine2.Block.BlockTypeEnum.operation)
                {
                    string expr = GenerateOperationExpression(child);
                    if (!string.IsNullOrEmpty(expr))
                    {
                        sb.AppendLine(Indent(indent) + "Debug.Log(" + expr + ");");
                        continue;
                    }
                }
                sb.Append(GenerateForBlock(child, indent));
            }
        }
        return sb.ToString();
    }

    // 입력으로부터 C# 값 표현식 생성 (숫자/문자열/연산/변수 블록 지원)
    string BuildValueExpression(I_BE2_BlockSectionHeaderInput input)
    {
        if (input == null) return "";
        var spot = input.Spot;
        var block = spot != null ? spot.Block : null;
        if (block != null)
        {
            // 입력에 블록이 놓인 경우 해당 블록의 연산 표현식 생성
            string op = GenerateOperationExpression(block);
            if (!string.IsNullOrEmpty(op)) return op;
        }
        // 블록이 없으면 현재 입력값을 그대로 사용
        var vals = input.InputValues;
        if (vals.isText)
        {
            return QuoteString(vals.stringValue);
        }
        else
        {
            return vals.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f";
        }
    }

    public bool SaveXmlToAssets(string relativeAssetPath = "Assets/Generated/BlocksGenerated.be2")
    {
        string xml = GenerateXmlFromAllEnvs();
        if (string.IsNullOrEmpty(xml)) return false;

        string fullPath;
        bool isPlayMode = Application.isPlaying;
        if (isPlayMode)
        {
            string fileName = Path.GetFileName(relativeAssetPath);
            if (string.IsNullOrEmpty(fileName)) fileName = "BlocksGenerated.be2";
            var nonAssetsDir = Path.Combine(Application.persistentDataPath, "Generated");
            if (!Directory.Exists(nonAssetsDir)) Directory.CreateDirectory(nonAssetsDir);
            fullPath = Path.Combine(nonAssetsDir, fileName);
        }
        else
        {
            fullPath = relativeAssetPath;
            if (!Path.IsPathRooted(fullPath))
            {
                if (relativeAssetPath.StartsWith("Assets/") || relativeAssetPath.StartsWith("Assets\\"))
                {
                    string sub = relativeAssetPath.Substring(7);
                    fullPath = Path.Combine(Application.dataPath, sub);
                }
                else
                {
                    fullPath = Path.Combine(Application.dataPath, relativeAssetPath);
                }
            }
            var dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, xml);
        LastSavedPath = fullPath;
        Debug.Log($"[BE2_CodeExporter] Saved generated blocks XML to: {fullPath} (PlayMode={isPlayMode})");

#if UNITY_EDITOR
        if (isPlayMode)
        {
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastTempXmlPath", fullPath);
            string relXml = relativeAssetPath;
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastRelXmlAssetPath", relXml);
        }
        else
        {
            UnityEditor.AssetDatabase.Refresh();
        }
#endif
        return true;
    }

    // 불리언 표현식 생성 (Equal, BiggerThan 등)
    string BuildBooleanExpression(I_BE2_BlockSectionHeaderInput input)
    {
        if (input == null) return "false";
        var spot = input.Spot;
        var block = spot != null ? spot.Block : null;
        if (block != null)
        {
            string expr = GenerateOperationExpression(block);
            if (!string.IsNullOrEmpty(expr)) return expr;
        }
        // 숫자/문자열 입력을 bool로 간주: "1"/true만 참
        var vals = input.InputValues;
        if (vals.isText)
            return vals.stringValue == "1" || vals.stringValue.ToLower() == "true" ? "true" : "false";
        return vals.floatValue != 0 ? "true" : "false";
    }

    // 연산/변수 블록을 C# 표현식으로 변환
    string GenerateOperationExpression(I_BE2_Block opBlock)
    {
        if (opBlock == null || opBlock.Instruction == null) return string.Empty;
        var baseIns = opBlock.Transform.GetComponent<I_BE2_InstructionBase>();
        var typeName = opBlock.Instruction.GetType().Name;
        switch (typeName)
        {
            case nameof(BE2_Op_Variable):
            {
                // 변수 참조 블록: 변수명을 C# 식별자로 변환하여 사용
                var name = baseIns.Section0Inputs[0].StringValue;
                return SanitizeVarName(name);
            }
            case nameof(BE2_Op_Equal):
            {
                var a = BuildValueExpression(baseIns.Section0Inputs[0]);
                var b = BuildValueExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " == " + b + ")";
            }
            case nameof(BE2_Op_BiggerThan):
            {
                var a = BuildValueExpression(baseIns.Section0Inputs[0]);
                var b = BuildValueExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " > " + b + ")";
            }
            default:
                return string.Empty;
        }
    }

    // 문자열을 C# 문자열 리터럴로 변환
    string QuoteString(string s)
    {
        if (s == null) s = string.Empty;
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // BE2 변수명을 C# 식별자로 정규화
    string SanitizeVarName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "var_";
        var sb = new StringBuilder();
        // 첫 글자는 문자 또는 '_'
        if (!(char.IsLetter(raw[0]) || raw[0] == '_')) sb.Append('_');
        for (int i = 0; i < raw.Length; i++)
        {
            char ch = raw[i];
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }
        var name = sb.ToString();
        if (string.IsNullOrEmpty(name)) name = "var_";
        return name;
    }

    string GenerateSectionBody(I_BE2_Block block, int sectionIndex, int indent)
    {
        var layout = block.Layout;
        if (layout == null || layout.SectionsArray == null) return string.Empty;
        if (sectionIndex < 0 || sectionIndex >= layout.SectionsArray.Length) return string.Empty;
        var section = layout.SectionsArray[sectionIndex];
        if (section == null || section.Body == null) return string.Empty;
        section.Body.UpdateChildBlocksList();
        var children = section.Body.ChildBlocksArray;
        var sb = new StringBuilder();
        if (children != null)
        {
            for (int i = 0; i < children.Length; i++)
            {
                sb.Append(GenerateForBlock(children[i], indent));
            }
        }
        return sb.ToString();
    }

    public bool SaveScriptToAssets(string relativeAssetPath = "Assets/Generated/BlocksGenerated.cs", string className = "BlocksGenerated", string methodName = "Run")
    {
        string code = GenerateCSharpFromAllEnvs();
        if (string.IsNullOrEmpty(code)) return false;
        string xml = GenerateXmlFromAllEnvs();
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections;");
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine("public class " + className + " : MonoBehaviour");
        sb.AppendLine("{");
        sb.AppendLine("    public void " + methodName + "()");
        sb.AppendLine("    {");
        var lines = code.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) { sb.AppendLine(); continue; }
            sb.AppendLine("        " + lines[i]);
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        string fullPath;
        bool isPlayMode = Application.isPlaying;
        if (isPlayMode)
        {
            string fileName = Path.GetFileName(relativeAssetPath);
            if (string.IsNullOrEmpty(fileName)) fileName = "BlocksGenerated.cs";
            var nonAssetsDir = Path.Combine(Application.persistentDataPath, "Generated");
            if (!Directory.Exists(nonAssetsDir)) Directory.CreateDirectory(nonAssetsDir);
            fullPath = Path.Combine(nonAssetsDir, fileName);
        }
        else
        {
            fullPath = relativeAssetPath;
            if (!Path.IsPathRooted(fullPath))
            {
                if (relativeAssetPath.StartsWith("Assets/") || relativeAssetPath.StartsWith("Assets\\"))
                {
                    string sub = relativeAssetPath.Substring(7);
                    fullPath = Path.Combine(Application.dataPath, sub);
                }
                else
                {
                    fullPath = Path.Combine(Application.dataPath, relativeAssetPath);
                }
            }
            var dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        File.WriteAllText(fullPath, sb.ToString());
        LastSavedPath = fullPath;
        Debug.Log($"[BE2_CodeExporter] Saved generated script to: {fullPath} (PlayMode={isPlayMode})");
        // Save XML next to the C# file
        string xmlPath = Path.ChangeExtension(fullPath, ".be2");
        if (!string.IsNullOrEmpty(xml))
        {
            File.WriteAllText(xmlPath, xml);
            Debug.Log($"[BE2_CodeExporter] Saved generated blocks XML to: {xmlPath}");
        }
#if UNITY_EDITOR
        if (isPlayMode)
        {
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastTempPath", fullPath);
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastRelAssetPath", relativeAssetPath);
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastTempXmlPath", xmlPath);
            string relXml = Path.ChangeExtension(relativeAssetPath, ".be2");
            UnityEditor.EditorPrefs.SetString("BE2_CodeExporter_LastRelXmlAssetPath", relXml);
        }
#endif
#if UNITY_EDITOR
        if (!isPlayMode)
        {
            UnityEditor.AssetDatabase.Refresh();
        }
#endif
        return true;
    }

    public bool ImportLastGeneratedToEnv(MG_BlocksEngine2.Environment.BE2_ProgrammingEnv targetEnv = null)
    {
        try
        {
            if (targetEnv == null)
            {
                var envs = GameObject.FindObjectsOfType<MG_BlocksEngine2.Environment.BE2_ProgrammingEnv>();
                if (envs != null && envs.Length > 0) targetEnv = envs[0];
            }
            if (targetEnv == null) { Debug.LogWarning("[BE2_CodeExporter] No ProgrammingEnv found for import."); return false; }

            string xmlPath = null;
#if UNITY_EDITOR
            // Prefer temp path saved during Play
            string tempXml = UnityEditor.EditorPrefs.GetString("BE2_CodeExporter_LastTempXmlPath", string.Empty);
            if (!string.IsNullOrEmpty(tempXml) && File.Exists(tempXml))
                xmlPath = tempXml;
#endif
            if (xmlPath == null)
            {
                if (!string.IsNullOrEmpty(LastSavedPath))
                {
                    string candidate = Path.ChangeExtension(LastSavedPath, ".be2");
                    if (File.Exists(candidate)) xmlPath = candidate;
                }
            }
            if (xmlPath == null)
            {
                // Fallback next to Assets path
                string rel = "Assets/Generated/BlocksGenerated.be2";
                if (rel.StartsWith("Assets/"))
                {
                    string sub = rel.Substring(7);
                    string candidate = Path.Combine(Application.dataPath, sub);
                    if (File.Exists(candidate)) xmlPath = candidate;
                }
            }
            if (string.IsNullOrEmpty(xmlPath)) { Debug.LogWarning("[BE2_CodeExporter] No generated XML found to import."); return false; }

            string xmlString = File.ReadAllText(xmlPath);
            if (string.IsNullOrWhiteSpace(xmlString)) { Debug.LogWarning("[BE2_CodeExporter] XML file is empty."); return false; }
            BE2_BlocksSerializer.XMLToBlocksCode(xmlString, targetEnv);
            Debug.Log($"[BE2_CodeExporter] Imported blocks from: {xmlPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BE2_CodeExporter] Import failed: {ex.Message}");
            return false;
        }
    }
}
