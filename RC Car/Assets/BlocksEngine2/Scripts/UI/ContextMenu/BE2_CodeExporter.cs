using System;
using System.IO;
using System.Text;
using UnityEngine;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.Block.Instruction;

public class BE2_CodeExporter : MonoBehaviour
{
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
                var inputs = baseIns.Section0Inputs;
                float value = inputs != null && inputs.Length > 0 ? inputs[0].FloatValue : 0f;
                sb.AppendLine(Indent(indent) + "transform.position += transform.forward * " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f;");
                break;
            }
            case nameof(BE2_Ins_TurnDirection):
            {
                var inputs = baseIns.Section0Inputs;
                string v = inputs != null && inputs.Length > 0 ? inputs[0].StringValue : "";
                if (string.Equals(v, "Left", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(Indent(indent) + "transform.Rotate(Vector3.up, -90);");
                else if (string.Equals(v, "Right", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(Indent(indent) + "transform.Rotate(Vector3.up, 90);");
                else
                    sb.AppendLine(Indent(indent) + "transform.Rotate(Vector3.up, 0);");
                break;
            }
            case nameof(BE2_Ins_Wait):
            {
                var inputs = baseIns.Section0Inputs;
                float value = inputs != null && inputs.Length > 0 ? inputs[0].FloatValue : 0f;
                sb.AppendLine(Indent(indent) + "yield return new WaitForSeconds(" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ");");
                break;
            }
            case nameof(BE2_Ins_Repeat):
            {
                var inputs = baseIns.Section0Inputs;
                int count = 0;
                if (inputs != null && inputs.Length > 0)
                {
                    count = Mathf.FloorToInt(inputs[0].FloatValue);
                }
                string loopVar = "i" + indent.ToString();
                sb.AppendLine(Indent(indent) + "for (int " + loopVar + " = 0; " + loopVar + " < " + count + "; " + loopVar + "++)");
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
                sb.AppendLine(Indent(indent + 1) + "yield return null;");
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_RepeatUntil):
            {
                var inputs = baseIns.Section0Inputs;
                string v = inputs != null && inputs.Length > 0 ? inputs[0].StringValue : "false";
                bool cond = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                sb.AppendLine(Indent(indent) + "while (!" + (cond ? "true" : "false") + ")");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent + 1) + "yield return null;");
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_If):
            {
                var inputs = baseIns.Section0Inputs;
                string v = inputs != null && inputs.Length > 0 ? inputs[0].StringValue : "false";
                bool cond = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                sb.AppendLine(Indent(indent) + "if (" + (cond ? "true" : "false") + ")");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                break;
            }
            case nameof(BE2_Ins_IfElse):
            {
                var inputs = baseIns.Section0Inputs;
                string v = inputs != null && inputs.Length > 0 ? inputs[0].StringValue : "false";
                bool cond = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                sb.AppendLine(Indent(indent) + "if (" + (cond ? "true" : "false") + ")");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 0, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
                sb.AppendLine(Indent(indent) + "else");
                sb.AppendLine(Indent(indent) + "{");
                sb.Append(GenerateSectionBody(block, 1, indent + 1));
                sb.AppendLine(Indent(indent) + "}");
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
                sb.Append(GenerateForBlock(children[c], indent));
            }
        }
        return sb.ToString();
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
        string fullPath = relativeAssetPath;
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
        File.WriteAllText(fullPath, sb.ToString());
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
        return true;
    }
}
