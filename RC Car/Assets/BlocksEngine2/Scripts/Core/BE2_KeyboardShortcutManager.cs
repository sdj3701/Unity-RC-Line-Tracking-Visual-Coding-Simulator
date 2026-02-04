using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Utils;
using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Serializer;

namespace MG_BlocksEngine2.Core
{
    /// <summary>
    /// 실행 취소 가능한 작업 타입
    /// </summary>
    public enum UndoActionType
    {
        Delete,       // 블록 삭제 (Undo 시 복원)
        Create,       // 블록 생성 (Undo 시 삭제)
        Paste,        // 블록 붙여넣기 (Undo 시 삭제)
        Move,         // 블록 위치 이동 (Undo 시 원위치로)
        ValueChange   // 블록 내 값 변경 (Undo 시 이전 값으로)
    }

    /// <summary>
    /// 실행 취소 작업 정보를 저장하는 구조체
    /// </summary>
    public struct UndoAction
    {
        public UndoActionType ActionType;
        public string BlockXml;           // 삭제된 블록의 XML (Delete 작업용)
        public I_BE2_Block CreatedBlock;  // 생성된 블록 참조 (Create/Paste/Move 작업용)
        public Vector3 Position;          // 블록 위치
        public Transform Parent;          // 부모 Transform
        
        // Move Undo용 추가 필드
        public Transform OriginalParent;  // Move 전 원래 부모
        public int SiblingIndex;          // 형제 순서 (블록 연결 순서 복원용)
        
        // ValueChange Undo용 추가 필드
        public Component InputComponent;  // InputField 또는 Dropdown 컴포넌트
        public string OldValue;           // 변경 전 값
        public int OldDropdownIndex;      // 드롭다운의 경우 인덱스

        public UndoAction(UndoActionType type, string xml = null, I_BE2_Block block = null, Vector3 pos = default, Transform parent = null, Transform originalParent = null, int siblingIndex = -1)
        {
            ActionType = type;
            BlockXml = xml;
            CreatedBlock = block;
            Position = pos;
            Parent = parent;
            OriginalParent = originalParent;
            SiblingIndex = siblingIndex;
            InputComponent = null;
            OldValue = null;
            OldDropdownIndex = -1;
        }
        
        // ValueChange용 생성자
        public UndoAction(Component inputComponent, string oldValue, int oldDropdownIndex = -1)
        {
            ActionType = UndoActionType.ValueChange;
            BlockXml = null;
            CreatedBlock = null;
            Position = default;
            Parent = null;
            OriginalParent = null;
            SiblingIndex = -1;
            InputComponent = inputComponent;
            OldValue = oldValue;
            OldDropdownIndex = oldDropdownIndex;
        }
    }

    /// <summary>
    /// 블록에 대한 키보드 단축키를 처리하는 매니저
    /// - Ctrl+C: 선택된 블록 복사
    /// - Ctrl+V: 클립보드 블록 붙여넣기
    /// - Ctrl+Z: 실행 취소
    /// - Delete: 선택된 블록 삭제
    /// </summary>
    public class BE2_KeyboardShortcutManager : MonoBehaviour
    {
        // 싱글톤 인스턴스
        public static BE2_KeyboardShortcutManager Instance { get; private set; }

        // 현재 선택된 블록 (마우스 클릭으로 선택)
        private I_BE2_Block _selectedBlock;

        // 클립보드에 복사된 블록
        private I_BE2_Block _clipboardBlock;

        // DragDropManager 참조
        private BE2_DragDropManager _dragDropManager;

        // Undo 스택 (최대 50개 작업 저장)
        private Stack<UndoAction> _undoStack = new Stack<UndoAction>();
        private const int MAX_UNDO_COUNT = 50;
        
        // Undo 실행 중 플래그 (중복 저장 방지)
        private bool _isUndoing = false;
        public bool IsUndoing => _isUndoing;

