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
    
    // 함수 정의를 저장하는 딕셔너리 (함수 이름 → 함수 XElement)
    private static Dictionary<string, XElement> functionDefinitions = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
    
    // 실제로 호출된 함수 이름들
    private static HashSet<string> calledFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static void Export(string xmlText)
    {
        var jsonPath = Path.Combine(Application.persistentDataPath, "BlocksRuntime.json");
        
        // 초기화
        variables.Clear();
        functionDefinitions.Clear();
        calledFunctionNames.Clear();
                
        var chunks = xmlText.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);

        // ============================================================
        // 1단계: 모든 청크를 파싱하여 함수 정의 수집 및 WhenPlayClicked 찾기
        // ============================================================
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
            
            // 디버그: 모든 블록 이름 출력
            Debug.Log($"[BE2XmlToRuntimeJson] Found block: {blockName}");
            
            // 함수 정의 블록 수집 (여러 가능한 이름 패턴)
            bool isFunctionDefinition = 
                blockName == "Block Ins DefineFunction" || blockName == "Block Cst DefineFunction" ||
                blockName == "Block Ins Function" || blockName == "Block Cst Function" ||
                blockName == "Block Ins NewFunction" || blockName == "Block Cst NewFunction" ||
                blockName == "Block Ins FunctionDef" || blockName == "Block Cst FunctionDef" ||
                (blockName != null && blockName.Contains("DefineFunction")) ||
                (blockName != null && blockName.Contains("NewFunction"));
            
            // 추가 확인: 블록에 defineID가 있고 FunctionBlock이 아닌 경우도 함수 정의일 수 있음
            if (!isFunctionDefinition && blockName != "Block Ins FunctionBlock" && blockName != "Block Cst FunctionBlock")
            {
                var hasDefineID = !string.IsNullOrEmpty(doc.Root?.Element("defineID")?.Value?.Trim());
                var hasSections = doc.Root?.Element("sections") != null;
                // defineID가 있고 sections가 있으면서 WhenPlayClicked이 아닌 경우, 함수 정의일 가능성 확인
                if (hasDefineID && hasSections && 
                    blockName != "Block Ins WhenPlayClicked" && blockName != "Block Cst WhenPlayClicked")
                {
                    var defineID = doc.Root?.Element("defineID")?.Value?.Trim();
                    Debug.Log($"[BE2XmlToRuntimeJson] Block '{blockName}' has defineID='{defineID}', checking if it's a function definition...");
                    
                    // defineID가 있는 블록을 함수 정의 후보로 처리
                    if (!string.IsNullOrEmpty(defineID) && !IsNumericOnly(defineID))
                    {
                        functionDefinitions[defineID] = doc.Root;
                        Debug.Log($"[BE2XmlToRuntimeJson] Found function definition from defineID: {defineID}");
                    }
                }
            }
            
            if (isFunctionDefinition)
            {
                var funcName = ExtractFunctionName(doc.Root);
                if (!string.IsNullOrEmpty(funcName))
                {
                    functionDefinitions[funcName] = doc.Root;
                    Debug.Log($"[BE2XmlToRuntimeJson] Found function definition: {funcName}");
                }
                else
                {
                    Debug.LogWarning($"[BE2XmlToRuntimeJson] Function definition block found but no name extracted!");
                }
            }
            // WhenPlayClicked 블록 찾기
            else if (blockName == "Block Ins WhenPlayClicked" || blockName == "Block Cst WhenPlayClicked")
            {
                mainTriggerBlock = doc.Root;
                Debug.Log($"[BE2XmlToRuntimeJson] Found main trigger block: {blockName}");
            }
        }
        
        if (mainTriggerBlock == null)
        {
            Debug.LogWarning("[BE2XmlToRuntimeJson] WhenPlayClicked block not found! No blocks will be exported.");
            // 빈 JSON 생성
            var emptyJson = BuildJson(new List<VariableNode>(), new List<LoopBlockNode>(), new List<FunctionNode>());
            File.WriteAllText(jsonPath, emptyJson);
            return;
        }
        
        Debug.Log($"[BE2XmlToRuntimeJson] Total function definitions found: {functionDefinitions.Count}");
                
        // ============================================================
        // 2단계: WhenPlayClicked과 연결된 변수 정의 처리
        // ============================================================
        var initBlocks = new List<VariableNode>();
        ProcessVariableDefinitions(mainTriggerBlock, initBlocks);
        
        foreach (var v in initBlocks)
        {
            Debug.Log($"  - {v.name} = {v.value}");
        }
        
        // ============================================================
        // 3단계: WhenPlayClicked과 연결된 Loop 블록 처리 (함수 호출 추적 포함)
        // ============================================================
        var loopBlocks = new List<LoopBlockNode>();
        ProcessLoopBlocks(mainTriggerBlock, loopBlocks);
        
        Debug.Log($"[BE2XmlToRuntimeJson] Loop blocks: {loopBlocks.Count}");
        Debug.Log($"[BE2XmlToRuntimeJson] Called functions: {string.Join(", ", calledFunctionNames)}");
        
        // ============================================================
        // 4단계: 호출된 함수만 파싱하여 functions 배열 구성
        // ============================================================
        var functionNodes = new List<FunctionNode>();
        foreach (var funcName in calledFunctionNames)
        {
            if (functionDefinitions.TryGetValue(funcName, out var funcBlock))
            {
                var funcNode = ParseFunctionDefinition(funcName, funcBlock);
                if (funcNode != null)
                {
                    functionNodes.Add(funcNode);
                    Debug.Log($"[BE2XmlToRuntimeJson] Added function to JSON: {funcName}");
                }
            }
            else
            {
                Debug.LogWarning($"[BE2XmlToRuntimeJson] Function '{funcName}' was called but not defined!");
            }
        }

        // JSON 빌드
        var json = BuildJson(initBlocks, loopBlocks, functionNodes);
        File.WriteAllText(jsonPath, json);
        
        Debug.Log($"[BE2XmlToRuntimeJson] Exported to: {jsonPath}");
        Debug.Log($"[BE2XmlToRuntimeJson] JSON content:\n{json}");
    }
    
    /// <summary>
    /// 함수 정의 블록에서 함수 이름 추출
    /// </summary>
    static string ExtractFunctionName(XElement funcBlock)
    {
        // 디버그: 블록 내용 출력
        Debug.Log($"[ExtractFunctionName] Block XML: {funcBlock.ToString().Substring(0, Math.Min(600, funcBlock.ToString().Length))}...");
        
        // 1. defineID에서 함수 이름 찾기 (가장 신뢰할 수 있음)
        var defineId = funcBlock.Element("defineID")?.Value?.Trim();
        if (!string.IsNullOrEmpty(defineId) && !IsNumericOnly(defineId))
        {
            Debug.Log($"[ExtractFunctionName] Found name from defineID: {defineId}");
            return defineId;
        }
        
        // 2. sections 내부의 inputs에서 defineID 찾기
        var sections = funcBlock.Element("sections")?.Elements("Section");
        if (sections != null)
        {
            foreach (var section in sections)
            {
                var inputs = section.Element("inputs")?.Elements("Input");
                if (inputs != null)
                {
                    foreach (var input in inputs)
                    {
                        var inputDefineId = input.Element("defineID")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(inputDefineId) && !IsNumericOnly(inputDefineId))
                        {
                            Debug.Log($"[ExtractFunctionName] Found name from section defineID: {inputDefineId}");
                            return inputDefineId;
                        }
                    }
                }
            }
        }
        
        // 3. headerInputs에서 함수 이름 찾기 (숫자가 아닌 값)
        var headerInputs = funcBlock.Element("headerInputs")?.Elements("Input").ToList();
        if (headerInputs != null && headerInputs.Count > 0)
        {
            foreach (var input in headerInputs)
            {
                // defineID 먼저 확인
                var inputDefineId = input.Element("defineID")?.Value?.Trim();
                if (!string.IsNullOrEmpty(inputDefineId) && !IsNumericOnly(inputDefineId))
                {
                    Debug.Log($"[ExtractFunctionName] Found name from headerInput defineID: {inputDefineId}");
                    return inputDefineId;
                }
                
                // value 확인 (숫자만 있는 값은 스킵)
                var nameValue = input.Element("value")?.Value?.Trim();
                if (!string.IsNullOrEmpty(nameValue) && !IsNumericOnly(nameValue))
                {
                    Debug.Log($"[ExtractFunctionName] Found name from headerInput value: {nameValue}");
                    return nameValue;
                }
            }
        }
        
        // 4. 모든 Descendants에서 defineID 찾기 (숫자가 아닌 값)
        var allDefineIds = funcBlock.Descendants("defineID");
        foreach (var did in allDefineIds)
        {
            var val = did.Value?.Trim();
            if (!string.IsNullOrEmpty(val) && !IsNumericOnly(val))
            {
                Debug.Log($"[ExtractFunctionName] Found name from descendants defineID: {val}");
                return val;
            }
        }
        
        Debug.LogWarning($"[ExtractFunctionName] Could not extract function name!");
        return null;
    }
    
    /// <summary>
    /// 함수 정의 블록을 FunctionNode로 파싱
    /// </summary>
    static FunctionNode ParseFunctionDefinition(string name, XElement funcBlock)
    {
        var funcNode = new FunctionNode 
        { 
            name = name, 
            body = new List<LoopBlockNode>(),
            parameters = new List<string>()
        };
        
        // defineItems에서 파라미터 이름 추출 (type="variable"인 Item)
        var defineItems = funcBlock.Element("defineItems")?.Elements("Item");
        if (defineItems != null)
        {
            foreach (var item in defineItems)
            {
                var itemType = item.Element("type")?.Value?.Trim();
                var itemValue = item.Element("value")?.Value?.Trim();
                
                // type이 "variable"인 경우 파라미터로 추가
                if (itemType == "variable" && !string.IsNullOrEmpty(itemValue))
                {
                    funcNode.parameters.Add(itemValue);
                    Debug.Log($"[ParseFunctionDefinition] Found parameter from defineItems: {itemValue}");
                }
            }
        }
        
        // headerInputs에서 파라미터 이름 추출 (defineItems에서 못 찾은 경우)
        if (funcNode.parameters.Count == 0)
        {
            var headerInputs = funcBlock.Element("headerInputs")?.Elements("Input").ToList();
            if (headerInputs != null)
            {
                foreach (var input in headerInputs)
                {
                    // value에서 파라미터 이름 추출 (숫자가 아닌 값)
                    var paramName = input.Element("value")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(paramName) && !IsNumericOnly(paramName) && paramName != name)
                    {
                        funcNode.parameters.Add(paramName);
                        Debug.Log($"[ParseFunctionDefinition] Found parameter: {paramName}");
                    }
                }
            }
        }
        
        // sections 내부의 inputs에서 파라미터 이름 추출
        var sections = funcBlock.Element("sections")?.Elements("Section");
        if (sections != null)
        {
            foreach (var section in sections)
            {
                // 파라미터 이름 추출
                var inputs = section.Element("inputs")?.Elements("Input");
                if (inputs != null)
                {
                    foreach (var input in inputs)
                    {
                        var paramName = input.Element("value")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(paramName) && !IsNumericOnly(paramName) && paramName != name)
                        {
                            // 중복 방지
                            if (!funcNode.parameters.Contains(paramName))
                            {
                                funcNode.parameters.Add(paramName);
                                Debug.Log($"[ParseFunctionDefinition] Found parameter from section: {paramName}");
                            }
                        }
                    }
                }
                
                // 블록들 파싱
                var childBlocks = section.Element("childBlocks")?.Elements("Block");
                if (childBlocks != null)
                {
                    foreach (var child in childBlocks)
                    {
                        ProcessLoopBlocksRecursive(child, funcNode.body);
                    }
                }
            }
        }
        
        // OuterArea 처리 (함수 본문에 연결된 블록들)
        var outerChildBlocks = funcBlock.Element("OuterArea")?.Element("childBlocks")?.Elements("Block");
        if (outerChildBlocks != null)
        {
            foreach (var child in outerChildBlocks)
            {
                ProcessLoopBlocksRecursive(child, funcNode.body);
            }
        }
        
        Debug.Log($"[ParseFunctionDefinition] Function '{name}' has {funcNode.parameters.Count} parameters: [{string.Join(", ", funcNode.parameters)}]");
        
        return funcNode;
    }
    
    /// <summary>
    /// 재귀적으로 블록을 처리하여 리스트에 추가 (함수 본문 파싱용)
    /// </summary>
    static void ProcessLoopBlocksRecursive(XElement block, List<LoopBlockNode> loopBlocks)
    {
        var node = ParseBlockToLoopNode(block);
        if (node != null)
        {
            loopBlocks.Add(node);
        }
        else
        {
            // 현재 블록이 Loop 노드가 아닌 경우 내부 섹션 처리
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
                            ProcessLoopBlocksRecursive(child, loopBlocks);
                        }
                    }
                }
            }
        }
        
        // OuterArea 재귀 처리
        var outerChildBlocks = block.Element("OuterArea")?.Element("childBlocks")?.Elements("Block");
        if (outerChildBlocks != null)
        {
            foreach (var child in outerChildBlocks)
                ProcessLoopBlocksRecursive(child, loopBlocks);
        }
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
        
        // 함수 호출 블록
        if (name == "Block Ins CallFunction" || name == "Block Cst CallFunction" ||
            name == "Block Ins FunctionBlock" || name == "Block Cst FunctionBlock")
        {
            return ParseCallFunctionBlock(block);
        }
        
        return null;
    }
    
    /// <summary>
    /// 함수 호출 블록 파싱
    /// </summary>
    static LoopBlockNode ParseCallFunctionBlock(XElement block)
    {
        var node = new LoopBlockNode { type = "callFunction" };
        node.args = new List<float>();  // 미리 초기화
        
        // 디버그: 블록 내용 출력
        Debug.Log($"[ParseCallFunctionBlock] Block XML: {block.ToString().Substring(0, Math.Min(800, block.ToString().Length))}...");
        
        // 1. 먼저 defineID 찾기 (가장 신뢰할 수 있는 함수 이름 소스)
        var defineId = block.Descendants("defineID").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrEmpty(defineId) && !IsNumericOnly(defineId))
        {
            node.functionName = defineId;
            calledFunctionNames.Add(defineId);
            Debug.Log($"[ParseCallFunctionBlock] Found function call from defineID: {defineId}");
        }
        
        // 2. headerInputs에서 함수 이름 찾기 (defineID를 못 찾은 경우)
        if (string.IsNullOrEmpty(node.functionName))
        {
            var headerInputs = block.Element("headerInputs")?.Elements("Input").ToList();
            if (headerInputs != null && headerInputs.Count > 0)
            {
                foreach (var input in headerInputs)
                {
                    // defineID 먼저 확인
                    var inputDefineId = input.Element("defineID")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(inputDefineId) && !IsNumericOnly(inputDefineId))
                    {
                        node.functionName = inputDefineId;
                        calledFunctionNames.Add(inputDefineId);
                        Debug.Log($"[ParseCallFunctionBlock] Found function call from headerInput defineID: {inputDefineId}");
                        break;
                    }
                    
                    // value 확인 (숫자만 있는 값은 스킵)
                    var funcName = input.Element("value")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(funcName) && !IsNumericOnly(funcName))
                    {
                        node.functionName = funcName;
                        calledFunctionNames.Add(funcName);
                        Debug.Log($"[ParseCallFunctionBlock] Found function call from headerInputs value: {funcName}");
                        break;
                    }
                }
            }
        }
        
        if (string.IsNullOrEmpty(node.functionName))
        {
            Debug.LogWarning($"[ParseCallFunctionBlock] Could not extract function name from block!");
        }
        
        // 3. 함수 인자(arguments) 추출 - sections 내부의 inputs에서
        var argSections = block.Element("sections")?.Elements("Section");
        if (argSections != null)
        {
            foreach (var section in argSections)
            {
                var inputs = section.Element("inputs")?.Elements("Input");
                if (inputs != null)
                {
                    foreach (var input in inputs)
                    {
                        var valStr = input.Element("value")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(valStr))
                        {
                            if (float.TryParse(valStr, out float argValue))
                            {
                                node.args.Add(argValue);
                                Debug.Log($"[ParseCallFunctionBlock] Added argument: {argValue}");
                            }
                            else
                            {
                                // 변수 이름일 수 있음 - 변수 값을 해석
                                var resolvedValue = ResolveFloat(valStr);
                                node.args.Add(resolvedValue);
                                Debug.Log($"[ParseCallFunctionBlock] Added argument (resolved): {valStr} -> {resolvedValue}");
                            }
                        }
                    }
                }
            }
        }
        
        Debug.Log($"[ParseCallFunctionBlock] Function '{node.functionName}' has {node.args.Count} arguments");
        
        return node;
    }
    
    /// <summary>
    /// 문자열이 숫자로만 구성되어 있는지 확인 (함수 이름이 아닌 값을 필터링)
    /// </summary>
    static bool IsNumericOnly(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;
        foreach (char c in str)
        {
            if (!char.IsDigit(c) && c != '.' && c != '-')
                return false;
        }
        return true;
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
        
        Debug.Log($"[ParseSectionBlocks] Section has childBlocks: {childBlocks != null}");
        
        if (childBlocks == null) 
        {
            Debug.Log($"[ParseSectionBlocks] No childBlocks found in section");
            return result;
        }
        
        foreach (var child in childBlocks)
        {
            var blockName = child.Element("blockName")?.Value?.Trim();
            Debug.Log($"[ParseSectionBlocks] Processing child block: {blockName}");
            
            var node = ParseBlockToLoopNode(child);
            if (node != null)
            {
                result.Add(node);
                Debug.Log($"[ParseSectionBlocks] Added node of type: {node.type}");
            }
            else
            {
                Debug.LogWarning($"[ParseSectionBlocks] Block '{blockName}' returned null - not recognized");
            }
        }
        
        Debug.Log($"[ParseSectionBlocks] Total blocks parsed: {result.Count}");
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
    
    static string BuildJson(List<VariableNode> variables, List<LoopBlockNode> loopBlocks, List<FunctionNode> functions)
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
        
        // functions 배열 (호출된 함수만)
        sb.AppendLine("  \"functions\": [");
        for (int i = 0; i < functions.Count; i++)
        {
            sb.Append("    ");
            sb.Append(FunctionNodeToJson(functions[i]));
            if (i < functions.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        
        sb.Append("}");
        return sb.ToString();
    }
    
    static string FunctionNodeToJson(FunctionNode func)
    {
        var parts = new List<string>();
        
        // name
        parts.Add($"\"name\": \"{EscapeJson(func.name)}\"");
        
        // params (파라미터 이름 목록)
        if (func.parameters != null && func.parameters.Count > 0)
        {
            var paramStrings = func.parameters.Select(p => $"\"{EscapeJson(p)}\"");
            parts.Add($"\"params\": [{string.Join(", ", paramStrings)}]");
        }
        else
        {
            parts.Add("\"params\": []");
        }
        
        // body
        var bodyParts = new List<string>();
        if (func.body != null)
        {
            foreach (var b in func.body)
                bodyParts.Add(LoopBlockNodeToJson(b));
        }
        parts.Add($"\"body\": [{string.Join(", ", bodyParts)}]");
        
        return $"{{ {string.Join(", ", parts)} }}";
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
            
            case "callFunction":
                parts.Add($"\"functionName\": \"{EscapeJson(node.functionName)}\"");
                // 함수 인자(args) 출력
                if (node.args != null && node.args.Count > 0)
                {
                    var argsStr = string.Join(", ", node.args);
                    parts.Add($"\"args\": [{argsStr}]");
                }
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
        public string type;        // "analogWrite", "if", "ifElse", "callFunction"
        
        // For analogWrite
        public int pin;
        public float value;
        public string valueVar;
        
        // For if/ifElse
        public int conditionPin;
        public int conditionValue;  // Section의 inputs에서 추출한 조건 값
        public List<LoopBlockNode> body;
        public List<LoopBlockNode> elseBody;
        
        // For callFunction
        public string functionName;
        public List<float> args;  // 함수 호출 시 전달하는 인자들
    }
    
    class FunctionNode
    {
        public string name;
        public List<string> parameters;  // 함수 파라미터 이름 목록
        public List<LoopBlockNode> body;
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
