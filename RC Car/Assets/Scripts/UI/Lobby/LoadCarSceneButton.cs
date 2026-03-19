using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadCarSceneButton : MonoBehaviour
{
    [Tooltip("레거시 직접 씬 이동 사용 여부")]
    [SerializeField] private bool _enableDirectLoad = false;

    public Button loadButton;

    /// <summary>
    /// 레거시 직접 이동 모드가 켜져 있을 때만 버튼 클릭 이벤트를 연결한다.
    /// 새 로비 플로우 기반 전환과 충돌하지 않도록 기본값은 비활성(false)로 유지한다.
    /// </summary>
    private void OnEnable()
    {
        if (!_enableDirectLoad)
            return;

        if (loadButton == null)
            return;

        loadButton.onClick.RemoveListener(LoadCarScene);
        loadButton.onClick.AddListener(LoadCarScene);
    }

    /// <summary>
    /// 오브젝트 비활성화 시 버튼 이벤트를 안전하게 해제한다.
    /// 동일 오브젝트 재활성화 시 중복 리스너가 쌓이지 않게 보장한다.
    /// </summary>
    private void OnDisable()
    {
        if (!_enableDirectLoad)
            return;

        if (loadButton == null)
            return;

        loadButton.onClick.RemoveListener(LoadCarScene);
    }

    /// <summary>
    /// 레거시 호환 경로로 차량 테스트 씬을 직접 로드한다.
    /// 현재 권장 경로는 OnRoomReady 이벤트 기반 네비게이터 전환이다.
    /// </summary>
    public void LoadCarScene()
    {
        SceneManager.LoadScene("03_NetworkCarTest");
    }
}