        // 붙여넣기 오프셋 추적
        private int _pasteCount = 0;
        private const float PASTE_OFFSET_X = 20f;   // 오른쪽으로 이동
        private const float PASTE_OFFSET_Y = 20f;  // 아래쪽으로 이동 (UI좌표계)

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            _dragDropManager = BE2_DragDropManager.Instance;
        }

        void OnEnable()
        {
            // 마우스 클릭 이벤트 구독 - 블록 선택 추적
            BE2_MainEventsManager.Instance.StartListening(BE2EventTypes.OnPrimaryKeyDown, OnPrimaryKeyDown);
        }

        void OnDisable()
        {
            BE2_MainEventsManager.Instance.StopListening(BE2EventTypes.OnPrimaryKeyDown, OnPrimaryKeyDown);
        }

        /// <summary>
        /// 마우스 클릭 시 선택된 블록 저장
        /// Raycaster를 직접 사용하여 클릭 위치에서 블록 찾기
        /// </summary>
        void OnPrimaryKeyDown()
        {
            // DragDropManager의 Raycaster를 사용하여 클릭 위치에서 블록 찾기
            if (_dragDropManager != null && _dragDropManager.Raycaster != null)
            {
                I_BE2_Drag drag = _dragDropManager.Raycaster.GetDragAtPosition(BE2_InputManager.Instance.ScreenPointerPosition);
                if (drag != null && drag.Block != null)
                {
                    _selectedBlock = drag.Block;
                    Debug.Log($"[Shortcut] Block selected: {_selectedBlock.Instruction.GetType().Name}");
                }
            }
        }

        void Update()
        {
            // InputField에 포커스가 있으면 단축키 무시
            if (EventSystem.current != null && 
                EventSystem.current.currentSelectedGameObject != null)
            {
                var inputField = EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
                var legacyInputField = EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
                
                if (inputField != null || legacyInputField != null)
                {
                    return; // InputField 입력 중에는 단축키 무시
                }
            }

            // Ctrl 키가 눌린 상태인지 확인
            bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // Ctrl+C: 복사
            if (ctrlPressed && Input.GetKeyDown(KeyCode.C))
            {
                CopyBlock();
            }
            // Ctrl+V: 붙여넣기
            else if (ctrlPressed && Input.GetKeyDown(KeyCode.V))
            {
                PasteBlock();
            }
            // Ctrl+Z: 실행 취소
            else if (ctrlPressed && Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
            }
            // Delete: 삭제
            else if (Input.GetKeyDown(KeyCode.Delete))
            {
                DeleteBlock();
            }
        }

        /// <summary>
        /// Ctrl+C: 선택된 블록을 클립보드에 복사
        /// </summary>
        public void CopyBlock()
        {
            if (_selectedBlock != null && _selectedBlock.Transform != null)
            {
                _clipboardBlock = _selectedBlock;
                _pasteCount = 0;  // 복사 시 붙여넣기 카운터 리셋
                Debug.Log($"[Shortcut] Block copied: {_selectedBlock.Instruction.GetType().Name}");
            }
            else
            {
                Debug.Log("[Shortcut] No block selected to copy");
            }
        }

