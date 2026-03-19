using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Auth;
using UnityEngine;

public class LobbyRoomFlow : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private string _createRoomEndpoint = "http://ioteacher.com/api/rooms";
    [SerializeField] private string _roomStatusEndpointTemplate = "http://ioteacher.com/api/rooms/{roomId}/status?jobId={jobId}";
    [SerializeField] private string _jobStatusEndpointTemplate = "http://ioteacher.com/api/room-jobs/{jobId}";

    [Header("Timeout / Polling")]
    [SerializeField] private int _requestTimeoutSeconds = 15;
    [SerializeField] private int _provisioningTimeoutSeconds = 90;
    [SerializeField] private float _pollIntervalSeconds = 2f;

    [Header("Temporary Bypass")]
    [Tooltip("웹의 DB 생성/상태 확인 기능이 준비되기 전, 상태 확인 단계를 건너뛰고 즉시 Ready 처리한다.")]
    [SerializeField] private bool _assumeReadyWithoutProvisioningCheck = false;
    [Tooltip("웹 API 호출 자체를 건너뛰고 로컬에서 즉시 Ready 이벤트를 발생시킨다. (개발/테스트 전용)")]
    [SerializeField] private bool _forceReadyWithoutCreateRequest = false;

    [Header("Debug")]
    [SerializeField] private bool _debugLogFlow = true;

    public event Action<string> OnRoomCreateStarted;
    public event Action<RoomProvisioningProgress> OnRoomCreateProgress;
    public event Action<RoomInfo> OnRoomReady;
    public event Action<RoomCreateError> OnRoomCreateFailed;
    public event Action OnRoomCreateCanceled;

    public bool IsBusy { get; private set; }

    private CancellationTokenSource _requestCancellation;

    /// <summary>
    /// UI에서 호출하는 룸 생성 진입점이다.
    /// 내부 비동기 파이프라인(CreateRoomAsync)을 시작해 검증/요청/상태확인/이벤트 발행을 수행한다.
    /// </summary>
    /// <param name="roomName">사용자가 입력한 룸 이름 원본 문자열</param>
    public void CreateRoom(string roomName)
    {
        _ = CreateRoomAsync(roomName);
    }

    /// <summary>
    /// 현재 진행 중인 룸 생성 요청을 취소한다.
    /// 패널 닫기, 씬 이동, 오브젝트 비활성화 시 남아 있는 네트워크 요청을 정리할 때 사용한다.
    /// </summary>
    public void CancelCurrentRequest()
    {
        if (_requestCancellation == null || _requestCancellation.IsCancellationRequested)
            return;

        _requestCancellation.Cancel();
    }

    /// <summary>
    /// 룸 생성 플로우의 메인 오케스트레이션 함수다.
    /// 검증 -> 생성 요청 -> 즉시 Ready 처리 또는 Provisioning 대기 -> 최종 이벤트 발행 순서로 동작한다.
    /// </summary>
    /// <param name="roomNameRaw">UI 입력 원본값(공백 포함 가능)</param>
    private async Task CreateRoomAsync(string roomNameRaw)
    {
        if (IsBusy)
        {
            EmitFailure(new RoomCreateError(
                RoomCreateErrorCode.Busy,
                "이미 룸 생성 요청을 처리 중입니다.",
                rawMessage: null,
                retryable: true));
            return;
        }

        string roomName = string.IsNullOrWhiteSpace(roomNameRaw)
            ? string.Empty
            : roomNameRaw.Trim();

        if (string.IsNullOrWhiteSpace(roomName))
        {
            EmitFailure(new RoomCreateError(
                RoomCreateErrorCode.Validation,
                "룸 이름을 입력해 주세요.",
                rawMessage: "Room name is empty.",
                retryable: true));
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnRoomCreateStarted?.Invoke(roomName);
            Log($"CreateRoom start: {roomName}");

            string hostUserId = ResolveHostUserId();
            string idempotencyKey = Guid.NewGuid().ToString("N");
            var request = new RoomCreateRequest(roomName, hostUserId, idempotencyKey);

            if (_forceReadyWithoutCreateRequest)
            {
                RoomInfo localBypassRoomInfo = BuildBypassRoomInfo(
                    source: null,
                    request: request,
                    jobId: null);

                Log("Create API bypass is enabled. Skipping HTTP request and treating room as READY.");
                PublishRoomReady(localBypassRoomInfo, request);
                return;
            }

            var service = new LobbyRoomService(
                _createRoomEndpoint,
                _roomStatusEndpointTemplate,
                _jobStatusEndpointTemplate,
                _requestTimeoutSeconds);

            string accessToken = ResolveAccessToken();
            RoomCreateResult createResult = await service.CreateRoomAsync(request, accessToken, _requestCancellation.Token);

            if (createResult.IsCanceled)
            {
                OnRoomCreateCanceled?.Invoke();
                Log("CreateRoom canceled while creating.");
                return;
            }

            if (!createResult.IsSuccess)
            {
                EmitFailure(createResult.Error);
                return;
            }

            if (_assumeReadyWithoutProvisioningCheck)
            {
                RoomInfo bypassRoomInfo = BuildBypassRoomInfo(
                    createResult.RoomInfo,
                    request,
                    createResult.JobId);

                Log("Provisioning check bypass is enabled. Treating room as READY.");
                PublishRoomReady(bypassRoomInfo, request);
                return;
            }

            if (createResult.Status == RoomProvisioningStatus.Ready)
            {
                PublishRoomReady(createResult.RoomInfo, request);
                return;
            }

            await WaitUntilReadyAsync(
                service,
                request,
                createResult.RoomInfo,
                createResult.JobId,
                accessToken,
                _requestCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            OnRoomCreateCanceled?.Invoke();
            Log("CreateRoom canceled by token.");
        }
        catch (Exception e)
        {
            EmitFailure(new RoomCreateError(
                RoomCreateErrorCode.Unknown,
                "룸 생성 중 예외가 발생했습니다.",
                rawMessage: e.Message,
                retryable: true));
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    /// <summary>
    /// 서버가 Provisioning 상태를 반환한 경우 READY가 될 때까지 주기적으로 상태를 조회한다.
    /// 타임아웃, 취소, 실패, 성공(Ready) 케이스를 각각 분기해 적절한 이벤트를 발행한다.
    /// </summary>
    /// <param name="service">룸 API 접근 서비스</param>
    /// <param name="request">초기 생성 요청 데이터</param>
    /// <param name="createdRoom">초기 생성 응답에서 받은 룸 정보</param>
    /// <param name="jobId">비동기 준비 작업 ID</param>
    /// <param name="accessToken">인증 토큰</param>
    /// <param name="cancellationToken">취소 토큰</param>
    private async Task WaitUntilReadyAsync(
        LobbyRoomService service,
        RoomCreateRequest request,
        RoomInfo createdRoom,
        string jobId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string roomId = createdRoom != null ? createdRoom.RoomId : null;
        if (string.IsNullOrWhiteSpace(roomId) && string.IsNullOrWhiteSpace(jobId))
        {
            EmitFailure(new RoomCreateError(
                RoomCreateErrorCode.Unknown,
                "룸 생성 응답에 roomId/jobId가 없습니다.",
                rawMessage: "Missing roomId and jobId.",
                retryable: true));
            return;
        }

        int pollCount = 0;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed.TotalSeconds < Mathf.Max(5, _provisioningTimeoutSeconds))
        {
            cancellationToken.ThrowIfCancellationRequested();

            pollCount++;

            OnRoomCreateProgress?.Invoke(new RoomProvisioningProgress
            {
                RoomId = roomId,
                JobId = jobId,
                PollCount = pollCount,
                ElapsedSeconds = (float)stopwatch.Elapsed.TotalSeconds,
                Status = RoomProvisioningStatus.Provisioning.ToString().ToUpperInvariant()
            });

            int delayMs = Mathf.Max(200, Mathf.RoundToInt(_pollIntervalSeconds * 1000f));
            await Task.Delay(delayMs, cancellationToken);

            RoomCreateResult statusResult = await service.GetRoomStatusAsync(
                roomId,
                jobId,
                accessToken,
                cancellationToken);

            if (statusResult.IsCanceled)
            {
                OnRoomCreateCanceled?.Invoke();
                Log("CreateRoom canceled while polling.");
                return;
            }

            if (!statusResult.IsSuccess)
            {
                EmitFailure(statusResult.Error);
                return;
            }

            if (statusResult.Status == RoomProvisioningStatus.Ready)
            {
                PublishRoomReady(statusResult.RoomInfo ?? createdRoom, request);
                return;
            }

            if (statusResult.Status == RoomProvisioningStatus.Failed)
            {
                EmitFailure(new RoomCreateError(
                    RoomCreateErrorCode.Server,
                    "룸 준비가 실패했습니다.",
                    rawMessage: "Provisioning failed.",
                    retryable: true));
                return;
            }
        }

        EmitFailure(new RoomCreateError(
            RoomCreateErrorCode.Timeout,
            "룸 준비 확인 시간이 초과되었습니다.",
            rawMessage: "Provisioning timeout.",
            retryable: true));
    }

    /// <summary>
    /// 임시 우회 모드에서 사용할 룸 정보를 보정한다.
    /// 서버가 roomId를 주지 않아도 로컬 테스트가 가능하도록 임시 ID를 생성한다.
    /// </summary>
    /// <param name="source">서버 응답 룸 정보(없을 수 있음)</param>
    /// <param name="request">원본 생성 요청</param>
    /// <param name="jobId">서버 작업 ID(있으면 임시 ID 생성에 활용)</param>
    /// <returns>씬 전환에 필요한 최소 필드가 채워진 룸 정보</returns>
    private static RoomInfo BuildBypassRoomInfo(RoomInfo source, RoomCreateRequest request, string jobId)
    {
        RoomInfo roomInfo = source ?? new RoomInfo();

        if (string.IsNullOrWhiteSpace(roomInfo.RoomId))
        {
            string seed = string.IsNullOrWhiteSpace(jobId) ? Guid.NewGuid().ToString("N") : jobId;
            roomInfo.RoomId = $"temp-room-{seed}";
        }

        if (string.IsNullOrWhiteSpace(roomInfo.RoomName))
            roomInfo.RoomName = request.RoomName;

        if (string.IsNullOrWhiteSpace(roomInfo.HostUserId))
            roomInfo.HostUserId = request.HostUserId;

        return roomInfo;
    }

    /// <summary>
    /// READY 상태가 확인된 룸 정보를 정규화한 뒤 OnRoomReady 이벤트를 발행한다.
    /// 필수값(roomId)이 비어 있으면 성공 처리하지 않고 실패 이벤트로 전환한다.
    /// </summary>
    /// <param name="roomInfo">서버에서 수신한 룸 정보</param>
    /// <param name="request">원본 요청 데이터(누락 필드 보정용)</param>
    private void PublishRoomReady(RoomInfo roomInfo, RoomCreateRequest request)
    {
        RoomInfo normalized = roomInfo ?? new RoomInfo();

        if (string.IsNullOrWhiteSpace(normalized.RoomName))
            normalized.RoomName = request.RoomName;
        if (string.IsNullOrWhiteSpace(normalized.HostUserId))
            normalized.HostUserId = request.HostUserId;

        if (string.IsNullOrWhiteSpace(normalized.RoomId))
        {
            EmitFailure(new RoomCreateError(
                RoomCreateErrorCode.Unknown,
                "룸 ID가 없어 씬 전환을 진행할 수 없습니다.",
                rawMessage: "RoomId is empty on ready event.",
                retryable: true));
            return;
        }

        OnRoomReady?.Invoke(normalized);
        Log($"Room ready: roomId={normalized.RoomId}, roomName={normalized.RoomName}");
    }

    /// <summary>
    /// AuthManager의 현재 사용자 정보에서 방장 사용자 ID를 추출한다.
    /// 로그인 정보가 없으면 빈 문자열을 반환해 서비스 계층에서 안전하게 처리하게 한다.
    /// </summary>
    /// <returns>현재 로그인 사용자 ID 또는 빈 문자열</returns>
    private static string ResolveHostUserId()
    {
        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
            return string.Empty;

        return AuthManager.Instance.CurrentUser.userId ?? string.Empty;
    }

    /// <summary>
    /// AuthManager에서 현재 access token을 가져온다.
    /// 인증 인스턴스가 없으면 빈 문자열을 반환한다.
    /// </summary>
    /// <returns>Bearer 인증에 사용할 access token 또는 빈 문자열</returns>
    private static string ResolveAccessToken()
    {
        return AuthManager.Instance != null
            ? AuthManager.Instance.GetAccessToken()
            : string.Empty;
    }

    /// <summary>
    /// 룸 생성 실패 이벤트를 일관된 형식으로 발행한다.
    /// null 오류 입력이 들어오면 기본 Unknown 오류를 만들어 UI가 안전하게 메시지를 표시하도록 보장한다.
    /// </summary>
    /// <param name="error">발행할 오류 정보</param>
    private void EmitFailure(RoomCreateError error)
    {
        RoomCreateError finalError = error ?? new RoomCreateError(
            RoomCreateErrorCode.Unknown,
            "알 수 없는 오류가 발생했습니다.",
            rawMessage: null,
            retryable: true);

        OnRoomCreateFailed?.Invoke(finalError);
        Log($"Room create failed: code={finalError.Code}, raw={finalError.RawMessage}");
    }

    /// <summary>
    /// 디버그 로그 출력 래퍼 함수다.
    /// 인스펙터 옵션(_debugLogFlow)에 따라 로그 출력을 켜거나 끈다.
    /// </summary>
    /// <param name="message">출력할 로그 메시지</param>
    private void Log(string message)
    {
        if (!_debugLogFlow)
            return;

        UnityEngine.Debug.Log($"[LobbyRoomFlow] {message}");
    }

    /// <summary>
    /// 오브젝트 비활성화 시 진행 중인 룸 생성 요청을 취소한다.
    /// 씬 전환이나 패널 종료 시 백그라운드 요청이 남지 않도록 정리한다.
    /// </summary>
    private void OnDisable()
    {
        CancelCurrentRequest();
    }
}
