using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Utils;
using MG_BlocksEngine2.Attribute;
using System.Linq;
using TMPro;
using MG_BlocksEngine2.EditorScript;
using MG_BlocksEngine2.Block.Instruction;

namespace MG_BlocksEngine2.Serializer
{
    // v2.12 - BE2_BlocksSerializer refactored to enable Function Blocks
    public static class BE2_BlocksSerializer
    {
        // v2.11 - BE2_BlocksSerializer.SaveCode refactored to use the BlocksCodeToXML method
        // v2.3 - added method SaveCode to facilitate the save of code by script
        public static void SaveCode(string path, I_BE2_ProgrammingEnv targetProgrammingEnv)
        {
            StreamWriter sw = new StreamWriter(path, false);
            sw.WriteLine(BlocksCodeToXML(targetProgrammingEnv));
            sw.Close();

            // v2.10.2 - bugfix: WebGL saves data not persisting after page reload
            PlayerPrefs.SetString("forceSave", string.Empty);
            PlayerPrefs.Save();
        }

        // v2.11 - added method BE2_BlocksSerializer.BlocksCodeToXML to make it possible to save or send the code XML string without the need for generating a .BE2 file 
        public static string BlocksCodeToXML(I_BE2_ProgrammingEnv targetProgrammingEnv)
        {
            string xmlString = "";

            targetProgrammingEnv.UpdateBlocksList();
            // v2.12 - the serialized blocks are reordered by placing the Define Function blocks first on the file
            // to guarantee that related Function Blocks are deserialization correctly
            List<I_BE2_Block> orderedBlocks = new List<I_BE2_Block>();
            orderedBlocks.AddRange(targetProgrammingEnv.BlocksList.OrderBy(OrderOnType));
            foreach (I_BE2_Block block in orderedBlocks)
            {
                xmlString += SerializableToXML(BlockToSerializable(block));
                xmlString += "\n#\n";
            }

            return xmlString;
        }

        // v2.12 - function to define the order of the serialized blocks
        private static int OrderOnType(I_BE2_Block block)
        {
            if (block.Type == BlockTypeEnum.define)
                return 0;

            return 1;
        }


