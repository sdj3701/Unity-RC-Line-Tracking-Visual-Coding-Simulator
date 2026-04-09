using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using MG_BlocksEngine2.UI;
using MG_BlocksEngine2.Core;

namespace MG_BlocksEngine2.Environment
{
    // v2.10 - bugfix: hide programmingEnv on WebGl not working properly
    // v2.7 - added a class to the extras that implements the logic for show/hide the Blocks Selection panel  
    public class BE2_HideBlocksSelection : MonoBehaviour
    {
        [System.Serializable]
        struct EnvLayoutState
        {
            public Vector2 anchoredPosition;
            public Vector2 sizeDelta;
            public Vector2 offsetMin;
            public Vector2 offsetMax;
        }

        [Header("Hidden Layout")]
        [SerializeField] float hiddenTop = 137.55f;
        [SerializeField] float hiddenBottom = 0f;
        [SerializeField] float hiddenWidth = 1450f;

        public BE2_Canvas _blocksSelectionCanvas;
        Vector2 _hidePosition;
        Dictionary<RectTransform, EnvLayoutState> _envs = new Dictionary<RectTransform, EnvLayoutState>();

        void Start()
        {
            _blocksSelectionCanvas = GetComponentInParent<BE2_Canvas>();
            _hidePosition = (_blocksSelectionCanvas.transform.GetChild(0) as RectTransform).anchoredPosition;

            GetComponent<Button>().onClick.AddListener(HideBlocksSelection);

            foreach (BE2_UI_SelectionButton button in FindObjectsOfType<BE2_UI_SelectionButton>())
            {
                button.GetComponent<Button>().onClick.AddListener(ShowBlocksSelection);
            }

            foreach (I_BE2_ProgrammingEnv env in BE2_ExecutionManager.Instance.ProgrammingEnvsList)
            {
                RectTransform envRect = env.Transform.GetComponentInParent<BE2_Canvas>().Canvas.transform.GetChild(0) as RectTransform;
                if (envRect && !_envs.ContainsKey(envRect))
                {
                    _envs.Add(envRect, new EnvLayoutState
                    {
                        anchoredPosition = envRect.anchoredPosition,
                        sizeDelta = envRect.sizeDelta,
                        offsetMin = envRect.offsetMin,
                        offsetMax = envRect.offsetMax
                    });
                }
            }
        }

        // void Update()
        // {

        // }

        public void HideBlocksSelection()
        {
            _blocksSelectionCanvas.gameObject.SetActive(false);

            foreach (KeyValuePair<RectTransform, EnvLayoutState> env in _envs)
            {
                RectTransform envRect = env.Key;

                Vector2 anchoredPosition = envRect.anchoredPosition;
                anchoredPosition.x = _hidePosition.x;
                envRect.anchoredPosition = anchoredPosition;

                envRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, hiddenWidth);

                Vector2 offsetMin = envRect.offsetMin;
                offsetMin.y = hiddenBottom;
                envRect.offsetMin = offsetMin;

                Vector2 offsetMax = envRect.offsetMax;
                offsetMax.y = -hiddenTop;
                envRect.offsetMax = offsetMax;
            }
        }

        public void ShowBlocksSelection()
        {
            if (!_blocksSelectionCanvas.gameObject.activeSelf)
            {
                _blocksSelectionCanvas.gameObject.SetActive(true);

                foreach (KeyValuePair<RectTransform, EnvLayoutState> env in _envs)
                {
                    RectTransform envRect = env.Key;
                    EnvLayoutState state = env.Value;

                    envRect.anchoredPosition = state.anchoredPosition;
                    envRect.sizeDelta = state.sizeDelta;
                    envRect.offsetMin = state.offsetMin;
                    envRect.offsetMax = state.offsetMax;
                }
            }
        }
    }
}