        /// <summary>
        /// Ctrl+V: 클립보드 블록을 붙여넣기 (모든 자식 포함)
        /// </summary>
        public void PasteBlock()
        {
            if (_clipboardBlock != null && _clipboardBlock.Transform != null)
            {
                _pasteCount++;
                
                // 복제 후 오프셋 적용
                I_BE2_ProgrammingEnv programmingEnv = _clipboardBlock.Transform.GetComponentInParent<I_BE2_ProgrammingEnv>();
                BE2_SerializableBlock serializableBlock = BE2_BlocksSerializer.BlockToSerializable(_clipboardBlock);
                I_BE2_Block newBlock = BE2_BlocksSerializer.SerializableToBlock(serializableBlock, programmingEnv);
                
                if (newBlock != null)
                {
                    // 누적 오프셋 적용 (오른쪽 아래로)
                    Vector3 offset = new Vector3(PASTE_OFFSET_X * _pasteCount, PASTE_OFFSET_Y * _pasteCount, 0);
                    newBlock.Transform.position = _clipboardBlock.Transform.position + offset;
                    
                    if (newBlock.Type == BlockTypeEnum.trigger)
                    {
                        BE2_ExecutionManager.Instance.AddToBlocksStackArray(
                            newBlock.Instruction.InstructionBase.BlocksStack, 
                            programmingEnv.TargetObject
                        );
                    }
                    
                    // Undo 스택에 붙여넣기 작업 저장
                    PushUndoAction(new UndoAction(UndoActionType.Paste, null, newBlock, newBlock.Transform.localPosition, newBlock.Transform.parent));
                    
                    // 붙여넣기된 블록을 "기존 블록"으로 표시 (이후 이동 시 Move Undo가 저장되도록)
                    var dragBlock = newBlock.Transform.GetComponent<DragDrop.BE2_DragBlock>();
                    if (dragBlock != null)
                    {
                        dragBlock.SetAsExistingBlock();
                    }
                    
                    Debug.Log($"[Shortcut] Block pasted with offset ({_pasteCount}), Undo stack count: {_undoStack.Count}");
                }
            }
            else
            {
                Debug.Log("[Shortcut] No block in clipboard to paste");
            }
        }

        /// <summary>
        /// Delete: 선택된 블록 삭제 (Undo 스택에 저장)
        /// </summary>
        public void DeleteBlock()
        {
            if (_selectedBlock != null && _selectedBlock.Transform != null)
            {
                // 삭제하기 전에 참조 저장
                I_BE2_Block blockToDelete = _selectedBlock;
                _selectedBlock = null;

                // Undo를 위해 블록 정보 저장
                // Undo를 위해 블록 정보 저장
                try
                {
                    BE2_SerializableBlock serializableBlock = BE2_BlocksSerializer.BlockToSerializable(blockToDelete);
                    string blockXml = BE2_BlocksSerializer.SerializableToXML(serializableBlock);
                    Vector3 position = blockToDelete.Transform.localPosition;
                    Transform parent = blockToDelete.Transform.parent;

                    PushUndoAction(new UndoAction(UndoActionType.Delete, blockXml, null, position, parent));
                    Debug.Log($"[Shortcut] Block saved to undo stack. Stack count: {_undoStack.Count}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Shortcut] Failed to save block for undo: {e.Message}\n{e.StackTrace}");
                }

                // 클립보드 블록도 삭제되는 경우 클리어
                if (_clipboardBlock == blockToDelete)
                {
                    _clipboardBlock = null;
                }

                BE2_BlockUtils.RemoveBlock(blockToDelete);
                Debug.Log("[Shortcut] Block deleted");
            }
            else
            {
                Debug.Log("[Shortcut] No block selected to delete");
            }
        }

