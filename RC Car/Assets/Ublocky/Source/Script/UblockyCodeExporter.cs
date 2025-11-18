using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UBlockly; // Ublocky의 핵심 기능에 접근하기 위해 필요합니다.
using System;
using System.IO;
using System.Text;
using UBlockly.UGUI;
using System.Text.RegularExpressions;

public class UblockyCodeExporter : MonoBehaviour
{
    //현재 UBlockly 워크스페이스의 블록을 C# 코드 문자열로 변환 후 반환.
    public string ExportCSharpCode()
    {
        var view = BlocklyUI.WorkspaceView;
        if (view == null) return string.Empty;
        var workspace = view.Workspace;
        if (workspace == null) return string.Empty;
        return CSharp.Generator.WorkspaceToCode(workspace);
    }

    //변환된 코드를 지정 클래스/메서드 구조로 감싸서 컴파일 가능한 C# 스크립트 문자열로 빌드.
    public string BuildCSharpScript(string className, string methodName = "Run", string ns = null, bool asMonoBehaviour = true)
    {
        string code = ExportCSharpCode();
        if (string.IsNullOrEmpty(code)) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using UnityEngine;");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
        }
        string classDecl = asMonoBehaviour ? ("public class " + className + " : MonoBehaviour") : ("public static class " + className);
        sb.AppendLine(" " + classDecl);
        sb.AppendLine(" {");
        string methodDecl = asMonoBehaviour ? ("public void " + methodName + "()") : ("public static void " + methodName + "()");
        sb.AppendLine("  " + methodDecl);
        sb.AppendLine("  {");
        var fixedCode = FixupCSharpForMethod(code.Replace("\r\n", "\n"));
        var lines = fixedCode.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) { sb.AppendLine(); continue; }
            sb.AppendLine("   " + lines[i]);
        }
        sb.AppendLine("  }");
        sb.AppendLine(" }");
        if (!string.IsNullOrEmpty(ns))
        {
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    private string FixupCSharpForMethod(string code)
    {
        var sb = new StringBuilder();
        var lines = code.Split('\n');
        var bareVar = new Regex("^\\s*var\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*;\\s*$");
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (bareVar.IsMatch(line))
            {
                line = bareVar.Replace(line, "object $1 = null;");
            }
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd('\n');
    }

    public bool SaveScriptToPath(string fullPath, string className = "UBlocklyGenerated", string methodName = "Run", string ns = null, bool asMonoBehaviour = true)
    {
        string script = BuildCSharpScript(className, methodName, ns, asMonoBehaviour);
        if (string.IsNullOrEmpty(script)) return false;
        string dir = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, script);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
        return true;
    }

    public bool SaveScriptToAssets(string relativeAssetPath = "Assets/Generated/UBlocklyGenerated.cs", string className = "UBlocklyGenerated", string methodName = "Run", string ns = null, bool asMonoBehaviour = true)
    {
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
        return SaveScriptToPath(fullPath, className, methodName, ns, asMonoBehaviour);
    }
}
