using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

using MG_BlocksEngine2.Utils;

namespace MG_BlocksEngine2.Block
{
    // v2.10 - Dropdown and InputField references in the block header inputs replaced by BE2_Dropdown and BE2_InputField to enable the use of legacy or TMP components
    public class BE2_BlockSectionHeader_InputField : MonoBehaviour, I_BE2_BlockSectionHeaderItem, I_BE2_BlockSectionHeaderInput
    {
        BE2_InputField _inputField;
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
            _inputField = BE2_InputField.GetBE2Component(transform);
            Spot = GetComponent<I_BE2_Spot>();
        }

        void OnEnable()
        {
            UpdateValues();
            _inputField.onEndEdit.AddListener(OnInputEndEdit);
        }

        void OnDisable()
        {
            _inputField.onEndEdit.RemoveListener(OnInputEndEdit);
            _isInitialized = false;  // 재활성화 시 다시 초기화
        }

        // 이전 값 저장 (Undo용)
        private string _previousValue;
        private bool _isInitialized = false;
        
        void OnInputEndEdit(string newValue)
        {
            // 초기화 전이면 무시
            if (!_isInitialized) return;
            
            // 값이 변경되었으면 Undo 저장
            if (_previousValue != newValue)
            {
                if (MG_BlocksEngine2.Core.BE2_KeyboardShortcutManager.Instance != null)
                {
                    MG_BlocksEngine2.Core.BE2_KeyboardShortcutManager.Instance.PushUndoAction(
                        new MG_BlocksEngine2.Core.UndoAction(this, _previousValue)
                    );
                    Debug.Log($"[ValueChange] InputField undo saved: '{_previousValue}' -> '{newValue}'");
                }
                // 새 값을 이전 값으로 업데이트
                _previousValue = newValue;
            }
            UpdateValues();
        }

        void Start()
        {
            // 직렬화 완료 후 현재 값을 이전 값으로 저장
            StartCoroutine(DelayedInit());
        }
        
        IEnumerator DelayedInit()
        {
            // 1프레임 대기하여 직렬화 완료 보장
            yield return null;
            _previousValue = _inputField.text;
            _isInitialized = true;
            UpdateValues();
        }

        public void UpdateValues()
        {
            bool isText;
            string stringValue = "";
            if (_inputField.text != null)
            {
                stringValue = _inputField.text;
            }
            StringValue = stringValue;

            float floatValue = 0;
            try
            {
                floatValue = float.Parse(StringValue, CultureInfo.InvariantCulture);
                isText = false;
            }
            catch
            {
                isText = true;
            }
            FloatValue = floatValue;

            InputValues = new BE2_InputValues(StringValue, FloatValue, isText);
        }
    }
}
