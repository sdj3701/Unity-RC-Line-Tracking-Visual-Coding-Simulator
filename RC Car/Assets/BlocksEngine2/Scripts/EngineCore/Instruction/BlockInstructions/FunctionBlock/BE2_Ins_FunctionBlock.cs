using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.Utils;
using MG_BlocksEngine2.Serializer;
using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Environment;
using TMPro;

namespace MG_BlocksEngine2.Block.Instruction
{
    // v2.12 - new FunctionBlock instruction
    public class BE2_Ins_FunctionBlock : BE2_InstructionBase, I_BE2_Instruction
    {
        public BE2_Ins_FunctionBlock(BE2_Ins_DefineFunction defineInstruction)
        {
            this.defineInstruction = defineInstruction;
        }

        public string defineID;
        public BE2_Ins_DefineFunction defineInstruction;

        protected override void OnStart()
        {
            if (defineInstruction)
            {
                defineInstruction.InstructionBase.Block.Layout.SectionsArray[0].Body.UpdateLayout();
                defineID = defineInstruction.defineID;
            }

            // v2.12.1 - added localVariables to Function Blocks to hold the input values and enable recursive functions
            localValues = new List<string>();
            foreach (I_BE2_BlockSectionHeaderInput input in Block.Layout.SectionsArray[0].Header.InputsArray)
            {
                localValues.Add("");
            }
        }

        protected override void OnEnableInstruction()
        {
            if (!_initialized)
            {
                // defineInstruction이 null이면 아직 초기화 시점이 아님
                // C_DelayedFunctionBlockInit에서 나중에 Initialize가 호출됨
                if (defineInstruction != null)
                {
                    Initialize(defineInstruction);
                }
                // defineInstruction이 null이면 여기서 아무것도 하지 않음 (정상적인 상황)
            }
            else
            {
                // Just re-register listeners
                if (defineInstruction)
                {
                    defineInstruction.onDefineChange.AddListener(RebuildFunctionInstance);
                }
                BE2_MainEventsManager.Instance.StartListening(BE2EventTypesBlock.OnFunctionDefinitionRemoved, Remove);
            }
        }

        protected override void OnDisableInstruction()
        {
            if (defineInstruction)
                defineInstruction.onDefineChange.RemoveListener(RebuildFunctionInstance);
            
            BE2_MainEventsManager.Instance.StopListening(BE2EventTypesBlock.OnFunctionDefinitionRemoved, Remove);
            // Do NOT reset _initialized = false here, to preserve state through Hot Reload
        }

        [SerializeField]
        bool _initialized = false;
        
        // 프로그래밍 환경 참조 (Operation 블록 생성 시 필요)
        private I_BE2_ProgrammingEnv _programmingEnv;
        
        public void Initialize(BE2_Ins_DefineFunction defineInstruction, List<BE2_SerializableInput> savedInputs = null, I_BE2_ProgrammingEnv programmingEnv = null)
        {
            
            if (!defineInstruction)
            {
                return;
            }

            // defineInstruction을 먼저 할당해야 RebuildFunctionInstance에서 사용 가능
            this.defineInstruction = defineInstruction;
            defineID = defineInstruction.defineID;
            _programmingEnv = programmingEnv;
            
            // FunctionBlock 헤더 아이템 생성 (라벨과 인풋필드) - 저장된 Operation 블록도 전달
            RebuildHeaderItems(savedInputs);
            
            RebuildFunctionInstance();

            defineInstruction.onDefineChange.AddListener(RebuildFunctionInstance);
            BE2_MainEventsManager.Instance.StartListening(BE2EventTypesBlock.OnFunctionDefinitionRemoved, Remove);

            _initialized = true;
        }
        