        // v2.9 - BlockToSerializable refactored to enable and facilitate the addition of custom variable types
        public static BE2_SerializableBlock BlockToSerializable(I_BE2_Block block)
        {
            BE2_SerializableBlock serializableBlock = new BE2_SerializableBlock();

            serializableBlock.blockName = block.Transform.name;
            // v2.4 - bugfix: fixed blocks load in wrong position if resolution changes
            serializableBlock.position = block.Transform.localPosition;

            System.Type instructionType = block.Instruction.GetType();
            SerializeAsVariableAttribute varAttribute = (SerializeAsVariableAttribute)System.Attribute.GetCustomAttribute(instructionType, typeof(SerializeAsVariableAttribute));

            if (varAttribute != null)
            {
                System.Type varManagerType = varAttribute.variablesManagerType;

                serializableBlock.varManagerName = varManagerType.ToString();

                // v2.1 - using BE2_Text to enable usage of Text or TMP components
                // BE2_Text varName = BE2_Text.GetBE2Text(block.Transform.GetChild(0).GetChild(0).GetChild(0));
                // serializableBlock.varName = varName.text;

                // FIX: Search for the actual variable variable item in the header, skipping static labels (e.g. "Set", "Change")
                string foundVarName = "Variable";
                if (block.Layout.SectionsArray.Length > 0)
                {
                    foreach (var item in block.Layout.SectionsArray[0].Header.ItemsArray)
                    {
                        // Skip static labels
                        if (item.Transform.GetComponent<BE2_BlockSectionHeader_Label>() != null) continue;

                        // Found the first non-label item (likely the variable Dropdown or Input)
                        BE2_Text textComp = BE2_Text.GetBE2Text(item.Transform);
                        if (textComp != null)
                        {
                            foundVarName = textComp.text;
                            break;
                        }
                    }
                }
                serializableBlock.varName = foundVarName;
            }
            else
            {
                serializableBlock.varManagerName = "";
            }

            // v2.12 - serializer Function Blocks
            if (instructionType == typeof(BE2_Op_FunctionLocalVariable))
            {
                BE2_Text varName = BE2_Text.GetBE2Text(block.Transform.GetChild(0).GetChild(0).GetChild(0));
                serializableBlock.varName = varName.text;
                serializableBlock.isLocalVar = "true";
            }

            if (instructionType == typeof(BE2_Ins_FunctionBlock) || block.Type == BlockTypeEnum.define)
            {
                BE2_Ins_FunctionBlock functionBlock = block.Instruction as BE2_Ins_FunctionBlock;
                if (functionBlock != null)
                    serializableBlock.defineID = functionBlock.defineID;

                BE2_Ins_DefineFunction defineBlock = block.Instruction as BE2_Ins_DefineFunction;
                if (defineBlock != null)
                {
                    serializableBlock.defineID = defineBlock.defineID;
                    serializableBlock.defineItems = new List<DefineItem>();
                    int i = 0;
                    foreach (I_BE2_BlockSectionHeaderItem item in block.Layout.SectionsArray[0].Header.ItemsArray)
                    {
                        if (item.Transform.name.Contains("[FixedLabel]"))
                        {
                            i++;
                            continue;
                        }

                        BE2_BlockSectionHeader_Label labelItem = item.Transform.GetComponent<BE2_BlockSectionHeader_Label>();
                        BE2_BlockSectionHeader_InputField inputFieldItem = item.Transform.GetComponent<BE2_BlockSectionHeader_InputField>();
                        BE2_BlockSectionHeader_LocalVariable localVariableItem = item.Transform.GetComponent<BE2_BlockSectionHeader_LocalVariable>();
                        BE2_BlockSectionHeader_Custom customItem = item.Transform.GetComponent<BE2_BlockSectionHeader_Custom>();

                        if (labelItem)
                        {
                            serializableBlock.defineItems.Add(new DefineItem("label", item.Transform.GetComponent<TMP_Text>().text));
                        }
                        else if (inputFieldItem || localVariableItem)
                        {
                            serializableBlock.defineItems.Add(new DefineItem("variable", item.Transform.GetComponentInChildren<TMP_Text>().text));
                        }
                        else if (customItem)
                        {
                            serializableBlock.defineItems.Add(new DefineItem("custom", customItem.serializableValue));
                        }

                        i++;
                    }
                }
            }

            foreach (I_BE2_BlockSection section in block.Layout.SectionsArray)
            {
                BE2_SerializableSection serializableSection = new BE2_SerializableSection();
                serializableBlock.sections.Add(serializableSection);

                foreach (I_BE2_BlockSectionHeaderInput input in section.Header.InputsArray)
                {
                    BE2_SerializableInput serializableInput = new BE2_SerializableInput();
                    serializableSection.inputs.Add(serializableInput);

                    I_BE2_Block inputBlock = input.Transform.GetComponent<I_BE2_Block>();
                    if (inputBlock != null)
                    {
                        serializableInput.isOperation = true;
                        serializableInput.operation = BlockToSerializable(inputBlock);

                        serializableInput.value = input.InputValues.stringValue;
                    }
                    else
                    {
                        serializableInput.isOperation = false;
                        serializableInput.value = input.InputValues.stringValue;
                    }
                }

                // v2.12 - condition to not serialize blocks child of Function Blocks (No View blocks), they are recreated automatically
                if (section.Body != null && block.Instruction.GetType() != typeof(BE2_Ins_FunctionBlock))
                {
                    foreach (I_BE2_Block childBlock in section.Body.ChildBlocksArray)
                    {
                        serializableSection.childBlocks.Add(BlockToSerializable(childBlock));
                    }
                }
            }

            // v2.13 - serialize the outer area and its children
            BE2_SerializableOuterArea serializableOuterArea = new BE2_SerializableOuterArea();
            // if (block.Instruction.GetType() != typeof(BE2_Ins_FunctionBlock) && block.Type != BlockTypeEnum.trigger)
            // {
            if (block.Layout.OuterArea != null)
            {
                foreach (I_BE2_Block childBlock in block.Layout.OuterArea.childBlocksArray)
                {
                    serializableOuterArea.childBlocks.Add(BlockToSerializable(childBlock));
                }
                // }

            }
            serializableBlock.outerArea = serializableOuterArea;


            return serializableBlock;
        }

