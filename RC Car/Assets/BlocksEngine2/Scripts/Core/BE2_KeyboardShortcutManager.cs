using UnityEngine;
using UnityEngine.EventSystems;

using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Utils;
using MG_BlocksEngine2.Core;

namespace MG_BlocksEngine2.Core
{
    /// <summary>
    /// 블록에 대한 키보드 단축키를 처리하는 매니저
    /// - Ctrl+C: 선택된 블록 복사
    /// - Ctrl+V: 클립보드 블록 붙여넣기
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
                BE2_BlockUtils.DuplicateBlock(_clipboardBlock);
                Debug.Log($"[Shortcut] Block pasted: {_clipboardBlock.Instruction.GetType().Name}");
            }
            else
            {
                Debug.Log("[Shortcut] No block in clipboard to paste");
            }
        }

        /// <summary>
        /// Delete: 선택된 블록 삭제
        /// </summary>
        public void DeleteBlock()
        {
            if (_selectedBlock != null && _selectedBlock.Transform != null)
            {
                // 삭제하기 전에 참조 저장
                I_BE2_Block blockToDelete = _selectedBlock;
                _selectedBlock = null;

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