        /// <summary>
        /// DefineFunction의 헤더 아이템을 복사하여 FunctionBlock 헤더에 생성
        /// </summary>
        /// <param name="savedInputs">저장된 Input 데이터 (역직렬화 시 사용, Operation 블록 포함)</param>
        public void RebuildHeaderItems(List<BE2_SerializableInput> savedInputs = null)
        {
            if (defineInstruction == null || Block == null) return;
            
            I_BE2_BlockLayout instantiatedLayout = Block.Transform.GetComponent<I_BE2_BlockLayout>();
            if (instantiatedLayout == null) return;
            
            Transform headerTransform = instantiatedLayout.SectionsArray[0].Header.RectTransform;
            var defineHeader = defineInstruction.Block.Layout.SectionsArray[0].Header;
            defineHeader.UpdateItemsArray();
            
            int i = 0;
            int inputIndex = 0; // savedInputs 인덱스
            foreach (I_BE2_BlockSectionHeaderItem item in defineHeader.ItemsArray)
            {
                // 첫 번째 아이템(고정 라벨)은 스킵
                if (i == 0)
                {
                    i++;
                    continue;
                }
                
                if (item is BE2_BlockSectionHeader_Label)
                {
                    GameObject label = Instantiate(
                        MG_BlocksEngine2.EditorScript.BE2_Inspector.Instance.LabelTextTemplate, 
                        Vector3.zero, 
                        Quaternion.identity,
                        headerTransform);
                    label.GetComponent<TMP_Text>().text = item.Transform.GetComponent<TMP_Text>().text;
                }
                else if (item is BE2_BlockSectionHeader_LocalVariable)
                {
                    // 저장된 Input 데이터 가져오기
                    BE2_SerializableInput savedInput = null;
                    if (savedInputs != null && inputIndex < savedInputs.Count)
                    {
                        savedInput = savedInputs[inputIndex];
                    }
                    
                    // 항상 InputField를 먼저 생성 (Operation 블록이 연결될 슬롯으로 사용)
                    GameObject input = Instantiate(
                        MG_BlocksEngine2.EditorScript.BE2_Inspector.Instance.InputFieldTemplate, 
                        Vector3.zero, 
                        Quaternion.identity,
                        headerTransform);
                    
                    // 저장된 값이 있으면 설정
                    string inputValue = "";
                    if (savedInput != null && !string.IsNullOrEmpty(savedInput.value))
                    {
                        inputValue = savedInput.value;
                    }
                    input.GetComponent<TMP_InputField>().text = inputValue;
                    
                    // Operation 블록이 있는 경우 (Block Op Variable 등) - InputField에 연결
                    if (savedInput != null && savedInput.isOperation && savedInput.operation != null && _programmingEnv != null)
                    {
                        // Operation 블록을 생성하고 InputField에 연결
                        StartCoroutine(C_CreateOperationBlock(savedInput.operation, headerTransform, inputIndex));
                    }
                    
                    inputIndex++;
                }
                
                i++;
            }
            
            // 헤더 레이아웃 업데이트
            instantiatedLayout.SectionsArray[0].Header.UpdateItemsArray();
            instantiatedLayout.SectionsArray[0].Header.UpdateInputsArray();
        }
        
        /// <summary>
        /// Operation 블록을 생성하여 FunctionBlock 헤더에 연결
        /// </summary>
        IEnumerator C_CreateOperationBlock(BE2_SerializableBlock operationBlock, Transform headerTransform, int inputIndex)
        {
            yield return new WaitForEndOfFrame();
            
            if (_programmingEnv == null) yield break;
            
            // Operation 블록 생성
            I_BE2_Block operation = BE2_BlocksSerializer.SerializableToBlock(operationBlock, _programmingEnv);
            if (operation == null) yield break;
            
            yield return new WaitForEndOfFrame();
            
            // 헤더의 InputsArray 업데이트
            Block.Layout.SectionsArray[0].Header.UpdateInputsArray();
            
            var inputs = Block.Layout.SectionsArray[0].Header.InputsArray;
            if (inputIndex < inputs.Length)
            {
                // 해당 Input 슬롯에 Operation 블록 연결
                var targetInput = inputs[inputIndex];
                if (targetInput != null)
                {
                    BE2_Raycaster.ConnectionPoint connectionPoint = new BE2_Raycaster.ConnectionPoint();
                    connectionPoint.spot = targetInput.Transform.GetComponent<I_BE2_Spot>();
                    BE2_DragDropManager.Instance.ConnectionPoint = connectionPoint;
                    operation.Transform.GetComponent<I_BE2_Drag>().OnPointerDown();
                    operation.Transform.GetComponent<I_BE2_Drag>().OnPointerUp();
                }
            }
            
            // 레이아웃 업데이트
            Block.Layout.SectionsArray[0].Header.UpdateItemsArray();
            Block.Layout.SectionsArray[0].Header.UpdateInputsArray();
        }

        void Remove(I_BE2_Block block)
        {
            if (defineInstruction.Block == block)
            {
                Block.Transform.SetParent(null);
                I_BE2_BlocksStack stack = Block.Instruction.InstructionBase.BlocksStack;
                if (stack != null)
                    stack.PopulateStack();

                BE2_BlockUtils.RemoveBlock(Block);
            }
        }

        public void RebuildFunctionInstance()
        {
            localVariables = new List<BE2_Op_FunctionLocalVariable>();

            StartCoroutine(C_RebuildFunctionInstance());
        }

        IEnumerator C_RebuildFunctionInstance()
        {
            yield return new WaitForEndOfFrame();

            if (Block == null || Block.Layout == null)
            {
                yield break;
            }
            
            if (defineInstruction == null)
            {
                yield break;
            }

            I_BE2_BlockSectionBody body = Block.Layout.SectionsArray[0].Body;
            for (int i = body.ChildBlocksCount - 1; i >= 0; i--)
            {
                if (body.ChildBlocksArray[i] as Object)
                    Destroy(body.ChildBlocksArray[i].Transform.gameObject);
            }

            var defineBody = defineInstruction.Block.Layout.SectionsArray[0].Body;
            int childCount = defineBody.ChildBlocksArray.Length;
            
            foreach (I_BE2_Block childBlock in defineBody.ChildBlocksArray)
            {
                InstantiateNoViewBlockRecursive(childBlock, Block.Layout.SectionsArray[0].Body.RectTransform);
            }
            
        }

        public BE2_Ins_FunctionBlock mirrorFunction;

        public List<BE2_Op_FunctionLocalVariable> localVariables;

