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

            // XML 생성
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

            // persistentDataPath에 XML 파일 저장
            string path = System.IO.Path.Combine(Application.persistentDataPath, "BlocksRuntime.xml");
            try
            {
                // 기존 파일이 있으면 변경 사항 비교
                if (System.IO.File.Exists(path))
                {
                    string oldContent = System.IO.File.ReadAllText(path);
                    if (oldContent != xmlContent)
                    {
                        Debug.Log("[XML 변경 감지] XML 파일이 수정되었습니다!");
                        LogXmlChanges(oldContent, xmlContent);
                    }
                    else
                    {
                        Debug.Log("[XML] 변경 사항 없음 - 기존 파일과 동일합니다.");
                    }
                }
                else
                {
                    Debug.Log("[XML] 새 파일 생성됨");
                }

                System.IO.File.WriteAllText(path, xmlContent);
                Debug.Log($"XML generated and saved to: {path}");
                
                // JSON 생성 (XML을 직접 전달하여 중복 생성 방지)
                BE2XmlToRuntimeJson.Export(xmlContent);
                Debug.Log("[CodeGenerated] JSON also generated and saved.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to save XML: {ex.Message}");
            }

            CloseContextMenu();
        }

        // XML Code Generated 코드 임포트
        public void XMLCodeGenerated()
        {
            // persistentDataPath에서 XML 파일 경로
            string path = System.IO.Path.Combine(Application.persistentDataPath, "BlocksRuntime.xml");
            
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[XMLCodeGenerated] XML 파일을 찾을 수 없습니다: {path}");
                CloseContextMenu();
                return;
            }

            // Programming Environment 찾기
            var envs = GameObject.FindObjectsOfType<MG_BlocksEngine2.Environment.BE2_ProgrammingEnv>();
            if (envs == null || envs.Length == 0)
            {
                Debug.LogWarning("[XMLCodeGenerated] BE2_ProgrammingEnv를 찾을 수 없습니다.");
                CloseContextMenu();
                return;
            }

            // 활성화된 env 찾기
            MG_BlocksEngine2.Environment.BE2_ProgrammingEnv targetEnv = null;
            foreach (var env in envs)
            {
                if (env != null && env.gameObject.activeInHierarchy)
                {
                    targetEnv = env;
                    break;
                }
            }
            if (targetEnv == null) targetEnv = envs[0];

            // XML 로드
            bool success = MG_BlocksEngine2.Serializer.BE2_BlocksSerializer.LoadCode(path, targetEnv);
            
            if (success)
            {
                Debug.Log($"[XMLCodeGenerated] XML 로드 성공: {path}");
            }
            else
            {
                Debug.LogWarning($"[XMLCodeGenerated] XML 로드 실패: {path}");
            }

            CloseContextMenu();
        }

        /// <summary>
        /// 기존 XML과 새 XML의 변경 사항을 라인별로 비교하여 Debug.Log로 출력
        /// </summary>
        private void LogXmlChanges(string oldXml, string newXml)
        {
            string[] oldLines = oldXml.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            string[] newLines = newXml.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== XML 변경 상세 내역 ===");

            // 추가된 라인 찾기
            var oldSet = new System.Collections.Generic.HashSet<string>(oldLines);
            var newSet = new System.Collections.Generic.HashSet<string>(newLines);

            int addedCount = 0;
            int removedCount = 0;

            foreach (string line in newLines)
            {
                if (!oldSet.Contains(line))
                {
                    sb.AppendLine($"[+추가] {line.Trim()}");
                    addedCount++;
                }
            }

            foreach (string line in oldLines)
            {
                if (!newSet.Contains(line))
                {
                    sb.AppendLine($"[-삭제] {line.Trim()}");
                    removedCount++;
                }
            }

            sb.AppendLine($"=== 총 변경: +{addedCount}줄 추가, -{removedCount}줄 삭제 ===");
            
            Debug.Log(sb.ToString());
        }
    }
}