        /// <summary>
        /// Ctrl+Z: 마지막 작업 실행 취소
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count == 0)
            {
                Debug.Log("[Shortcut] Nothing to undo");
                return;
            }

            _isUndoing = true;  // Undo 실행 중 플래그 설정

            UndoAction action = _undoStack.Pop();

            switch (action.ActionType)
            {
                case UndoActionType.Delete:
                    // 삭제된 블록 복원
                    if (!string.IsNullOrEmpty(action.BlockXml) && action.Parent != null)
                    {
                        try
                        {
                            // XML에서 블록 복원
                            I_BE2_ProgrammingEnv programmingEnv = action.Parent.GetComponentInParent<I_BE2_ProgrammingEnv>();
                            if (programmingEnv != null)
                            {
                                BE2_BlocksSerializer.XMLToBlocksCode(action.BlockXml, programmingEnv);
                                Debug.Log("[Shortcut] Block restored (Undo delete)");
                            }
                            else
                            {
                                Debug.LogWarning("[Shortcut] Cannot restore block: Programming environment not found");
                            }
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"[Shortcut] Failed to restore block: {e.Message}");
                        }
                    }
                    break;

                case UndoActionType.Create:
                case UndoActionType.Paste:
                    // 생성된 블록 삭제
                    if (action.CreatedBlock != null && action.CreatedBlock.Transform != null)
                    {
                        BE2_BlockUtils.RemoveBlock(action.CreatedBlock);
                        Debug.Log("[Shortcut] Block removed (Undo create/paste)");
                    }
                    break;

                case UndoActionType.Move:
                    // 이동된 블록을 원위치로 복원 (위치만)
                    if (action.CreatedBlock != null && action.CreatedBlock.Transform != null && action.OriginalParent != null)
                    {
                        action.CreatedBlock.Transform.SetParent(action.OriginalParent);
                        action.CreatedBlock.Transform.localPosition = action.Position;
                        
                        if (action.SiblingIndex >= 0)
                        {
                            action.CreatedBlock.Transform.SetSiblingIndex(action.SiblingIndex);
                        }
                        
                        action.CreatedBlock.Instruction.InstructionBase.UpdateTargetObject();
                        Debug.Log("[Shortcut] Block moved back to original position (Undo move)");
                    }
                    break;

                case UndoActionType.ValueChange:
                    // 값 변경 되돌리기
                    if (action.InputComponent != null)
                    {
                        // InputField인 경우
                        var inputField = Utils.BE2_InputField.GetBE2Component(action.InputComponent.transform);
                        if (inputField != null && !inputField.isNull)
                        {
                            inputField.text = action.OldValue;
                            Debug.Log($"[Shortcut] InputField value restored to: {action.OldValue}");
                            
                            // UpdateValues 호출
                            var headerInput = action.InputComponent.GetComponent<I_BE2_BlockSectionHeaderInput>();
                            if (headerInput != null)
                            {
                                headerInput.UpdateValues();
                            }
                            break;
                        }
                        
                        // Dropdown인 경우
                        var dropdown = Utils.BE2_Dropdown.GetBE2Component(action.InputComponent.transform);
                        if (dropdown != null && !dropdown.isNull)
                        {
                            if (action.OldDropdownIndex >= 0)
                            {
                                dropdown.value = action.OldDropdownIndex;
                            }
                            Debug.Log($"[Shortcut] Dropdown value restored to index: {action.OldDropdownIndex} ({action.OldValue})");
                            
                            // UpdateValues 호출
                            var headerInput = action.InputComponent.GetComponent<I_BE2_BlockSectionHeaderInput>();
                            if (headerInput != null)
                            {
                                headerInput.UpdateValues();
                            }
                        }
                    }
                    break;
            }
            
            _isUndoing = false;  // Undo 완료
        }

        /// <summary>
        /// Undo 스택에 작업 추가
        /// </summary>
        public void PushUndoAction(UndoAction action)
        {
            // Undo 실행 중에는 새로운 Undo 저장 무시
            if (_isUndoing)
            {
                Debug.Log("[Shortcut] Ignoring undo action during undo execution");
                return;
            }
            
            // 스택 크기 제한
            if (_undoStack.Count >= MAX_UNDO_COUNT)
            {
                // 오래된 항목 제거 (Stack을 List로 변환 후 처리)
                var tempList = new List<UndoAction>(_undoStack);
                tempList.RemoveAt(tempList.Count - 1);  // 가장 오래된 항목 제거
                _undoStack.Clear();
                for (int i = tempList.Count - 1; i >= 0; i--)
                {
                    _undoStack.Push(tempList[i]);
                }
            }

            _undoStack.Push(action);
        }

        /// <summary>
        /// Undo 스택 초기화
        /// </summary>
        public void ClearUndoStack()
        {
            _undoStack.Clear();
        }

        /// <summary>
        /// 외부에서 블록 선택 설정 (필요 시)
        /// </summary>
        public void SetSelectedBlock(I_BE2_Block block)
        {
            _selectedBlock = block;
        }

        /// <summary>
        /// 현재 선택된 블록 반환
        /// </summary>
        public I_BE2_Block GetSelectedBlock()
        {
            return _selectedBlock;
        }

        /// <summary>
        /// 클립보드 블록 반환
        /// </summary>
        public I_BE2_Block GetClipboardBlock()
        {
            return _clipboardBlock;
        }
    }
}