        public List<string> localValues = new List<string>();

        void InstantiateNoViewBlockRecursive(I_BE2_Block mirrorBlock, Transform parent)
        {
            if (mirrorBlock is BE2_GhostBlock)
                return;

            I_BE2_Block noViewBlock = default;
            BE2_Ins_ReferenceFunctionBlock refFunction = default;

            if (mirrorBlock.Instruction.GetType() == typeof(BE2_Ins_FunctionBlock) && (mirrorBlock.Instruction as BE2_Ins_FunctionBlock).defineInstruction == defineInstruction)
            {
                mirrorFunction = mirrorBlock.Instruction as BE2_Ins_FunctionBlock;
                noViewBlock = mirrorBlock.InstantiateNoViewBlock<BE2_Ins_ReferenceFunctionBlock>();
                refFunction = noViewBlock.Instruction as BE2_Ins_ReferenceFunctionBlock;
                refFunction.Initialize(Block.Instruction as BE2_Ins_FunctionBlock);
            }
            else
            {
                noViewBlock = mirrorBlock.InstantiateNoViewBlock();
            }

            if (noViewBlock == null)
                return;

            int sectionIndex = 0;
            foreach (I_BE2_BlockSection section in mirrorBlock.Layout.SectionsArray)
            {
                I_BE2_BlockSectionHeader header = section.Header;
                header.UpdateInputsArray();
                I_BE2_BlockSection noViewSection = noViewBlock.Layout.SectionsArray[sectionIndex];
                int inputIndex = 0;
                foreach (I_BE2_BlockSectionHeaderInput input in header.InputsArray)
                {
                    if (input is BE2_BlockSectionHeader_Operation)
                    {
                        I_BE2_Block inputBlock = input.Transform.GetComponent<I_BE2_Block>();
                        InstantiateNoViewBlockRecursive(inputBlock, noViewSection.Header.RectTransform);
                    }
                    else if (input is BE2_BlockSectionHeader_LocalVariable)
                    {
                        I_BE2_Block inputBlock = input.Transform.GetComponent<I_BE2_Block>();
                        InstantiateNoViewBlockRecursive(inputBlock, noViewSection.Header.RectTransform);
                    }
                    else
                    {
                        GameObject nvInputGO = new GameObject("input", typeof(RectTransform));
                        nvInputGO.transform.SetParent(noViewSection.Header.RectTransform);
                        nvInputGO.transform.SetAsLastSibling();

                        BE2_BlockSectionHeader_ReferenceInput nvInput = nvInputGO.AddComponent<BE2_BlockSectionHeader_ReferenceInput>();
                        nvInput.referenceInput = input;
                    }

                    inputIndex++;
                }
                noViewSection.Header.UpdateInputsArray();

                if (!refFunction)
                {
                    I_BE2_BlockSectionBody body = section.Body;
                    if (body != null)
                    {
                        body.UpdateChildBlocksList();
                        int bodyIndex = 0;
                        foreach (I_BE2_Block childBlock in body.ChildBlocksArray)
                        {
                            InstantiateNoViewBlockRecursive(childBlock, noViewSection.Body.RectTransform);

                            bodyIndex++;
                        }
                        noViewSection.Body.UpdateChildBlocksList();
                    }
                }

                sectionIndex++;
            }

            noViewBlock.Transform.SetParent(parent);
            noViewBlock.Transform.SetAsLastSibling();

            if (noViewBlock.Instruction.GetType() == typeof(BE2_Op_FunctionLocalVariable))
            {
                // BE2_Op_FunctionLocalVariable localVariable = noViewBlock.Instruction as BE2_Op_FunctionLocalVariable;
                // localVariable.varName = mirrorBlock.Transform.GetComponentInChildren<TMP_Text>().text;
                // localVariables.Add(localVariable);

                StartCoroutine(C_SetLocalVarName(noViewBlock, mirrorBlock));
            }

            (noViewBlock.Instruction.InstructionBase as BE2_InstructionBase).Initialize();
        }

        // v2.12.1 - bugfix: tmp text not being found when Function Block A was set with local variable inside Define Block B 
        IEnumerator C_SetLocalVarName(I_BE2_Block noViewBlock, I_BE2_Block mirrorBlock)
        {
            yield return new WaitForEndOfFrame();

            BE2_Op_FunctionLocalVariable localVariable = noViewBlock.Instruction as BE2_Op_FunctionLocalVariable;
            TMP_Text tmpText = mirrorBlock.Transform.GetComponentInChildren<TMP_Text>();
            if (tmpText)
            {
                localVariable.varName = tmpText.text;
                localVariables.Add(localVariable);
            }
        }

        public override void OnPrepareToPlay()
        {
            foreach (BE2_Op_FunctionLocalVariable localvar in localVariables)
            {
                localvar.defineInstruction = defineInstruction;
                localvar.blockToObserve = Block as BE2_Block;
            }
        }

        public new void Function()
        {
            for (int i = 0; i < localValues.Count; i++)
            {
                localValues[i] = Section0Inputs[i].StringValue;
            }

            ExecuteSection(0);
        }
    }
}