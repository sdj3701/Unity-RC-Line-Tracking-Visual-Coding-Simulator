using UBlockly; // Ublocky의 핵심 네임스페이스
using UnityEngine;
using UnityEngine.UI; // Text 컴포넌트를 사용하기 위해 필요
using UBlockly.UGUI;

public class BlocklyCodeViewer : MonoBehaviour
{
    // 워크스페이스 뷰 (기존 코드에서 사용되는 객체)
    // 인스펙터에서 할당하거나, Awake/Start에서 찾아와야 합니다.
    public WorkspaceView mWorkspaceView; 
    
    // C# 코드를 출력할 UI Text 컴포넌트 (인스펙터에서 할당)
    public Text m_CodeOutputText; 
    
    // 코드 출력 패널 또는 창 (코드를 담는 부모 GameObject)
    public GameObject m_CodePanel;

    // 코드 보기 버튼에 연결될 함수
    public void OnShowCodeClicked()
    {
        // 1. 현재 워크스페이스가 유효한지 확인
        //     if (mWorkspaceView == null || mWorkspaceView.Workspace == null)
        //     {
        //         Debug.LogError("WorkspaceView 또는 Workspace 객체가 유효하지 않습니다.");
        //         if (m_CodeOutputText != null)
        //         {
        //             m_CodeOutputText.text = "Error: Workspace is not initialized.";
        //         }
        //         return;
        //     }

        //     // 2. Ublocky의 C# Generator를 사용하여 블록 코드를 C# 코드로 변환
        //     // CSharp.Generator는 블록을 C# 코드 문자열로 변환하는 핵심 기능입니다.
        //     string csharpCode = CSharp.Generator.Generate(mWorkspaceView.Workspace); 

        //     // 3. 변환된 코드를 UI Text 컴포넌트에 표시
        //     if (m_CodeOutputText != null)
        //     {
        //         // 가독성을 위해 간단한 헤더를 추가할 수 있습니다.
        //         m_CodeOutputText.text = "// --- Generated C# Code from Ublocky ---\n\n" + csharpCode;
        //     }
        //     else
        //     {
        //         Debug.LogError("m_CodeOutputText가 할당되지 않았습니다.");
        //     }

        //     // 4. 코드 출력 패널을 활성화하여 사용자에게 보여줍니다.
        //     if (m_CodePanel != null)
        //     {
        //         m_CodePanel.SetActive(true);
        //     }
        // }

        // // 코드 창을 닫는 함수
        // public void OnCloseCodePanel()
        // {
        //     if (m_CodePanel != null)
        //     {
        //         m_CodePanel.SetActive(false);
        //     }
        // }
    }
}