        public static string SerializableToXML(BE2_SerializableBlock serializableBlock)
        {
            // JsonUtility has a depth limitation but you can use another Json alternative
            return BE2_BlockXML.SBlockToXElement(serializableBlock).ToString();
        }

        // v2.11 - BE2_BlocksSerializer.LoadCode refactored to use the XMLToBlocksCode method
        // v2.3 - added method LoadCode to facilitate the load of code by script
        public static bool LoadCode(string path, I_BE2_ProgrammingEnv targetProgrammingEnv)
        {
            if (File.Exists(path))
            {
                var sr = new StreamReader(path);
                string xmlCode = sr.ReadToEnd();
                sr.Close();

                XMLToBlocksCode(xmlCode, targetProgrammingEnv);

                return true;
            }

            return false;
        }

        // v2.11 - added method BE2_BlocksSerializer.XMLToBlocksCode to make it possible to load code from a XML string without the need for a .BE2 file 
        public static void XMLToBlocksCode(string xmlString, I_BE2_ProgrammingEnv targetProgrammingEnv)
        {
            string[] xmlBlocks = xmlString.Split('#');

            // === 1차 순회: 모든 변수를 먼저 등록 (Dropdown 초기화 문제 방지) ===
            // 참고: 변수 블록은 자식 블록으로 중첩되어 있을 수 있으므로 재귀적으로 검색
            Debug.Log("[1차 순회] 변수 등록 시작...");
            int varCount = 0;
            foreach (string xmlBlock in xmlBlocks)
            {
                BE2_SerializableBlock serializableBlock = XMLToSerializable(xmlBlock);
                if (serializableBlock != null)
                {
                    RegisterVariablesRecursive(serializableBlock, ref varCount);
                }
            }
            Debug.Log($"[1차 순회] 변수 등록 완료. 총 {varCount}개 변수 등록됨.");

            // === 2차 순회: 블록 생성 ===
            foreach (string xmlBlock in xmlBlocks)
            {
                BE2_SerializableBlock serializableBlock = XMLToSerializable(xmlBlock);

                // v2.12 - check if the serialized Define Function Block already exists on scene
                bool serializeBlock = true;
                if (serializableBlock != null && serializableBlock.blockName == "Block Ins DefineFunction")
                {
                    targetProgrammingEnv.UpdateBlocksList();
                    BE2_Ins_DefineFunction define = default;
                    foreach (I_BE2_Block envBlock in targetProgrammingEnv.BlocksList)
                    {
                        define = envBlock.Instruction as BE2_Ins_DefineFunction;
                        if (define != null)
                        {
                            if (define.defineID == serializableBlock.defineID)
                            {
                                serializeBlock = false;
                                break;
                            }
                        }
                    }
                }

                if (serializeBlock)
                    SerializableToBlock(serializableBlock, targetProgrammingEnv);
            }
        }

