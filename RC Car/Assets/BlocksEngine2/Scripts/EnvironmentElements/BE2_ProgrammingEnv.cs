using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.UI;

namespace MG_BlocksEngine2.Environment
{
    public class BE2_ProgrammingEnv : MonoBehaviour, I_BE2_ProgrammingEnv
    {
        Transform _transform;
        RectTransform _rectTransform;
        public Transform Transform => _transform ? _transform : transform;
        public List<I_BE2_Block> BlocksList { get; set; }
        public BE2_TargetObject targetObject;
        public I_BE2_TargetObject TargetObject => targetObject;

        // v2.4 - added property to set visibility of Programming Environment, facilitates the use of multiple individualy programmable Target Objects in the same scene 
        [SerializeField]
        bool _visible = true;
        public bool Visible 
        {
            get => _visible;
            set
            {
                _visible = value;

                // v2.6 - bugfix: fixed null exception on play scene without target objects and programming envs
                // v2.4.1 - bugfix: fixed Null Exception on opening the "Target Object and Programming Env" prefabs 
                if (gameObject.scene.name != null && _parentCanvasGroup)
                {
                    if (value)
                    {
                        _parentCanvasGroup.alpha = 1;
                        _parentCanvasGroup.blocksRaycasts = true;
                    }
                    else
                    {
                        _parentCanvasGroup.alpha = 0;
                        _parentCanvasGroup.blocksRaycasts = false;
                    }
                }
            }
        }

        CanvasGroup _parentCanvasGroup;
        GraphicRaycaster _parentGraphicRaycaster;

        void OnValidate()
        {
            _parentCanvasGroup = GetComponentInParent<CanvasGroup>();
            Visible = _visible;
        }

        void Awake()
        {
            // v2.11 - added null check to avoid error if ProgrammingEnv has no TargetObject set
            if (targetObject)
            {
                // v2.5 - sets the ProgrammingEnv reference on the TargetObject
                targetObject.ProgrammingEnv = this;
            }

            _transform = transform;
            _rectTransform = GetComponent<RectTransform>();
            UpdateBlocksList();

            _parentCanvasGroup = GetComponentInParent<CanvasGroup>();
            _parentGraphicRaycaster = _parentCanvasGroup.GetComponent<GraphicRaycaster>();
        }

        void Start()
        {
            BE2_DragDropManager.Instance.Raycaster.AddRaycaster(_parentGraphicRaycaster);
        }

        public void UpdateBlocksList()
        {
            BlocksList = new List<I_BE2_Block>();
            foreach (Transform child in Transform)
            {
                if (child.gameObject.activeSelf)
                {
                    I_BE2_Block childBlock = child.GetComponent<I_BE2_Block>();
                    if (childBlock != null)
                        BlocksList.Add(childBlock);
                }
            }

            // Fallback: if no direct children blocks were found, scan descendants and pick top-level blocks
            if (BlocksList.Count == 0)
            {
                I_BE2_Block[] allBlocks = Transform.GetComponentsInChildren<I_BE2_Block>(true);
                foreach (var block in allBlocks)
                {
                    if (!block.Transform.gameObject.activeInHierarchy)
                        continue;

                    // Top-level heuristic: no ancestor with a BlockSectionBody (i.e., not nested inside another block's body)
                    bool insideBody = block.Transform.GetComponentInParent<I_BE2_BlockSectionBody>() != null && block.Transform.parent != Transform;
                    if (!insideBody)
                    {
                        BlocksList.Add(block);
                    }
                }
                // Optional: keep only unique entries
                // (List is typically small; distinct by Transform instance)
                if (BlocksList.Count > 1)
                {
                    var unique = new List<I_BE2_Block>();
                    var seen = new System.Collections.Generic.HashSet<Transform>();
                    foreach (var b in BlocksList)
                    {
                        if (b != null && seen.Add(b.Transform)) unique.Add(b);
                    }
                    BlocksList = unique;
                }
            }
        }

        public void OpenContextMenu()
        {
            BE2_UI_ContextMenuManager.instance.OpenContextMenu(1, this);
        }

        public void ClearBlocks()
        {
            BlocksList = new List<I_BE2_Block>();
            foreach (Transform child in Transform)
            {
                if (child.gameObject.activeSelf)
                {
                    I_BE2_Block childBlock = child.GetComponent<I_BE2_Block>();
                    if (childBlock != null)
                        Destroy(childBlock.Transform.gameObject);
                }
            }

            UpdateBlocksList();
        }

        public void SaveButton()
        {
            BE2_UI_ContextMenuManager.instance.CodeGenerated();
        }

        public void SaveXMLButton()
        {
            var exporter = GameObject.FindObjectOfType<BE2_CodeExporter>();
            bool created = false;
            if (exporter == null)
            {
                var go = new GameObject("BE2_CodeExporter_Auto");
                exporter = go.AddComponent<BE2_CodeExporter>();
                created = true;
            }

            string relativeAssetPath = "Assets/Generated/BlocksGenerated.be2";
            bool success = exporter.SaveXmlToAssets(this, relativeAssetPath);

            if (created && exporter != null)
            {
                DestroyImmediate(exporter.gameObject);
            }

            if (success)
            {
                Debug.Log("Blocks XML generated for this env.");
            }
            else
            {
                Debug.LogWarning("Blocks XML generation failed or no blocks found in this env.");
            }
        }
    }
}