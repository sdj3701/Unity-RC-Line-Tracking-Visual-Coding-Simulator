using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

using MG_BlocksEngine2.Utils;
using MG_BlocksEngine2.Storage;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Serializer;

namespace MG_BlocksEngine2.UI
{
    public class BE2_UI_ContextMenuManager : MonoBehaviour
    {
        private I_BE2_UI_ContextMenu[] _contextMenuArray;
        private I_BE2_UI_ContextMenu currentContextMenu;

        /// <summary>
        /// 코드 생성/저장/불러오기가 완료되면 발생하는 이벤트입니다.
        /// </summary>
        public static event System.Action OnCodeGenerated;

        // v2.6.2 - 버그 수정: BE2 Inspector 경로 변경사항이 유지되지 않던 문제 수정
        // Null 예외를 피하기 위해 UI ContextMenuManager 인스턴스 접근을 프로퍼티로 변경
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
        public Image image;

        [Header("Save/Load UI Panels")]
        public BE2_UI_CodeSavePanel codeSavePanel;
        public BE2_UI_CodeLoadPanel codeLoadPanel;

        private void Start()
        {
            _contextMenuArray = new I_BE2_UI_ContextMenu[0];
            foreach (Transform child in transform)
            {
                I_BE2_UI_ContextMenu context = child.GetComponent<I_BE2_UI_ContextMenu>();
                if (context != null)
                {
                    BE2_ArrayUtils.Add(ref _contextMenuArray, context);
                }
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

        #region New Save/Load

        public void OpenSavePanel()
        {
            if (codeSavePanel != null)
            {
                codeSavePanel.Open();
            }
            else
            {
                Debug.LogWarning("[ContextMenuManager] CodeSavePanel is not assigned. Using legacy save.");
                CodeGenerated();
            }
        }

        public void OpenLoadPanel()
        {
            if (codeLoadPanel != null)
            {
                codeLoadPanel.Open();
            }
            else
            {
                Debug.LogWarning("[ContextMenuManager] CodeLoadPanel is not assigned. Using legacy load.");
                XMLCodeGenerated();
            }
        }

        /// <summary>
        /// 비동기 저장소 제공자를 통해 XML + JSON을 저장합니다.
        /// isModified는 기존 XML과 현재 XML이 다를 때만 true입니다.
        /// </summary>
        public async Task<bool> SaveCodeWithNameAsync(string fileName)
        {
            var exporter = GameObject.FindObjectOfType<BE2_CodeExporter>();
            bool created = false;
            if (exporter == null)
            {
                var go = new GameObject("BE2_CodeExporter_Auto");
                exporter = go.AddComponent<BE2_CodeExporter>();
                created = true;
            }

            // XML 문자열 메모리 생성/검증 단계
            string xmlContent = exporter.GenerateXmlFromAllEnvs();

            if (created && exporter != null)
            {
                DestroyImmediate(exporter.gameObject);
            }

            if (string.IsNullOrEmpty(xmlContent))
            {
                Debug.LogWarning("[SaveCodeWithNameAsync] XML generation failed or no blocks found.");
                return false;
            }

            // XML -> JSON 문자열 메모리 생성
            string jsonContent = BE2XmlToRuntimeJson.ExportToString(xmlContent);
            if (string.IsNullOrEmpty(jsonContent))
            {
                Debug.LogWarning("[SaveCodeWithNameAsync] JSON generation failed.");
                return false;
            }

            // 기존 저장본 대비 수정됐느지 판단
            string existingXml = await BE2_CodeStorageManager.Instance.LoadXmlAsync(fileName);
            bool isModified = !string.IsNullOrEmpty(existingXml) &&
                              !string.Equals(existingXml, xmlContent, StringComparison.Ordinal);

            // 파일 생성 및 저장
            bool success = await BE2_CodeStorageManager.Instance.SaveCodeAsync(fileName, xmlContent, jsonContent, isModified);

            if (success)
            {
                Debug.Log($"[SaveCodeWithNameAsync] Saved: {fileName}, isModified={isModified}");
                OnCodeGenerated?.Invoke();

                if (image != null)
                {
                    image.gameObject.SetActive(true);
                }
            }

            return success;
        }

        /// <summary>
        /// 비동기 저장소에서 XML을 불러와 블록을 재구성하고 런타임 JSON을 다시 생성합니다.
        /// </summary>
        public async Task<bool> LoadCodeFromFileAsync(string fileName)
        {
            string xmlContent = await BE2_CodeStorageManager.Instance.LoadXmlAsync(fileName);
            if (string.IsNullOrEmpty(xmlContent))
            {
                Debug.LogWarning($"[LoadCodeFromFileAsync] XML not found: {fileName}");
                return false;
            }

            var envs = GameObject.FindObjectsOfType<BE2_ProgrammingEnv>();
            if (envs == null || envs.Length == 0)
            {
                Debug.LogWarning("[LoadCodeFromFileAsync] No BE2_ProgrammingEnv found.");
                return false;
            }

            BE2_ProgrammingEnv targetEnv = null;
            foreach (var env in envs)
            {
                if (env != null && env.gameObject.activeInHierarchy)
                {
                    targetEnv = env;
                    break;
                }
            }

            if (targetEnv == null)
            {
                targetEnv = envs[0];
            }

            targetEnv.ClearBlocks();
            BE2_BlocksSerializer.XMLToBlocksCode(xmlContent, targetEnv);
            BE2XmlToRuntimeJson.Export(xmlContent);

            OnCodeGenerated?.Invoke();
            Debug.Log($"[LoadCodeFromFileAsync] Loaded: {fileName}");

            return true;
        }

        /// <summary>
        /// 비동기 저장소 제공자를 통해 저장 파일을 삭제합니다.
        /// </summary>
        public Task<bool> DeleteSaveFileAsync(string fileName)
        {
            return BE2_CodeStorageManager.Instance.DeleteCodeAsync(fileName);
        }

        /// <summary>
        /// 비동기 저장소 제공자를 통해 저장 파일 목록을 가져옵니다.
        /// </summary>
        public Task<List<string>> GetSavedFileListAsync()
        {
            return BE2_CodeStorageManager.Instance.GetFileListAsync();
        }

        /// <summary>
        /// 비동기 저장소 제공자를 통해 파일 존재 여부를 확인합니다.
        /// </summary>
        public Task<bool> FileExistsAsync(string fileName)
        {
            return BE2_CodeStorageManager.Instance.FileExistsAsync(fileName);
        }

        #endregion

        #region Legacy Save/Load (Compatibility)

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

            string xmlContent = exporter.GenerateXmlFromAllEnvs();

            if (created && exporter != null)
            {
                DestroyImmediate(exporter.gameObject);
            }

            if (string.IsNullOrEmpty(xmlContent))
            {
                Debug.LogWarning("XML generation failed or no blocks found.");
                CloseContextMenu();
                return;
            }

            string path = System.IO.Path.Combine(Application.persistentDataPath, "BlocksRuntime.xml");
            try
            {
                if (System.IO.File.Exists(path))
                {
                    string oldContent = System.IO.File.ReadAllText(path);
                    if (oldContent != xmlContent)
                    {
                        Debug.Log("[XML Change Detected] XML has been modified.");
                        LogXmlChanges(oldContent, xmlContent);
                    }
                    else
                    {
                        Debug.Log("[XML] No changes detected.");
                    }
                }
                else
                {
                    Debug.Log("[XML] New file created.");
                }

                System.IO.File.WriteAllText(path, xmlContent);
                Debug.Log($"XML generated and saved to: {path}");

                BE2XmlToRuntimeJson.Export(xmlContent);
                Debug.Log("[CodeGenerated] JSON also generated and saved.");

                OnCodeGenerated?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save XML: {ex.Message}");
            }

            if (image != null)
            {
                image.gameObject.SetActive(true);
            }

            CloseContextMenu();
        }

        public void XMLCodeGenerated()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "BlocksRuntime.xml");

            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[XMLCodeGenerated] XML file not found: {path}");
                CloseContextMenu();
                return;
            }

