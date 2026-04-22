using System;

public enum RoomProvisioningStatus
{
    Unknown,
    Ready,
    Provisioning,
    Failed
}

public enum RoomCreateErrorCode
{
    None,
    Validation,
    Busy,
    Network,
    Auth,
    Conflict,
    Timeout,
    Server,
    Unknown,
    Canceled
}

[Serializable]
public class RoomInfo
{
    public string RoomId;
    public string ApiRoomId;
    public string PhotonSessionName;
    public string RoomName;
    public string HostUserId;
    public string TableName;
    public string NetworkEndpoint;
    public string CreatedAtUtc;
}

[Serializable]
public class RoomProvisioningProgress
{
    public string RoomId;
    public string JobId;
    public int PollCount;
    public float ElapsedSeconds;
    public string Status;
}

public sealed class RoomCreateRequest
{
    /// <summary>
    /// 룸 생성 요청에 필요한 최소 데이터를 생성한다.
    /// Flow 계층에서 검증을 끝낸 값을 이 객체로 묶어 Service 계층으로 전달한다.
    /// </summary>
    /// <param name="roomName">사용자가 입력한 룸 이름</param>
    /// <param name="hostUserId">현재 로그인 사용자 ID</param>
    /// <param name="idempotencyKey">중복 생성 요청을 방지하기 위한 멱등 키</param>
    public RoomCreateRequest(string roomName, string hostUserId, string idempotencyKey)
    {
        RoomName = roomName;
        HostUserId = hostUserId;
        IdempotencyKey = idempotencyKey;
    }

    public string RoomName { get; }
    public string HostUserId { get; }
    public string IdempotencyKey { get; }
}

public sealed class RoomCreateError
{
    /// <summary>
    /// 룸 생성 실패 정보를 구조화한다.
    /// UI는 <see cref="UserMessage"/>를 표시하고, 디버깅/로그는 <see cref="RawMessage"/>를 사용한다.
    /// </summary>
    /// <param name="code">오류 유형 코드</param>
    /// <param name="userMessage">사용자에게 노출할 메시지</param>
    /// <param name="rawMessage">개발 로그/원인 추적용 원문 메시지</param>
    /// <param name="retryable">재시도 가능 여부</param>
    public RoomCreateError(RoomCreateErrorCode code, string userMessage, string rawMessage, bool retryable)
    {
        Code = code;
        UserMessage = userMessage;
        RawMessage = rawMessage;
        Retryable = retryable;
    }

    public RoomCreateErrorCode Code { get; }
    public string UserMessage { get; }
    public string RawMessage { get; }
    public bool Retryable { get; }
}

public sealed class RoomCreateResult
{
    /// <summary>
    /// 룸 생성 처리 결과를 단일 객체로 생성한다.
    /// 성공/실패/취소 상태와 추가 데이터(roomInfo, jobId, error)를 함께 관리한다.
    /// </summary>
    /// <param name="isSuccess">요청 처리 성공 여부</param>
    /// <param name="isCanceled">요청 취소 여부</param>
    /// <param name="status">현재 프로비저닝 상태</param>
    /// <param name="roomInfo">생성된 룸 정보(성공 시)</param>
    /// <param name="jobId">비동기 프로비저닝 작업 ID</param>
    /// <param name="error">실패 상세 정보</param>
    private RoomCreateResult(
        bool isSuccess,
        bool isCanceled,
        RoomProvisioningStatus status,
        RoomInfo roomInfo,
        string jobId,
        RoomCreateError error)
    {
        IsSuccess = isSuccess;
        IsCanceled = isCanceled;
        Status = status;
        RoomInfo = roomInfo;
        JobId = jobId;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsCanceled { get; }
    public RoomProvisioningStatus Status { get; }
    public RoomInfo RoomInfo { get; }
    public string JobId { get; }
    public RoomCreateError Error { get; }

    /// <summary>
    /// 룸 준비가 즉시 완료된 성공 결과를 생성한다.
    /// 서버 응답이 READY/ACTIVE 성격일 때 사용한다.
    /// </summary>
    /// <param name="roomInfo">즉시 사용 가능한 룸 정보</param>
    /// <param name="jobId">선택적 작업 ID</param>
    /// <returns>Ready 상태 성공 결과</returns>
    public static RoomCreateResult Ready(RoomInfo roomInfo, string jobId = null)
    {
        return new RoomCreateResult(
            isSuccess: true,
            isCanceled: false,
            status: RoomProvisioningStatus.Ready,
            roomInfo: roomInfo,
            jobId: jobId,
            error: null);
    }

    /// <summary>
    /// 룸 생성은 수락되었지만 서버 준비가 진행 중인 결과를 생성한다.
    /// Flow는 이 결과를 받으면 상태 조회 폴링으로 전환한다.
    /// </summary>
    /// <param name="roomInfo">현재까지 확보된 룸 정보</param>
    /// <param name="jobId">준비 상태 추적용 작업 ID</param>
    /// <returns>Provisioning 상태 성공 결과</returns>
    public static RoomCreateResult Provisioning(RoomInfo roomInfo, string jobId)
    {
        return new RoomCreateResult(
            isSuccess: true,
            isCanceled: false,
            status: RoomProvisioningStatus.Provisioning,
            roomInfo: roomInfo,
            jobId: jobId,
            error: null);
    }

    /// <summary>
    /// 룸 생성 실패 결과를 생성한다.
    /// 실패 사유를 <see cref="RoomCreateError"/>에 담아 상위 계층으로 전달한다.
    /// </summary>
    /// <param name="error">실패 상세 정보</param>
    /// <param name="status">실패 시점의 상태(기본값: Failed)</param>
    /// <returns>실패 결과</returns>
    public static RoomCreateResult Failed(RoomCreateError error, RoomProvisioningStatus status = RoomProvisioningStatus.Failed)
    {
        return new RoomCreateResult(
            isSuccess: false,
            isCanceled: false,
            status: status,
            roomInfo: null,
            jobId: null,
            error: error);
    }

    /// <summary>
    /// 사용자 취소 또는 화면 종료 등으로 요청이 중단된 결과를 생성한다.
    /// 취소는 실패와 별도로 취급해 UI가 구분된 메시지를 보여줄 수 있게 한다.
    /// </summary>
    /// <returns>취소 결과</returns>
    public static RoomCreateResult Canceled()
    {
        return new RoomCreateResult(
            isSuccess: false,
            isCanceled: true,
            status: RoomProvisioningStatus.Unknown,
            roomInfo: null,
            jobId: null,
            error: new RoomCreateError(
                RoomCreateErrorCode.Canceled,
                "요청이 취소되었습니다.",
                rawMessage: null,
                retryable: true));
    }
}
