using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UBlockly.UGUI
{
    public class PlayControlView : MonoBehaviour
    {
        [SerializeField] private Toggle m_ToggleNormal;
        [SerializeField] private Toggle m_ToggleDebug;
        [SerializeField] private Button m_BtnRun;
        [SerializeField] private Button m_BtnPause;
        [SerializeField] private Button m_BtnStop;
        [SerializeField] private Button m_BtnStep;
        [SerializeField] private Toggle m_ToggleASync;
        [SerializeField] private Toggle m_ToggleSync;
        [SerializeField] private Toggle m_ToggleCallstack;
        [SerializeField] private GameObject m_PanelCallstack;
        [SerializeField] private GameObject m_prefabCallstackText;
        // 생성된 C# 스크립트 미리보기용 UI 패널
        [SerializeField] private CodePreviewView m_CodePreviewView;
        // 블록 -> C# 코드 변환기 (씬에 존재하면 할당, 없으면 런타임에 찾음)
        [SerializeField] private UblockyCodeExporter m_CodeExporter;

        private WorkspaceView mWorkspaceView;
        
        private RunnerUpdateStateObserver mObserver;

        public void Init(WorkspaceView workspaceView)
        {
            mWorkspaceView = workspaceView;
            mObserver = new RunnerUpdateStateObserver(this);
            CSharp.Runner.AddObserver(mObserver);
            
            m_BtnRun.onClick.AddListener(OnRun);
            m_BtnPause.onClick.AddListener(OnPause);
            m_BtnStop.onClick.AddListener(OnStop);
            m_BtnStep.onClick.AddListener(OnStep);

            m_ToggleNormal.isOn = true;
            SetMode(Runner.Mode.Normal);            
            m_ToggleNormal.onValueChanged.AddListener(on => SetMode(Runner.Mode.Normal));
            m_ToggleDebug.onValueChanged.AddListener(on => SetMode(Runner.Mode.Step));

            m_ToggleASync.isOn = true;
            m_ToggleASync.onValueChanged.AddListener(on => SwitchSync(false));
            m_ToggleSync.onValueChanged.AddListener(on => SwitchSync(true));

            m_ToggleCallstack.isOn = false;
            HideCallstack();
            m_ToggleCallstack.onValueChanged.AddListener(on =>
            {
                if (on) ShowCallstack();
                else HideCallstack();
            });
        }

        public void Reset()
        {
            OnStop();
            
            m_ToggleNormal.onValueChanged.RemoveAllListeners();
            m_ToggleDebug.onValueChanged.RemoveAllListeners();
            m_BtnRun.onClick.RemoveAllListeners();
            m_BtnPause.onClick.RemoveAllListeners();
            m_BtnStop.onClick.RemoveAllListeners();
            m_BtnStep.onClick.RemoveAllListeners();
            m_ToggleCallstack.onValueChanged.RemoveAllListeners();
            
            CSharp.Runner.RemoveObserver(mObserver);
        }

        private void EnableSettings(bool enable)
        {
            m_ToggleNormal.enabled = enable;
            m_ToggleDebug.enabled = enable;
            m_ToggleASync.enabled = enable;
            m_ToggleSync.enabled = enable;
        }

        private void SetMode(Runner.Mode mode)
        {
            if (CSharp.Runner.CurStatus != Runner.Status.Stop)
            {
                Debug.Log("<color=red> Switch Mode is not supported when code is running</color>");
                return;
            }
            
            CSharp.Runner.SetMode(mode);

            if (mode == Runner.Mode.Normal)
            {
                m_BtnStep.gameObject.SetActive(false);
                m_BtnStop.gameObject.SetActive(true);
                m_BtnRun.gameObject.SetActive(true);
                m_BtnPause.gameObject.SetActive(false);
                m_ToggleCallstack.gameObject.SetActive(false);
            }
            else
            {
                m_BtnStep.gameObject.SetActive(true);
                m_BtnStop.gameObject.SetActive(true);
                m_BtnRun.gameObject.SetActive(false);
                m_BtnPause.gameObject.SetActive(false);
                m_ToggleCallstack.gameObject.SetActive(true);
            }
        }

        private void OnRun()
        {            
            m_BtnRun.gameObject.SetActive(false);
            m_BtnPause.gameObject.SetActive(true);
            EnableSettings(false);

            // BtnRun 클릭 시, 현재 블록으로부터 생성된 C# 스크립트를 GUI로 미리보기
            ShowGeneratedCodePreview();

            if (CSharp.Runner.CurStatus == Runner.Status.Stop)
            {
                CSharp.Runner.Run(mWorkspaceView.Workspace);
                //            Lua.Runner.Run(mWorkspaceView.Workspace);
            }
            else
            {
                CSharp.Runner.Resume();
            }
        }

        /// <summary>
        /// 현재 워크스페이스의 블록을 C# 스크립트로 변환하고 미리보기 패널에 표시
        /// </summary>
        private void ShowGeneratedCodePreview()
        {
            // Exporter 참조 확보 (직접 할당 또는 씬에서 검색)
            var exporter = m_CodeExporter != null ? m_CodeExporter : FindObjectOfType<UblockyCodeExporter>();
            if (exporter == null) return;

            // 클래스 래핑된 스크립트를 생성하여 사람이 읽기 편하게 출력
            string script = exporter.BuildCSharpScript("UBlocklyGenerated", "Run", null, true);
            if (m_CodePreviewView != null)
            {
                m_CodePreviewView.Show(script, "Generated C# Preview");
            }
        }

        private void OnPause()
        {
            CSharp.Runner.Pause();
        }

        private void OnStop()
        {
            CSharp.Runner.Stop();
        }

        private void OnStep()
        {
            if (CSharp.Runner.CurStatus == Runner.Status.Stop)
            {
                CSharp.Runner.Run(mWorkspaceView.Workspace);
            }
            else
            {
                CSharp.Runner.Step();
            }
        }

        private void SwitchSync(bool isSync)
        {
            if (CSharp.Runner.CurStatus != Runner.Status.Stop)
            {
                Debug.Log("<color=red> Switch Mode is not supported when code is running</color>");
                return;
            }

            mWorkspaceView.Workspace.Options.Synchronous = isSync;
        }

        private void ShowCallstack()
        {
            m_PanelCallstack.SetActive(true);
            
            Transform parent = m_prefabCallstackText.transform.parent;
            foreach (Transform child in parent)
            {
                child.gameObject.SetActive(false);
            }
            
            List<string> callstack = CSharp.Runner.GetCallStack();
            if (callstack == null)
                return;

            int index = 1;
            foreach (string str in callstack)
            {
                if (index < parent.childCount)
                {
                    parent.GetChild(index).gameObject.SetActive(true);
                    parent.GetChild(index).GetComponent<Text>().text = str;
                }
                else
                {
                    GameObject textObj = GameObject.Instantiate(m_prefabCallstackText, parent);
                    textObj.SetActive(true);
                    textObj.GetComponent<Text>().text = str;
                }
                index++;
            }
        }

        private void HideCallstack()
        {
            if (!m_ToggleCallstack.isOn)
            {
                m_PanelCallstack.SetActive(false);
            }
            else
            {
                Transform parent = m_prefabCallstackText.transform.parent;
                foreach (Transform child in parent)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private void UpdateStatus(RunnerUpdateState args)
        {
            switch (args.Type)
            {
                case RunnerUpdateState.Pause:
                    m_BtnRun.gameObject.SetActive(true);
                    m_BtnPause.gameObject.SetActive(false);
                    break;
                case RunnerUpdateState.Resume:
                    m_BtnRun.gameObject.SetActive(false);
                    m_BtnPause.gameObject.SetActive(true);
                    break;
                case RunnerUpdateState.Stop:
                case RunnerUpdateState.Error:
                    HideCallstack();
                    EnableSettings(true);
                    SetMode(CSharp.Runner.RunMode);
                    break;
                case RunnerUpdateState.RunBlock:
                case RunnerUpdateState.FinishBlock:
                    if (m_ToggleCallstack.isOn)
                        ShowCallstack();
                    break;
            }
        }
        
        private class RunnerUpdateStateObserver : IObserver<RunnerUpdateState>
        {
            private PlayControlView mView;

            public RunnerUpdateStateObserver(PlayControlView statusView)
            {
                mView = statusView;
            }

            public void OnUpdated(object subject, RunnerUpdateState args)
            {
                mView.UpdateStatus(args);
            }
        }
    }
}