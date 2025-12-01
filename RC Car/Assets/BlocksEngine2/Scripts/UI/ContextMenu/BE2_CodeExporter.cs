using System;
using System.IO;
using System.Text;
using UnityEngine;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.Block.Instruction;
using MG_BlocksEngine2.Serializer;
using TMPro;

public class BE2_CodeExporter : MonoBehaviour
{
    // 변수 선언 여부를 추적하기 위한 집합 (중복 선언 방지)
    System.Collections.Generic.HashSet<string> _declaredVars = new System.Collections.Generic.HashSet<string>();
    // 함수 정의 중복 생성을 방지하고, Run() 상단에 삽입할 로컬 함수 버퍼를 관리
    System.Collections.Generic.HashSet<string> _generatedFunctionIds = new System.Collections.Generic.HashSet<string>();
    StringBuilder _functionsSb = new StringBuilder();
    // 함수 본문 내 로컬 변수명 -> 파라미터명 매핑 컨텍스트
    System.Collections.Generic.Dictionary<string, string> _currentLocalVarMap;
    bool _inFunctionBody = false;
    public string LastSavedPath;
    public string GenerateCSharpFromAllEnvs()
    {
        var envs = GameObject.FindObjectsOfType<MG_BlocksEngine2.Environment.BE2_ProgrammingEnv>();
        var sb = new StringBuilder();
        // Export 상태 초기화
        _declaredVars.Clear();
        _generatedFunctionIds.Clear();
        _functionsSb = new StringBuilder();
        for (int i = 0; i < envs.Length; i++)
        {
            var env = envs[i];
            if (env == null) continue;
            var blocks = GetTopLevelBlocks(env);
            if (blocks == null) continue;
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (block == null) continue;
                // 함수 정의 블록은 로컬 함수로 미리 생성하고, 본문에는 출력하지 않음
                if (block.Instruction is BE2_Ins_DefineFunction defIns)
                {
                    EnsureFunctionGenerated(defIns);
                    continue;
                }
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

    System.Collections.Generic.List<I_BE2_Block> GetTopLevelBlocks(MG_BlocksEngine2.Environment.BE2_ProgrammingEnv env)
    {
        var result = new System.Collections.Generic.List<I_BE2_Block>();
        if (env == null) return result;
        var all = env.Transform.GetComponentsInChildren<I_BE2_Block>(true);
        var seen = new System.Collections.Generic.HashSet<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            var b = all[i];
            if (b == null) continue;
            var t = b.Transform;
            if (t == null) continue;
            var go = t.gameObject;
            if (go == null || !go.activeInHierarchy) continue;
            bool inBody = t.GetComponentInParent<MG_BlocksEngine2.Block.I_BE2_BlockSectionBody>() != null && t.parent != env.Transform;
            if (inBody) continue;
            if (seen.Add(t)) result.Add(b);
        }
        return result;
    }

    string QuoteString(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder();
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
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
            case nameof(BE2_Ins_DefineFunction):
            {
                // 함수 정의는 Run() 내부 로컬 함수로 생성되고, 호출 위치에는 아무 것도 출력하지 않음
                EnsureFunctionGenerated((BE2_Ins_DefineFunction)ins);
                break;
            }
            case nameof(BE2_Ins_FunctionBlock):
            {
                // 함수 호출 블록: 대응되는 로컬 함수를 호출
                var callIns = (BE2_Ins_FunctionBlock)ins;
                var def = callIns != null ? callIns.defineInstruction : null;
                if (def != null)
                {
                    EnsureFunctionGenerated(def);
                    string methodName = BuildFunctionName(def);
                    var inputs = baseIns.Section0Inputs;
                    System.Collections.Generic.List<string> args = new System.Collections.Generic.List<string>();
                    if (inputs != null)
                    {
                        for (int i = 0; i < inputs.Length; i++)
                        {
                            string expr = BuildValueExpression(inputs[i]);
                            args.Add(expr);
                        }
                    }
                    sb.AppendLine(Indent(indent) + methodName + "(" + string.Join(", ", args.ToArray()) + ");");
                }
                break;
            }
            case nameof(BE2_Ins_ReferenceFunctionBlock):
            {
                var refIns = (BE2_Ins_ReferenceFunctionBlock)ins;
                var func = refIns != null ? refIns.functionInstruction : null;
                var def = func != null ? func.defineInstruction : null;
                if (def != null)
                {
                    EnsureFunctionGenerated(def);
                    string methodName = BuildFunctionName(def);
                    var inputs = baseIns.Section0Inputs;
                    System.Collections.Generic.List<string> args = new System.Collections.Generic.List<string>();
                    if (inputs != null)
                    {
                        for (int i = 0; i < inputs.Length; i++)
                        {
                            string expr = BuildValueExpression(inputs[i]);
                            args.Add(expr);
                        }
                    }
                    sb.AppendLine(Indent(indent) + methodName + "(" + string.Join(", ", args.ToArray()) + ");");
                }
                break;
            }
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
                // 조건 블록에 대한 일반 처리: 섹션 수로 if/else 판단 (타입명이 다를 때 대비)
                if (block.Type == MG_BlocksEngine2.Block.BlockTypeEnum.condition && baseIns != null)
                {
                    var inputs = baseIns.Section0Inputs;
                    string condExpr = inputs != null && inputs.Length > 0 ? BuildBooleanExpression(inputs[0]) : "false";
                    int sectionCount = (block.Layout != null && block.Layout.SectionsArray != null) ? block.Layout.SectionsArray.Length : 0;
                    if (sectionCount >= 1)
                    {
                        sb.AppendLine(Indent(indent) + "if (" + condExpr + ")");
                        sb.AppendLine(Indent(indent) + "{");
                        sb.Append(GenerateSectionBody(block, 0, indent + 1));
                        sb.AppendLine(Indent(indent) + "}");
                        if (sectionCount >= 2)
                        {
                            sb.AppendLine(Indent(indent) + "else");
                            sb.AppendLine(Indent(indent) + "{");
                            sb.Append(GenerateSectionBody(block, 1, indent + 1));
                            sb.AppendLine(Indent(indent) + "}");
                        }
                        break;
                    }
                }
                // 기본: 자식 순차 블록 생성
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
            case nameof(BE2_Op_FunctionLocalVariable):
            {
                // 함수 로컬 변수 참조를 파라미터명으로 매핑
                string v = "";
                var text = opBlock.Transform.GetComponentInChildren<TMP_Text>();
                if (text != null) v = text.text;
                if (_inFunctionBody && _currentLocalVarMap != null && !string.IsNullOrEmpty(v) && _currentLocalVarMap.TryGetValue(v, out var paramName))
                {
                    return "System.Convert.ToSingle(" + paramName + ")";
                }
                // 매핑이 없으면 빈 문자열
                return string.Empty;
            }
            case nameof(BE2_Op_Variable):
            {
                // 변수 참조 블록: 변수명을 C# 식별자로 변환하여 사용
                var name = baseIns.Section0Inputs[0].StringValue;
                return SanitizeVarName(name);
            }
            case nameof(BE2_Op_Sum):
            {
                var a = BuildValueExpression(baseIns.Section0Inputs[0]);
                var b = BuildValueExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " + " + b + ")";
            }
            case nameof(BE2_Op_Multiply):
            {
                var a = BuildValueExpression(baseIns.Section0Inputs[0]);
                var b = BuildValueExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " * " + b + ")";
            }
            case nameof(BE2_Op_Divide):
            {
                var a = BuildValueExpression(baseIns.Section0Inputs[0]);
                var b = BuildValueExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " / " + b + ")";
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
            case nameof(BE2_Op_Not):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                return "(!" + a + ")";
            }
            case nameof(BE2_Op_And):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                var b = BuildBooleanExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " && " + b + ")";
            }
            case nameof(BE2_Op_Or):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                var b = BuildBooleanExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " || " + b + ")";
            }
            case nameof(BE2_Op_Xor):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                var b = BuildBooleanExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " ^ " + b + ")";
            }
            case nameof(BE2_Op_Nand):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                var b = BuildBooleanExpression(baseIns.Section0Inputs[1]);
                return "(!(" + a + " && " + b + "))";
            }
            case nameof(BE2_Op_Nor):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                var b = BuildBooleanExpression(baseIns.Section0Inputs[1]);
                return "(!(" + a + " || " + b + "))";
            }
            case nameof(BE2_Op_Xnor):
            {
                var a = BuildBooleanExpression(baseIns.Section0Inputs[0]);
                var b = BuildBooleanExpression(baseIns.Section0Inputs[1]);
                return "(" + a + " == " + b + ")";
            }
            case nameof(BE2_Op_Random):
            {
                var a = BuildValueExpression(baseIns.Section0Inputs[0]);
                var b = BuildValueExpression(baseIns.Section0Inputs[1]);
                return "UnityEngine.Random.Range(" + a + ", " + b + ")";
            }
            case nameof(BE2_Op_KeyPressed):
            {
                var idx = BuildValueExpression(baseIns.Section0Inputs[0]);
                return "UnityEngine.Input.GetKey(MG_BlocksEngine2.Core.BE2_InputManager.keyCodeList[(int)(" + idx + ")])";
            }
            case nameof(BE2_Op_JoystickKeyPressed):
            {
                var idx = BuildValueExpression(baseIns.Section0Inputs[0]);
                return "MG_BlocksEngine2.UI.BE2_VirtualJoystick.instance.keys[(int)(" + idx + ")].isPressed";
            }
            default:
            {
                return string.Empty;
            }
        }
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
        if (string.IsNullOrEmpty(code) && (_functionsSb == null || _functionsSb.Length == 0)) return false;
        string xml = GenerateXmlFromAllEnvs();
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections;");
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine("public class " + className + " : MonoBehaviour");
        sb.AppendLine("{");
        sb.AppendLine("    public void " + methodName + "()");
        sb.AppendLine("    {");
        // 먼저 로컬 함수들(사용자 정의 함수)을 선언
        if (_functionsSb != null && _functionsSb.Length > 0)
        {
            sb.Append(_functionsSb.ToString());
        }
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

    // Helper methods for user-defined Function Blocks
    string BuildFunctionName(BE2_Ins_DefineFunction def)
    {
        if (def == null) return "Func_";
        var layout = def.Block != null ? def.Block.Layout : null;
        var header = (layout != null && layout.SectionsArray != null && layout.SectionsArray.Length > 0) ? layout.SectionsArray[0].Header : null;
        System.Collections.Generic.List<string> labelParts = new System.Collections.Generic.List<string>();
        if (header != null)
        {
            header.UpdateItemsArray();
            var items = header.ItemsArray;
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null) continue;
                    // 라벨 여부 판단: Label 컴포넌트 존재 시 라벨로 간주
                    var isLabel = item.Transform.GetComponentInChildren<MG_BlocksEngine2.UI.FunctionBlock.Label>() != null;
                    if (isLabel)
                    {
                        var t = item.Transform.GetComponentInChildren<TMPro.TMP_Text>();
                        if (t != null && !string.IsNullOrEmpty(t.text))
                        {
                            labelParts.Add(SanitizeVarName(t.text));
                        }
                    }
                }
            }
        }
        if (labelParts.Count > 0)
        {
            return string.Join("_", labelParts.ToArray());
        }
        string id = string.IsNullOrEmpty(def.defineID) ? Guid.NewGuid().ToString("N") : def.defineID.Replace("-", "");
        return id;
    }

    void EnsureFunctionGenerated(BE2_Ins_DefineFunction def)
    {
        if (def == null) return;
        if (_generatedFunctionIds.Contains(def.defineID)) return;

        var layout = def.Block != null ? def.Block.Layout : null;
        var header = (layout != null && layout.SectionsArray != null && layout.SectionsArray.Length > 0) ? layout.SectionsArray[0].Header : null;

        System.Collections.Generic.List<string> paramNames = new System.Collections.Generic.List<string>();
        _currentLocalVarMap = new System.Collections.Generic.Dictionary<string, string>();
        if (header != null)
        {
            header.UpdateItemsArray();
            var items = header.ItemsArray;
            if (items != null)
            {
                int idx = 1;
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null) continue;
                    var isLabel = item.Transform.GetComponentInChildren<MG_BlocksEngine2.UI.FunctionBlock.Label>() != null;
                    if (!isLabel)
                    {
                        string param = SanitizeVarName("input" + idx.ToString());
                        paramNames.Add(param);
                        idx++;
                        var t = item.Transform.GetComponentInChildren<TMPro.TMP_Text>();
                        if (t != null && !string.IsNullOrEmpty(t.text))
                        {
                            if (!_currentLocalVarMap.ContainsKey(t.text))
                                _currentLocalVarMap.Add(t.text, param);
                        }
                    }
                }
            }
        }

        string methodName = BuildFunctionName(def);
        _inFunctionBody = true;
        _functionsSb.AppendLine("        void " + methodName + "(" + string.Join(", ", paramNames.ConvertAll(n => "object " + n).ToArray()) + ")");
        _functionsSb.AppendLine("        {");
        string body = GenerateSectionBody(def.Block, 0, 3);
        if (!string.IsNullOrEmpty(body))
        {
            _functionsSb.Append(body);
        }
        _functionsSb.AppendLine("        }");
        _inFunctionBody = false;
        _currentLocalVarMap = null;

        _generatedFunctionIds.Add(def.defineID);
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