        // 재귀적으로 모든 중첩 블록에서 변수를 찾아 등록하는 헬퍼 함수
        private static void RegisterVariablesRecursive(BE2_SerializableBlock serializableBlock, ref int varCount)
        {
            if (serializableBlock == null)
                return;

            // 현재 블록이 변수 블록인지 확인
            if (!string.IsNullOrEmpty(serializableBlock.varManagerName))
            {
                Debug.Log($"[1차 순회] 변수 블록 발견: {serializableBlock.blockName}, varName: {serializableBlock.varName}");
                
                System.Type varManagerType = System.Type.GetType(serializableBlock.varManagerName);
                if (varManagerType != null)
                {
                    I_BE2_VariablesManager varManager = MonoBehaviour.FindObjectOfType(varManagerType) as I_BE2_VariablesManager;
                    if (varManager != null)
                    {
                        Debug.Log($"[1차 순회] CreateAndAddVarToPanel 호출: {serializableBlock.varName}");
                        varManager.CreateAndAddVarToPanel(serializableBlock.varName);
                        varCount++;
                    }
                }
            }

            // sections의 모든 자식 블록 순회
            if (serializableBlock.sections != null)
            {
                foreach (var section in serializableBlock.sections)
                {
                    // childBlocks 순회
                    if (section.childBlocks != null)
                    {
                        foreach (var childBlock in section.childBlocks)
                        {
                            RegisterVariablesRecursive(childBlock, ref varCount);
                        }
                    }

                    // inputs의 operation 블록 순회
                    if (section.inputs != null)
                    {
                        foreach (var input in section.inputs)
                        {
                            if (input.isOperation && input.operation != null)
                            {
                                RegisterVariablesRecursive(input.operation, ref varCount);
                            }
                        }
                    }
                }
            }

            // outerArea의 자식 블록 순회
            if (serializableBlock.outerArea != null && serializableBlock.outerArea.childBlocks != null)
            {
                foreach (var childBlock in serializableBlock.outerArea.childBlocks)
                {
                    RegisterVariablesRecursive(childBlock, ref varCount);
                }
            }
        }

        public static BE2_SerializableBlock XMLToSerializable(string blockString)
        {
            // v2.2 - bugfix: fixed empty blockString from XML file causing error on load
            blockString = blockString.Trim();
            if (blockString.Length > 1)
            {
                // JsonUtility has a depth limitation but you can use another Json alternative
                XElement xBlock = XElement.Parse(blockString);
                return BE2_BlockXML.XElementToSBlock(xBlock);
            }
            else
            {
                return null;
            }
        }

