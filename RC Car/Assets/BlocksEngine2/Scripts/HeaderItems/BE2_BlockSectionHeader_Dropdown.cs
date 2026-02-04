using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

using MG_BlocksEngine2.Utils;

namespace MG_BlocksEngine2.Block
{

    // v2.10 - Dropdown and InputField references in the block header inputs replaced by BE2_Dropdown and BE2_InputField to enable the use of legacy or TMP components
    public class BE2_BlockSectionHeader_Dropdown : MonoBehaviour, I_BE2_BlockSectionHeaderItem, I_BE2_BlockSectionHeaderInput
    {
        BE2_Dropdown _dropdown;
        RectTransform _rectTransform;

        public Transform Transform => transform;
        public Vector2 Size => _rectTransform ? _rectTransform.sizeDelta : GetComponent<RectTransform>().sizeDelta;
        public I_BE2_Spot Spot { get; set; }
        public float FloatValue { get; set; }
        public string StringValue { get; set; }
        public BE2_InputValues InputValues { get; set; }

        void OnValidate()
        {
            Awake();
        }

        void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _dropdown = BE2_Dropdown.GetBE2Component(transform);
            Spot = GetComponent<I_BE2_Spot>();
        }

        void OnEnable()
        {
            UpdateValues();
            if (_dropdown != null)
                _dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        }

        void OnDisable()
        {
            if (_dropdown != null)
                _dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            _isInitialized = false;
        }
        
        // 이전 값 저장 (Undo용)
        private int _previousIndex = -1;
        private string _previousValue = "";
        private bool _isInitialized = false;
        
        void OnDropdownValueChanged(int newIndex)
        {
            // 초기화 전이면 무시
            if (!_isInitialized) return;
            
            // 값이 변경되었으면 Undo 저장
            if (_previousIndex != newIndex && _previousIndex >= 0)
            {
                if (MG_BlocksEngine2.Core.BE2_KeyboardShortcutManager.Instance != null)
                {
                    MG_BlocksEngine2.Core.BE2_KeyboardShortcutManager.Instance.PushUndoAction(
                        new MG_BlocksEngine2.Core.UndoAction(this, _previousValue, _previousIndex)
                    );
                    Debug.Log($"[ValueChange] Dropdown undo saved: index {_previousIndex} -> {newIndex}");
                }
            }
            
            // 현재 값을 이전 값으로 저장
            _previousIndex = newIndex;
            _previousValue = _dropdown.GetOptionsCount() > 0 ? _dropdown.GetSelectedOptionText() : "";
            
            UpdateValues();
        }

        void Start()
        {
            GetComponent<BE2_DropdownDynamicResize>().Resize(0);
            // 직렬화 완료 후 현재 값을 이전 값으로 저장
            StartCoroutine(DelayedInit());
        }
        
        IEnumerator DelayedInit()
        {
            // 1프레임 대기하여 직렬화 완료 보장
            yield return null;
            if (_dropdown != null)
            {
                _previousIndex = _dropdown.value;
                _previousValue = _dropdown.GetOptionsCount() > 0 ? _dropdown.GetSelectedOptionText() : "";
            }
            _isInitialized = true;
            UpdateValues();
        }

        public void UpdateValues()
        {
            bool isText = false;
            if (_dropdown.GetOptionsCount() > 0)
            {
                StringValue = _dropdown.GetSelectedOptionText();
            }
            else
            {
                StringValue = "";
            }

            // v2.12 - dropdown input now returns the index of the selected item as FloatValue
            FloatValue = _dropdown.value;

            InputValues = new BE2_InputValues(StringValue, FloatValue, isText);
        }
    }
}
