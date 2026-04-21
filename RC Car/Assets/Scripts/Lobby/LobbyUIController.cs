using System.Threading.Tasks;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUIController : MonoBehaviour
{
    [Tooltip("Room Info UI")]
    [SerializeField] private GameObject _roomInfoUI;
    [SerializeField] private TMP_InputField _roomNameInputField;
    [SerializeField] private TMP_InputField _maxUserCountInputField;
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _closeRoomInfoButton;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private LobbyRoomFlow _roomFlow;
    [SerializeField] private ChatRoomManager _chatRoomManager;

    [Header("Photon Room Path")]
    [SerializeField] private bool _usePhotonRoomCreation = true;

    [Header("Scene Transition (ChatRoom Path)")]
    [SerializeField] private bool _moveToNetworkSceneOnChatRoomReady = true;
    [SerializeField] private string _targetSceneName = "03_NetworkCarTest";
    [SerializeField] private bool _storeRoomContext = true;

    /// <summary>
    /// 로비 UI의 초기 표시 상태를 설정한다.
    /// 시작 시 방 정보 패널은 숨기고 상태 텍스트는 빈 문자열로 초기화한다.
    /// </summary>
    private void Awake()
    {
        SetRoomInfoVisible(false);
        SetStatusText(string.Empty);
    }

    /// <summary>
    /// UI 버튼 이벤트와 Flow 이벤트를 구독한다.
    /// UI 액션(버튼 클릭)과 비즈니스 상태 변화(시작/진행/성공/실패/취소)를 연결하는 접점이다.
    /// </summary>
    private void OnEnable()
    {
        if (_createRoomButton != null)
        {
            _createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);
            _createRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);
        }

        if (_closeRoomInfoButton != null)
        {
            _closeRoomInfoButton.onClick.RemoveListener(HideRoomInfoUI);
            _closeRoomInfoButton.onClick.AddListener(HideRoomInfoUI);
        }

        if (_roomFlow != null)
        {
            _roomFlow.OnRoomCreateStarted += HandleRoomCreateStarted;
            _roomFlow.OnRoomCreateProgress += HandleRoomCreateProgress;
            _roomFlow.OnRoomReady += HandleRoomReady;
            _roomFlow.OnRoomCreateFailed += HandleRoomCreateFailed;
            _roomFlow.OnRoomCreateCanceled += HandleRoomCreateCanceled;
        }

        if (_chatRoomManager != null)
        {
            _chatRoomManager.OnCreateStarted += HandleChatRoomCreateStarted;
            _chatRoomManager.OnCreateSucceeded += HandleChatRoomCreateSucceeded;
            _chatRoomManager.OnCreateFailed += HandleChatRoomCreateFailed;
            _chatRoomManager.OnCreateCanceled += HandleChatRoomCreateCanceled;
        }
    }

    /// <summary>
    /// 활성화 단계에서 등록한 버튼/Flow 이벤트를 모두 해제한다.
    /// 중복 호출과 메모리 누수를 막기 위한 정리 단계다.
    /// </summary>
    private void OnDisable()
    {
        if (_createRoomButton != null)
            _createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);

        if (_closeRoomInfoButton != null)
            _closeRoomInfoButton.onClick.RemoveListener(HideRoomInfoUI);

        if (_roomFlow != null)
        {
            _roomFlow.OnRoomCreateStarted -= HandleRoomCreateStarted;
            _roomFlow.OnRoomCreateProgress -= HandleRoomCreateProgress;
            _roomFlow.OnRoomReady -= HandleRoomReady;
            _roomFlow.OnRoomCreateFailed -= HandleRoomCreateFailed;
            _roomFlow.OnRoomCreateCanceled -= HandleRoomCreateCanceled;
        }

        if (_chatRoomManager != null)
        {
            _chatRoomManager.OnCreateStarted -= HandleChatRoomCreateStarted;
            _chatRoomManager.OnCreateSucceeded -= HandleChatRoomCreateSucceeded;
            _chatRoomManager.OnCreateFailed -= HandleChatRoomCreateFailed;
            _chatRoomManager.OnCreateCanceled -= HandleChatRoomCreateCanceled;
        }
    }

    /// <summary>
    /// 사용자가 Create Room 진입 버튼을 눌렀을 때 RoomInfoUI를 보여준다.
    /// 새 요청을 입력할 수 있도록 상태 메시지와 버튼 상태를 기본값으로 되돌린다.
    /// </summary>
    public void ShowRoomInfoUI()
    {
        SetRoomInfoVisible(true);
        SetStatusText(string.Empty);
        SetCreateButtonInteractable(true);
    }

    /// <summary>
    /// RoomInfoUI를 닫는다.
    /// 생성 요청이 진행 중이면 먼저 취소를 시도해 백그라운드 요청이 남지 않도록 처리한다.
    /// </summary>
    public void HideRoomInfoUI()
    {
        if (_roomFlow != null && _roomFlow.IsBusy)
            _roomFlow.CancelCurrentRequest();

        if (_chatRoomManager != null && _chatRoomManager.IsBusy)
            _chatRoomManager.CancelCurrentRequest();

        SetRoomInfoVisible(false);
        SetStatusText(string.Empty);
        SetCreateButtonInteractable(true);
    }

    /// <summary>
    /// RoomInfoUI 내부 CreateRoom 버튼 클릭 처리 함수다.
    /// 입력 필드 값을 읽어 Flow의 CreateRoom 커맨드로 전달한다.
    /// </summary>
    public void OnCreateRoomButtonClicked()
    {
        if (!TryReadCreateInputs(out string roomName, out string maxUserCountRaw))
            return;

        if (_usePhotonRoomCreation)
        {
            _ = CreatePhotonRoomFromInputsAsync(roomName, maxUserCountRaw);
            return;
        }

        if (_chatRoomManager != null)
        {
            _chatRoomManager.CreateRoom(roomName, maxUserCountRaw);
            return;
        }

        if (_roomFlow == null)
        {
            SetStatusText("ChatRoomManager 또는 LobbyRoomFlow 참조가 없습니다.");
            return;
        }

        // 레거시 룸 플로우 fallback. maxUserCount 입력 검증은 위에서 이미 수행한다.
        _roomFlow.CreateRoom(roomName);
    }

    private async Task CreatePhotonRoomFromInputsAsync(string roomName, string maxUserCountRaw)
    {
        if (!int.TryParse(maxUserCountRaw, out int maxPlayers) || maxPlayers <= 0)
        {
            SetStatusText("최대 인원은 1명 이상의 숫자로 입력해주세요.");
            return;
        }

        SetCreateButtonInteractable(false);
        SetStatusText($"\"{roomName}\" Photon 방 생성 중...");
        FusionDebugLog.Info(FusionDebugFlow.Room, $"Lobby create button requested Photon room. room={roomName}, maxPlayers={maxPlayers}");

        FusionRoomService roomService = FusionRoomService.GetOrCreate();
        bool success = await roomService.CreateRoomAsync(
            roomName,
            maxPlayers,
            _targetSceneName,
            _moveToNetworkSceneOnChatRoomReady);

        if (!success)
        {
            SetCreateButtonInteractable(true);
            string message = string.IsNullOrWhiteSpace(roomService.LastErrorMessage)
                ? "Photon 방 생성에 실패했습니다."
                : roomService.LastErrorMessage;
            SetStatusText(message);
            return;
        }

        SetStatusText("Photon 방 생성 완료. 네트워크 씬으로 이동합니다.");
        SetCreateButtonInteractable(!_moveToNetworkSceneOnChatRoomReady);
    }

    /// <summary>
    /// 룸 생성 시작 이벤트 수신 시 UI를 로딩 상태로 전환한다.
    /// 중복 클릭 방지를 위해 생성 버튼을 비활성화하고 안내 메시지를 표시한다.
    /// </summary>
    /// <param name="roomName">요청에 사용된 룸 이름</param>
    private void HandleRoomCreateStarted(string roomName)
    {
        SetCreateButtonInteractable(false);
        SetStatusText($"\"{roomName}\" 룸 생성 중...");
    }

    /// <summary>
    /// 서버 준비 진행 이벤트를 받아 사용자에게 현재 진행 중임을 알린다.
    /// 폴링 횟수를 함께 노출해 네트워크 지연 상황에서 사용자 혼란을 줄인다.
    /// </summary>
    /// <param name="progress">프로비저닝 진행 정보</param>
    private void HandleRoomCreateProgress(RoomProvisioningProgress progress)
    {
        if (progress == null)
            return;

        SetStatusText($"서버 준비 중... ({progress.PollCount})");
    }

    /// <summary>
    /// 룸 준비 완료 이벤트 수신 시 씬 전환 직전 상태 메시지를 표시한다.
    /// 이 시점에서는 버튼을 다시 비활성화해 중복 액션을 방지한다.
    /// </summary>
    /// <param name="roomInfo">준비 완료된 룸 정보</param>
    private void HandleRoomReady(RoomInfo roomInfo)
    {
        SetCreateButtonInteractable(false);
        SetStatusText("룸 준비 완료. 씬 전환 중...");
    }

    /// <summary>
    /// 룸 생성 실패 이벤트 수신 시 사용자 메시지를 표시한다.
    /// 재시도 가능 상태를 위해 생성 버튼을 다시 활성화한다.
    /// </summary>
    /// <param name="error">실패 코드/메시지/재시도 여부를 담은 오류 정보</param>
    private void HandleRoomCreateFailed(RoomCreateError error)
    {
        SetCreateButtonInteractable(true);

        string message = error != null && !string.IsNullOrWhiteSpace(error.UserMessage)
            ? error.UserMessage
            : "룸 생성에 실패했습니다.";

        SetStatusText(message);
    }

    /// <summary>
    /// 요청 취소 이벤트 수신 시 UI를 대기 상태로 복구한다.
    /// 사용자가 즉시 다시 시도할 수 있도록 버튼을 재활성화한다.
    /// </summary>
    private void HandleRoomCreateCanceled()
    {
        SetCreateButtonInteractable(true);
        SetStatusText("룸 생성 요청이 취소되었습니다.");
    }

    /// <summary>
    /// ChatRoomManager 생성 시작 이벤트 수신 시 UI를 로딩 상태로 전환한다.
    /// </summary>
    private void HandleChatRoomCreateStarted(string roomTitle, int maxUserCount)
    {
        SetCreateButtonInteractable(false);
        SetStatusText($"\"{roomTitle}\" 채팅방 생성 중... (최대 {maxUserCount}명)");
    }

    /// <summary>
    /// ChatRoomManager 생성 성공 이벤트 수신 시 상태 메시지를 표시한다.
    /// </summary>
    private void HandleChatRoomCreateSucceeded(ChatRoomCreateInfo roomInfo)
    {
        SetCreateButtonInteractable(!_moveToNetworkSceneOnChatRoomReady);

        string roomId = roomInfo != null ? roomInfo.RoomId : string.Empty;
        string title = roomInfo != null ? roomInfo.Title : string.Empty;
        string ownerUserId = roomInfo != null ? roomInfo.OwnerUserId : string.Empty;
        string createdAtUtc = roomInfo != null ? roomInfo.CreatedAtUtc : string.Empty;

        if (string.IsNullOrWhiteSpace(title))
            SetStatusText($"채팅방 생성 완료 (ID: {roomId})");
        else
            SetStatusText($"\"{title}\" 채팅방 생성 완료 (ID: {roomId})");

        if (!_moveToNetworkSceneOnChatRoomReady)
            return;

        if (_storeRoomContext)
        {
            RoomSessionContext.Set(new RoomInfo
            {
                RoomId = roomId,
                RoomName = title,
                HostUserId = ownerUserId,
                CreatedAtUtc = createdAtUtc
            });
        }

        if (!string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    /// <summary>
    /// ChatRoomManager 생성 실패 이벤트 수신 시 사용자 메시지를 표시한다.
    /// </summary>
    private void HandleChatRoomCreateFailed(string userMessage)
    {
        SetCreateButtonInteractable(true);
        SetStatusText(string.IsNullOrWhiteSpace(userMessage) ? "채팅방 생성에 실패했습니다." : userMessage);
    }

    /// <summary>
    /// ChatRoomManager 취소 이벤트 수신 시 UI를 대기 상태로 복구한다.
    /// </summary>
    private void HandleChatRoomCreateCanceled()
    {
        SetCreateButtonInteractable(true);
        SetStatusText("채팅방 생성 요청이 취소되었습니다.");
    }

    /// <summary>
    /// 생성 버튼의 상호작용 가능 여부를 일관되게 제어한다.
    /// null 체크를 내부에서 처리해 호출부를 단순화한다.
    /// </summary>
    /// <param name="interactable">버튼 활성화 여부</param>
    private void SetCreateButtonInteractable(bool interactable)
    {
        if (_createRoomButton != null)
            _createRoomButton.interactable = interactable;
    }

    /// <summary>
    /// 생성에 필요한 입력값(방 이름, 최대 인원)을 읽고 검증한다.
    /// 둘 중 하나라도 비어 있으면 false를 반환한다.
    /// </summary>
    private bool TryReadCreateInputs(out string roomName, out string maxUserCountRaw)
    {
        roomName = _roomNameInputField != null ? _roomNameInputField.text : string.Empty;
        maxUserCountRaw = _maxUserCountInputField != null ? _maxUserCountInputField.text : string.Empty;

        roomName = string.IsNullOrWhiteSpace(roomName) ? string.Empty : roomName.Trim();
        maxUserCountRaw = string.IsNullOrWhiteSpace(maxUserCountRaw) ? string.Empty : maxUserCountRaw.Trim();

        if (string.IsNullOrWhiteSpace(roomName))
        {
            SetStatusText("방 이름을 입력해 주세요.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(maxUserCountRaw))
        {
            SetStatusText("최대 인원을 입력해 주세요.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 상태 메시지 텍스트를 갱신한다.
    /// null 입력 시 빈 문자열로 치환해 텍스트 컴포넌트의 안정성을 보장한다.
    /// </summary>
    /// <param name="message">사용자에게 표시할 상태 문구</param>
    private void SetStatusText(string message)
    {
        if (_statusText != null)
            _statusText.text = message ?? string.Empty;
    }

    /// <summary>
    /// RoomInfoUI의 활성/비활성 표시를 제어한다.
    /// 패널 오브젝트가 할당되지 않은 경우를 안전하게 처리한다.
    /// </summary>
    /// <param name="isVisible">표시 여부</param>
    private void SetRoomInfoVisible(bool isVisible)
    {
        if (_roomInfoUI != null)
            _roomInfoUI.SetActive(isVisible);
    }
}