        // v2.13 - name changed from C_AddInputs to C_AddInputsAndChildBlocks
        // v2.12.1 - added counter variable in the serializer to chech end of serialization of all inputs
        static int counterForEndOfDeserialization = 0;
        // 메인 블록의 자식 블록 추가 기능
        static IEnumerator C_AddInputsAndChildBlocks(I_BE2_Block block, BE2_SerializableBlock serializableBlock, I_BE2_ProgrammingEnv programmingEnv)
        {
            yield return new WaitForEndOfFrame();

            I_BE2_BlockSection[] sections = block.Layout.SectionsArray;

            for (int s = 0; s < sections.Length; s++)
            {
                // v2.12 - deserialize local variables of Function Block definitions  
                if (block.Instruction.GetType() != typeof(BE2_Ins_DefineFunction))
                {
                    I_BE2_BlockSectionHeaderInput[] inputs = sections[s].Header.InputsArray;
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        BE2_SerializableInput serializableInput = serializableBlock.sections[s].inputs[i];
                        if (serializableInput.isOperation)
                        {
                            I_BE2_Block operation = SerializableToBlock(serializableInput.operation, programmingEnv);

                            if (operation.Instruction.GetType() == typeof(BE2_Op_FunctionLocalVariable))
                            {
                                operation.Transform.GetComponentInChildren<TMP_Text>().text = serializableInput.value;
                            }

                            BE2_Raycaster.ConnectionPoint connectionPoint = new BE2_Raycaster.ConnectionPoint();
                            connectionPoint.spot = inputs[i].Transform.GetComponent<I_BE2_Spot>();
                            BE2_DragDropManager.Instance.ConnectionPoint = connectionPoint;
                            // BE2_DragDropManager.Instance.CurrentSpot = inputs[i].Transform.GetComponent<I_BE2_Spot>();
                            operation.Transform.GetComponent<I_BE2_Drag>().OnPointerDown();
                            operation.Transform.GetComponent<I_BE2_Drag>().OnPointerUp();
                        }
                        else
                        {

                            // v2.10 - Dropdown and InputField references replaced by BE2_Dropdown and BE2_InputField to enable the use of legacy or TMP components
                            BE2_InputField inputText = BE2_InputField.GetBE2Component(inputs[i].Transform);
                            BE2_Dropdown inputDropdown = BE2_Dropdown.GetBE2Component(inputs[i].Transform);
                            if (inputText != null && !inputText.isNull)
                            {
                                inputText.text = serializableInput.value;
                            }
                            else if (inputDropdown != null && !inputDropdown.isNull)
                            {
                                int idx = inputDropdown.GetIndexOf(serializableInput.value);
                                if (idx >= 0)
                                {
                                    inputDropdown.value = idx;
                                }
                                else
                                {
                                    // 변수가 드롭다운에 없으면 옵션으로 추가 후 선택
                                    inputDropdown.AddOption(serializableInput.value);
                                    inputDropdown.value = inputDropdown.GetOptionsCount() - 1;
                                    inputDropdown.RefreshShownValue();
                                }
                            }
                        }

                        if (serializableBlock.isLocalVar == "true")
                        {
                            //                                        | block        | section   | header    | text      |
                            // BE2_Text newVarName = BE2_Text.GetBE2Text(block.Transform.GetChild(0).GetChild(0).GetChild(0));
                            TMP_Text newVarName = block.Transform.GetChild(0).GetChild(0).GetChild(0).GetComponent<TMP_Text>();
                            newVarName.text = serializableBlock.varName;
                        }

                        inputs[i].UpdateValues();
                    }
                }

                I_BE2_BlockSectionBody body = sections[s].Body;
                if (body != null)
                {
                    // add children
                    foreach (BE2_SerializableBlock serializableChildBlock in serializableBlock.sections[s].childBlocks)
                    {
                        I_BE2_Block childBlock = SerializableToBlock(serializableChildBlock, programmingEnv);
                        childBlock.Transform.SetParent(body.RectTransform);
                    }
                }

                sections[s].Header.UpdateItemsArray();
                sections[s].Header.UpdateInputsArray();
            }

            // v2.13 - deserialize the outer area and children
            BE2_OuterArea outerArea = block.Layout.OuterArea;
            if (outerArea != null)
            {
                // add children
                foreach (BE2_SerializableBlock serializableChildBlock in serializableBlock.outerArea.childBlocks)
                {
                    I_BE2_Block childBlock = SerializableToBlock(serializableChildBlock, programmingEnv);
                    childBlock.Transform.SetParent(outerArea.Transform);
                }
            }

            yield return null;

            counterForEndOfDeserialization--;
        }

