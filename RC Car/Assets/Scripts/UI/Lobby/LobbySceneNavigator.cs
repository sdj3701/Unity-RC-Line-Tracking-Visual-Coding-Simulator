using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbySceneNavigator : MonoBehaviour
{
    [SerializeField] private LobbyRoomFlow _roomFlow;
    [SerializeField] private string _targetSceneName = "03_NetworkCarTest";
    [SerializeField] private bool _storeRoomContext = true;

    /// <summary>
    /// 오브젝트 활성화 시 룸 준비 완료 이벤트를 구독한다.
    /// 로비 플로우가 성공 신호를 보낼 때만 씬 전환이 일어나도록 연결한다.
    /// </summary>
    private void OnEnable()
    {
        if (_roomFlow != null)
            _roomFlow.OnRoomReady += HandleRoomReady;
    }

    /// <summary>
    /// 오브젝트 비활성화 시 이벤트 구독을 해제한다.
    /// 중복 구독/메모리 누수/중복 씬 전환을 방지하기 위한 생명주기 정리 단계다.
    /// </summary>
    private void OnDisable()
    {
        if (_roomFlow != null)
            _roomFlow.OnRoomReady -= HandleRoomReady;
    }

    /// <summary>
    /// 룸 준비 완료 이벤트를 수신하면 룸 컨텍스트를 저장하고 목표 씬으로 이동한다.
    /// 씬 전환 책임을 UI/Flow에서 분리해 네비게이터 계층으로 고정한다.
    /// </summary>
    /// <param name="roomInfo">준비 완료된 룸 정보</param>
    private void HandleRoomReady(RoomInfo roomInfo)
    {
        if (_storeRoomContext)
            RoomSessionContext.Set(roomInfo);

        if (!string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }
}
