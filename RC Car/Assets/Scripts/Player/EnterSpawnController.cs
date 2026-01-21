using UnityEngine;
using MG_BlocksEngine2.UI;

public class EnterSpawnController : MonoBehaviour
{
    [SerializeField] public GameObject targetObject; // 활성화/비활성화 체크할 게임 오브젝트
    [SerializeField] private BE2_UI_NewVariablePanel newVariablePanel; // CreateVariable 호출용

    void FixedUpdate()
    {
        // 타겟 오브젝트가 활성화되어 있을 때만 Enter 키 입력 처리
        if (targetObject != null && targetObject.activeSelf && Input.GetKeyDown(KeyCode.Return))
        {
            if (newVariablePanel != null)
            {
                // BE2_UI_NewVariablePanel의 inputVarName에서 텍스트를 가져와 CreateVariable 호출
                // inputVarName이 private이므로 리플렉션 대신 public 필드나 버튼 클릭 시뮬레이션 필요
                // 여기서는 직접 input field를 참조하도록 추가 필드 사용
                string varName = GetVariableName();
                if (!string.IsNullOrEmpty(varName))
                {
                    newVariablePanel.CreateVariable(varName);
                }
            }
        }
    }

    // BE2_UI_NewVariablePanel에서 입력된 변수 이름을 가져오는 메서드
    private string GetVariableName()
    {
        if (newVariablePanel != null)
        {
            // BE2_UI_NewVariablePanel의 자식에서 InputField 찾기
            var inputField = newVariablePanel.transform.GetChild(1);
            if (inputField != null)
            {
                var be2Input = MG_BlocksEngine2.Utils.BE2_InputField.GetBE2Component(inputField);
                if (be2Input != null)
                {
                    return be2Input.text;
                }
            }
        }
        return "";
    }
}