        // 메인 블록 생성
        public static I_BE2_Block SerializableToBlock(BE2_SerializableBlock serializableBlock, I_BE2_ProgrammingEnv programmingEnv)
        {
            I_BE2_Block block = null;

            // serializableBlock이 null이면 처리 중단
            if (serializableBlock == null)
            {
                return null;
            }

            // 1. XML에서 블록 이름을 가져와서 프리팹 로드
            string prefabName = serializableBlock.blockName;
            GameObject loadedPrefab = BE2_BlockUtils.LoadPrefabBlock(prefabName);

            if (loadedPrefab == null)
            {
                Debug.LogWarning($"[SerializableToBlock] 프리팹을 찾을 수 없음: {prefabName}");
                return null;
            }

            // 2. 프리팹을 Instantiate하여 블록 GameObject 생성
            GameObject blockGo = MonoBehaviour.Instantiate(
                loadedPrefab,
                serializableBlock.position,
                Quaternion.identity,
                programmingEnv.Transform) as GameObject;

            blockGo.name = prefabName;

            // 3. XML에 저장된 올바른 위치에 배치
            blockGo.transform.localPosition = new Vector3(
                serializableBlock.position.x, 
                serializableBlock.position.y, 
                0);
            blockGo.transform.localEulerAngles = Vector3.zero;

            // 4. I_BE2_Block 인터페이스 가져오기
            block = blockGo.GetComponent<I_BE2_Block>();

            // 5. 변수 블록 처리 (SetVariable, ChangeVariable 등)
            // 참고: 변수 등록은 XMLToBlocksCode의 1차 순회에서 이미 완료됨
            if (!string.IsNullOrEmpty(serializableBlock.varManagerName))
            {
                // 블록 헤더에서 변수명을 표시할 컴포넌트 찾기
                if (block.Layout.SectionsArray.Length > 0)
                {
                    foreach (var item in block.Layout.SectionsArray[0].Header.ItemsArray)
                    {
                        // Label은 스킵 (예: "Set", "Change" 텍스트)
                        if (item.Transform.GetComponent<BE2_BlockSectionHeader_Label>() != null)
                            continue;

                        // TMP_Dropdown 확인 (Block Ins SetVariable 등)
                        TMP_Dropdown dropdown = item.Transform.GetComponent<TMP_Dropdown>();
                        if (dropdown != null)
                        {
                            // 옵션에서 해당 변수명 찾기
                            int idx = -1;
                            for (int i = 0; i < dropdown.options.Count; i++)
                            {
                                if (dropdown.options[i].text == serializableBlock.varName)
                                {
                                    idx = i;
                                    break;
                                }
                            }

                            if (idx >= 0)
                            {
                                dropdown.value = idx;
                                dropdown.RefreshShownValue();
                            }
                            else
                            {
                                // 옵션에 없으면 captionText에 직접 설정
                                dropdown.captionText.text = serializableBlock.varName;
                            }
                            break;
                        }

                        // Dropdown이 아니면 BE2_Text 로직
                        BE2_Text textComp = BE2_Text.GetBE2Text(item.Transform);
                        if (textComp != null)
                        {
                            textComp.text = serializableBlock.varName;
                            break;
                        }
                    }
                }
            }

            // 6. 자식 블록들 생성 (Input의 Operation 블록, Section Body 자식, Outer Area 자식)
            counterForEndOfDeserialization++;
            BE2_ExecutionManager.Instance.StartCoroutine(C_AddInputsAndChildBlocks(block, serializableBlock, programmingEnv));

            // 7. 트리거 블록이면 BlocksStack에 등록
            if (block.Type == BlockTypeEnum.trigger && block.Type != BlockTypeEnum.define)
            {
                BE2_ExecutionManager.Instance.AddToBlocksStackArray(block.Instruction.InstructionBase.BlocksStack, programmingEnv.TargetObject);
                block.Instruction.InstructionBase.BlocksStack.PopulateStack();
            }

            // 7. 사용한 프리팹 언로드 (메모리 해제)
            BE2_BlockUtils.UnloadPrefab();

            return block;
        }

        // v2.12 - all Function Block Instructions are initialized after being loaded to make sure it has a definition set (DefineFunction Instruction)
        static IEnumerator C_InitializeFunctionInstruction(BE2_Ins_FunctionBlock functionInstruction, BE2_Ins_DefineFunction defineInstruction)
        {
            yield return new WaitForEndOfFrame();
            functionInstruction.Initialize(defineInstruction);

            // v2.12.1 - bugfix: Function Blocks not being rebuild right after being loaded
            yield return new WaitUntil(() => counterForEndOfDeserialization == 0);
            functionInstruction.RebuildFunctionInstance();
        }
    }
}