            var envs = GameObject.FindObjectsOfType<BE2_ProgrammingEnv>();
            if (envs == null || envs.Length == 0)
            {
                Debug.LogWarning("[XMLCodeGenerated] No BE2_ProgrammingEnv found.");
                CloseContextMenu();
                return;
            }

            BE2_ProgrammingEnv targetEnv = null;
            foreach (var env in envs)
            {
                if (env != null && env.gameObject.activeInHierarchy)
                {
                    targetEnv = env;
                    break;
                }
            }

            if (targetEnv == null)
            {
                targetEnv = envs[0];
            }

            bool success = BE2_BlocksSerializer.LoadCode(path, targetEnv);
            if (success)
            {
                Debug.Log($"[XMLCodeGenerated] XML load success: {path}");
            }
            else
            {
                Debug.LogWarning($"[XMLCodeGenerated] XML load failed: {path}");
            }

            CloseContextMenu();
        }

        #endregion

        public void CloseCompletedUI()
        {
            if (image != null)
            {
                image.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 기존 XML과 새 XML을 줄 단위로 비교해 변경 내역을 출력합니다.
        /// </summary>
        private void LogXmlChanges(string oldXml, string newXml)
        {
            string[] oldLines = oldXml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] newLines = newXml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== XML Change Details ===");

            var oldSet = new HashSet<string>(oldLines);
            var newSet = new HashSet<string>(newLines);

            int addedCount = 0;
            int removedCount = 0;

            foreach (string line in newLines)
            {
                if (!oldSet.Contains(line))
                {
                    sb.AppendLine($"[+Added] {line.Trim()}");
                    addedCount++;
                }
            }

            foreach (string line in oldLines)
            {
                if (!newSet.Contains(line))
                {
                    sb.AppendLine($"[-Removed] {line.Trim()}");
                    removedCount++;
                }
            }

            sb.AppendLine($"=== Total changes: +{addedCount}, -{removedCount} ===");
            Debug.Log(sb.ToString());
        }
    }
}

