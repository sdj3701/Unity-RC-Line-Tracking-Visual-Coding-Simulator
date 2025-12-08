using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MG_BlocksEngine2.Utils;

namespace MG_BlocksEngine2.UI
{
    public class BE2_UI_ContextMenuManager : MonoBehaviour
    {
        I_BE2_UI_ContextMenu[] _contextMenuArray;
        I_BE2_UI_ContextMenu currentContextMenu;

        // v2.6.2 - bugfix: fixed changes on BE2 Inspector paths not perssiting 
        // UI ContextMenuManager instance changed to property to avoid null exception
        static BE2_UI_ContextMenuManager _instance;
        public static BE2_UI_ContextMenuManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GameObject.FindObjectOfType<BE2_UI_ContextMenuManager>();
                }
                return _instance;
            }
            set => _instance = value;
        }
        public BE2_UI_PanelCancel panelCancel;
        public bool isActive = false;

        void Start()
        {
            _contextMenuArray = new I_BE2_UI_ContextMenu[0];
            foreach (Transform child in transform)
            {
                I_BE2_UI_ContextMenu context = child.GetComponent<I_BE2_UI_ContextMenu>();
                if (context != null)
                    BE2_ArrayUtils.Add(ref _contextMenuArray, context);
            }

            CloseContextMenu();
        }

        public void OpenContextMenu<T>(int menuIndex, T target, params string[] options)
        {
            if (!isActive)
            {
                currentContextMenu = _contextMenuArray[menuIndex];
                currentContextMenu.Open(target, options);
                isActive = true;
                panelCancel.transform.gameObject.SetActive(true);
            }
        }

        public void CloseContextMenu()
        {
            if (isActive)
            {
                if (currentContextMenu != null)
                {
                    currentContextMenu.Close();
                    currentContextMenu = null;
                }
                isActive = false;
                panelCancel.transform.gameObject.SetActive(false);
            }
        }

        public void CodeGenerated()
        {
            var exporter = GameObject.FindObjectOfType<BE2_CodeExporter>();
            bool created = false;
            if (exporter == null)
            {
                var go = new GameObject("BE2_CodeExporter_Auto");
                exporter = go.AddComponent<BE2_CodeExporter>();
                created = true;
            }

            string relativeAssetPath = "Assets/Generated/BlocksGenerated.cs";
            bool success = exporter.SaveScriptToAssets(relativeAssetPath, "BlocksGenerated", "Start");

            string savedPath = exporter != null ? exporter.LastSavedPath : relativeAssetPath;

            if (created && exporter != null)
            {
                DestroyImmediate(exporter.gameObject);  
            }

            if (success)
            {
                Debug.Log($"Code generated and saved to: {savedPath}");
            }
            else
            {
                Debug.LogWarning("Code generation failed or no blocks found.");
            }

            CloseContextMenu();
        }

        // XML Code Generated 코드 임포트
        public void XMLCodeGenerated()
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
            bool success = exporter.SaveXmlToAssets(relativeAssetPath);

            string savedPath = exporter != null ? exporter.LastSavedPath : relativeAssetPath;

            if (created && exporter != null)
            {
                DestroyImmediate(exporter.gameObject);
            }

            if (success)
            {
                Debug.Log($"Blocks XML generated and saved to: {savedPath}");
            }
            else
            {
                Debug.LogWarning("Blocks XML generation failed or no blocks found.");
            }

            CloseContextMenu();
        }
    }
}