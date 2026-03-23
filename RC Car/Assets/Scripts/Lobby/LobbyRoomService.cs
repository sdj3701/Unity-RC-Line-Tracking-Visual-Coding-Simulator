using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class LobbyRoomService
{
    private readonly string _createRoomEndpoint;
    private readonly string _roomStatusEndpointTemplate;
    private readonly string _jobStatusEndpointTemplate;
    private readonly int _requestTimeoutSeconds;

    /// <summary>
    /// 로비 룸 생성/상태조회에 필요한 API 엔드포인트와 타임아웃 설정을 초기화한다.
    /// 템플릿 엔드포인트는 {roomId}, {jobId} 토큰 치환 방식을 지원한다.
    /// </summary>
    /// <param name="createRoomEndpoint">룸 생성 POST 엔드포인트</param>
    /// <param name="roomStatusEndpointTemplate">roomId 기반 상태조회 엔드포인트 템플릿</param>
    /// <param name="jobStatusEndpointTemplate">jobId 기반 상태조회 엔드포인트 템플릿</param>
    /// <param name="requestTimeoutSeconds">HTTP 요청 타임아웃(초)</param>
    public LobbyRoomService(
        string createRoomEndpoint,
        string roomStatusEndpointTemplate,
        string jobStatusEndpointTemplate,
        int requestTimeoutSeconds)
    {
        _createRoomEndpoint = createRoomEndpoint;
        _roomStatusEndpointTemplate = roomStatusEndpointTemplate;
        _jobStatusEndpointTemplate = jobStatusEndpointTemplate;
        _requestTimeoutSeconds = Mathf.Max(1, requestTimeoutSeconds);
    }

    /// <summary>
    /// 룸 생성을 서버에 요청한다.
    /// 요청 본문 직렬화, 인증/멱등 헤더 부착, 네트워크 호출, 공통 응답 파싱까지 한 번에 처리한다.
    /// </summary>
    /// <param name="requestModel">룸 생성 요청 모델</param>
    /// <param name="accessToken">Bearer 인증 토큰</param>
    /// <param name="cancellationToken">요청 취소 토큰</param>
    /// <returns>Ready/Provisioning/Failed/Canceled 중 하나의 결과</returns>
    public async Task<RoomCreateResult> CreateRoomAsync(
        RoomCreateRequest requestModel,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (requestModel == null)
            return BuildValidationFailure("요청 데이터가 없습니다.");

        if (string.IsNullOrWhiteSpace(_createRoomEndpoint))
            return BuildUnknownFailure("방 생성 API URL이 비어 있습니다.", retryable: false);

        var payload = new CreateRoomRequestPayload
        {
            roomName = requestModel.RoomName,
            hostUserId = requestModel.HostUserId
        };

        string requestJson = JsonUtility.ToJson(payload);

        using (var request = new UnityWebRequest(_createRoomEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(requestJson);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = _requestTimeoutSeconds;

            ApplyCommonHeaders(request, accessToken, requestModel.IdempotencyKey);

            bool isCanceled = await SendRequestAsync(request, cancellationToken);
            if (isCanceled)
                return RoomCreateResult.Canceled();

            return ParseRoomResponse(request);
        }
    }

    /// <summary>
    /// 룸 상태 또는 작업(job) 상태를 조회한다.
    /// jobId 우선 규칙으로 엔드포인트를 선택하고 공통 파서로 결과를 해석한다.
    /// </summary>
    /// <param name="roomId">조회할 룸 ID</param>
    /// <param name="jobId">조회할 작업 ID(있으면 우선 사용)</param>
    /// <param name="accessToken">Bearer 인증 토큰</param>
    /// <param name="cancellationToken">요청 취소 토큰</param>
    /// <returns>현재 서버 상태에 해당하는 결과</returns>
    public async Task<RoomCreateResult> GetRoomStatusAsync(
        string roomId,
        string jobId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string endpoint = ResolveStatusEndpoint(roomId, jobId);
        if (string.IsNullOrWhiteSpace(endpoint))
            return BuildUnknownFailure("룸 상태 조회 API URL이 비어 있습니다.");

        using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbGET))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = _requestTimeoutSeconds;

            ApplyCommonHeaders(request, accessToken, idempotencyKey: null);

            bool isCanceled = await SendRequestAsync(request, cancellationToken);
            if (isCanceled)
                return RoomCreateResult.Canceled();

            return ParseRoomResponse(request);
        }
    }

    /// <summary>
    /// 상태 조회에 사용할 실제 URL을 결정한다.
    /// jobId가 있으면 job 상태 엔드포인트를 우선하고, 없으면 room 상태 엔드포인트를 사용한다.
    /// </summary>
    /// <param name="roomId">룸 식별자</param>
    /// <param name="jobId">작업 식별자</param>
    /// <returns>치환 완료된 조회 URL, 없으면 빈 문자열</returns>
    private string ResolveStatusEndpoint(string roomId, string jobId)
    {
        string escapedRoomId = EscapePath(roomId);
        string escapedJobId = EscapePath(jobId);

        if (!string.IsNullOrWhiteSpace(jobId) && !string.IsNullOrWhiteSpace(_jobStatusEndpointTemplate))
            return FillTemplate(_jobStatusEndpointTemplate, escapedRoomId, escapedJobId);

        if (!string.IsNullOrWhiteSpace(_roomStatusEndpointTemplate))
            return FillTemplate(_roomStatusEndpointTemplate, escapedRoomId, escapedJobId);

        return string.Empty;
    }

    /// <summary>
    /// 템플릿 문자열의 {roomId}/{jobId} 또는 string.Format 자리표시자를 실제 값으로 치환한다.
    /// 서로 다른 백엔드 URL 템플릿 규칙을 한 함수에서 수용하기 위한 호환 유틸리티다.
    /// </summary>
    /// <param name="template">URL 템플릿</param>
    /// <param name="roomId">치환할 roomId</param>
    /// <param name="jobId">치환할 jobId</param>
    /// <returns>치환된 URL 문자열</returns>
    private static string FillTemplate(string template, string roomId, string jobId)
    {
        string value = template
            .Replace("{roomId}", roomId)
            .Replace("{jobId}", jobId);

        if (value.Contains("{0}") || value.Contains("{1}"))
        {
            value = string.Format(
                value,
                roomId ?? string.Empty,
                jobId ?? string.Empty);
        }

        return value;
    }

    /// <summary>
    /// URL 경로/쿼리로 들어갈 값을 URL 인코딩한다.
    /// null/공백 입력은 빈 문자열로 처리해 상위 템플릿 치환에서 안전하게 사용할 수 있게 한다.
    /// </summary>
    /// <param name="value">인코딩 대상 문자열</param>
    /// <returns>인코딩된 문자열 또는 빈 문자열</returns>
    private static string EscapePath(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : UnityWebRequest.EscapeURL(value);
    }

    /// <summary>
    /// 모든 요청에 공통으로 필요한 HTTP 헤더를 설정한다.
    /// Content-Type은 항상 JSON으로 고정하고, 토큰/멱등 키는 값이 있을 때만 추가한다.
    /// </summary>
    /// <param name="request">헤더를 설정할 UnityWebRequest</param>
    /// <param name="accessToken">Bearer 토큰</param>
    /// <param name="idempotencyKey">중복 요청 방지용 멱등 키</param>
    private static void ApplyCommonHeaders(UnityWebRequest request, string accessToken, string idempotencyKey)
    {
        request.SetRequestHeader("Content-Type", "application/json");

        if (!string.IsNullOrWhiteSpace(accessToken))
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.SetRequestHeader("Idempotency-Key", idempotencyKey);
    }

    /// <summary>
    /// UnityWebRequest를 비동기로 전송하고 취소 토큰을 감시한다.
    /// 취소 요청이 들어오면 request.Abort()를 호출하고 취소 상태(true)를 반환한다.
    /// </summary>
    /// <param name="request">전송할 요청 객체</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>취소되었으면 true, 정상 완료면 false</returns>
    private static async Task<bool> SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
    {
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                request.Abort();
                return true;
            }

            await Task.Yield();
        }

        return cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// 서버 응답을 공통 규칙으로 파싱해 RoomCreateResult로 변환한다.
    /// 네트워크 오류, HTTP 상태코드, 응답 body(status/jobId/room payload)를 종합해 최종 상태를 결정한다.
    /// </summary>
    /// <param name="request">완료된 UnityWebRequest</param>
    /// <returns>성공(Ready/Provisioning) 또는 실패/취소 결과</returns>
    private static RoomCreateResult ParseRoomResponse(UnityWebRequest request)
    {
        if (request == null)
            return BuildUnknownFailure("요청 객체가 없습니다.");

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            return BuildFailure(
                RoomCreateErrorCode.Network,
                "네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.",
                request.error,
                retryable: true);
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        RoomServiceResponse response = ParseResponseBody(body);

        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;
        bool responseSuccess = response.success || response.isSuccess || httpSuccess;
        RoomProvisioningStatus status = ParseStatus(response.status);
        RoomInfo roomInfo = ExtractRoomInfo(response);
        string jobId = FirstNonEmpty(response.jobId, response.job_id);

        if (status == RoomProvisioningStatus.Unknown)
        {
            if (!string.IsNullOrWhiteSpace(jobId))
                status = RoomProvisioningStatus.Provisioning;
            else if (responseSuccess)
                status = RoomProvisioningStatus.Ready;
        }

        if (!responseSuccess || status == RoomProvisioningStatus.Failed)
        {
            RoomCreateErrorCode code = MapErrorCode(request.responseCode, response.code, status);
            string userMessage = MapUserMessage(code, response.message);
            string rawMessage = FirstNonEmpty(response.error, response.message, request.error, $"HTTP {request.responseCode}");
            bool retryable = code != RoomCreateErrorCode.Auth && code != RoomCreateErrorCode.Conflict;

            return BuildFailure(code, userMessage, rawMessage, retryable);
        }

        if (status == RoomProvisioningStatus.Provisioning)
            return RoomCreateResult.Provisioning(roomInfo, jobId);

        return RoomCreateResult.Ready(roomInfo, jobId);
    }

    /// <summary>
    /// 응답 본문(JSON)을 RoomServiceResponse로 역직렬화한다.
    /// 파싱 실패 시 예외를 외부로 던지지 않고 빈 응답 객체를 반환해 상위 로직이 안전하게 후처리하도록 한다.
    /// </summary>
    /// <param name="body">응답 본문 문자열</param>
    /// <returns>파싱된 응답 객체(실패 시 빈 객체)</returns>
    private static RoomServiceResponse ParseResponseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new RoomServiceResponse();

        try
        {
            RoomServiceResponse parsed = JsonUtility.FromJson<RoomServiceResponse>(body);
            return parsed ?? new RoomServiceResponse();
        }
        catch (Exception)
        {
            return new RoomServiceResponse();
        }
    }

    /// <summary>
    /// 서버 응답의 다양한 필드 명세를 통합해 RoomInfo를 구성한다.
    /// room 중첩 객체와 최상위 필드를 모두 조회해 첫 유효 값을 선택한다.
    /// </summary>
    /// <param name="response">파싱된 서버 응답</param>
    /// <returns>의미 있는 룸 정보가 있으면 RoomInfo, 없으면 null</returns>
    private static RoomInfo ExtractRoomInfo(RoomServiceResponse response)
    {
        RoomPayload payload = response.room ?? new RoomPayload();
        var roomInfo = new RoomInfo
        {
            RoomId = FirstNonEmpty(payload.roomId, payload.id, response.roomId, response.id),
            RoomName = FirstNonEmpty(payload.roomName, payload.name, response.roomName),
            HostUserId = FirstNonEmpty(payload.hostUserId, payload.ownerUserId, response.hostUserId),
            TableName = FirstNonEmpty(payload.tableName, response.tableName),
            NetworkEndpoint = FirstNonEmpty(payload.networkEndpoint, response.networkEndpoint),
            CreatedAtUtc = FirstNonEmpty(payload.createdAtUtc, payload.createdAt, response.createdAtUtc, response.createdAt)
        };

        if (string.IsNullOrWhiteSpace(roomInfo.RoomId) &&
            string.IsNullOrWhiteSpace(roomInfo.RoomName) &&
            string.IsNullOrWhiteSpace(roomInfo.HostUserId))
        {
            return null;
        }

        return roomInfo;
    }

    /// <summary>
    /// 서버의 문자열 상태값을 내부 enum으로 변환한다.
    /// READY/ACTIVE/COMPLETED, PROVISIONING/CREATING/PENDING, FAILED/ERROR를 각각 표준 상태로 매핑한다.
    /// </summary>
    /// <param name="rawStatus">서버 원본 상태 문자열</param>
    /// <returns>내부 표준 프로비저닝 상태</returns>
    private static RoomProvisioningStatus ParseStatus(string rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
            return RoomProvisioningStatus.Unknown;

        string normalized = rawStatus.Trim().ToUpperInvariant();

        if (normalized == "READY" || normalized == "ACTIVE" || normalized == "COMPLETED")
            return RoomProvisioningStatus.Ready;

        if (normalized == "PROVISIONING" || normalized == "CREATING" || normalized == "PENDING")
            return RoomProvisioningStatus.Provisioning;

        if (normalized == "FAILED" || normalized == "ERROR")
            return RoomProvisioningStatus.Failed;

        return RoomProvisioningStatus.Unknown;
    }

    /// <summary>
    /// HTTP 상태코드와 서버 응답 코드를 기반으로 내부 오류 코드를 결정한다.
    /// UI 메시지/재시도 정책 분기를 위한 정규화 단계다.
    /// </summary>
    /// <param name="httpStatusCode">HTTP 응답 코드</param>
    /// <param name="responseCode">서버 비즈니스 오류 코드</param>
    /// <param name="status">파싱된 프로비저닝 상태</param>
    /// <returns>내부 오류 코드</returns>
    private static RoomCreateErrorCode MapErrorCode(long httpStatusCode, string responseCode, RoomProvisioningStatus status)
    {
        if (status == RoomProvisioningStatus.Failed)
            return RoomCreateErrorCode.Server;

        if (httpStatusCode == 401 || httpStatusCode == 403)
            return RoomCreateErrorCode.Auth;
        if (httpStatusCode == 409)
            return RoomCreateErrorCode.Conflict;
        if (httpStatusCode == 408)
            return RoomCreateErrorCode.Timeout;
        if (httpStatusCode >= 500)
            return RoomCreateErrorCode.Server;
        if (httpStatusCode > 0 && httpStatusCode < 500)
            return RoomCreateErrorCode.Unknown;

        if (string.IsNullOrWhiteSpace(responseCode))
            return RoomCreateErrorCode.Unknown;

        string normalized = responseCode.Trim().ToUpperInvariant();
        if (normalized.Contains("AUTH"))
            return RoomCreateErrorCode.Auth;
        if (normalized.Contains("CONFLICT") || normalized.Contains("DUPLICATE"))
            return RoomCreateErrorCode.Conflict;
        if (normalized.Contains("TIMEOUT"))
            return RoomCreateErrorCode.Timeout;
        if (normalized.Contains("VALIDATION"))
            return RoomCreateErrorCode.Validation;

        return RoomCreateErrorCode.Unknown;
    }

    /// <summary>
    /// 오류 코드에 대응하는 기본 사용자 메시지를 생성한다.
    /// 서버 메시지(fallback)가 있으면 우선 사용하고, 없으면 코드 기반 기본 문구를 반환한다.
    /// </summary>
    /// <param name="code">내부 오류 코드</param>
    /// <param name="fallback">서버에서 내려준 사용자 메시지(선택)</param>
    /// <returns>최종 사용자 노출 메시지</returns>
    private static string MapUserMessage(RoomCreateErrorCode code, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        switch (code)
        {
            case RoomCreateErrorCode.Validation:
                return "룸 이름을 확인해 주세요.";
            case RoomCreateErrorCode.Auth:
                return "인증이 만료되었습니다. 다시 로그인해 주세요.";
            case RoomCreateErrorCode.Conflict:
                return "이미 사용 중인 룸 이름입니다.";
            case RoomCreateErrorCode.Timeout:
                return "요청 시간이 초과되었습니다. 다시 시도해 주세요.";
            case RoomCreateErrorCode.Network:
                return "네트워크 연결을 확인해 주세요.";
            case RoomCreateErrorCode.Server:
                return "서버 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.";
            default:
                return "룸 생성 중 오류가 발생했습니다.";
        }
    }

    /// <summary>
    /// 여러 후보 문자열 중 첫 번째 유효값(비어 있지 않은 값)을 반환한다.
    /// 필드명 변형이 있는 API 응답을 유연하게 흡수하기 위한 공통 유틸 함수다.
    /// </summary>
    /// <param name="candidates">우선순위 순으로 나열된 후보 문자열 배열</param>
    /// <returns>첫 유효 문자열 또는 null</returns>
    private static string FirstNonEmpty(params string[] candidates)
    {
        if (candidates == null)
            return null;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[i]))
                return candidates[i];
        }

        return null;
    }

    /// <summary>
    /// 입력 검증 실패를 RoomCreateResult 실패 형식으로 래핑한다.
    /// Validation 코드와 재시도 가능 플래그를 표준화해 반환한다.
    /// </summary>
    /// <param name="message">검증 실패 메시지</param>
    /// <returns>Validation 실패 결과</returns>
    private static RoomCreateResult BuildValidationFailure(string message)
    {
        return BuildFailure(
            RoomCreateErrorCode.Validation,
            userMessage: message,
            rawMessage: message,
            retryable: true);
    }

    /// <summary>
    /// 알 수 없는 실패를 RoomCreateResult로 구성한다.
    /// 설정 누락, 파싱 불가 등 일반 실패를 공통 형식으로 묶는다.
    /// </summary>
    /// <param name="message">사용자/로그 공통 메시지</param>
    /// <param name="retryable">재시도 가능 여부</param>
    /// <returns>Unknown 실패 결과</returns>
    private static RoomCreateResult BuildUnknownFailure(string message, bool retryable = true)
    {
        return BuildFailure(
            RoomCreateErrorCode.Unknown,
            userMessage: message,
            rawMessage: message,
            retryable: retryable);
    }

    /// <summary>
    /// 오류 정보를 RoomCreateResult 실패 객체로 변환한다.
    /// 하위 계층에서 생성한 오류 메타데이터(code/message/retryable)를 손실 없이 상위 계층으로 전달한다.
    /// </summary>
    /// <param name="code">내부 오류 코드</param>
    /// <param name="userMessage">UI 표시 메시지</param>
    /// <param name="rawMessage">로그/디버깅 메시지</param>
    /// <param name="retryable">재시도 가능 여부</param>
    /// <returns>Failed 결과 객체</returns>
    private static RoomCreateResult BuildFailure(
        RoomCreateErrorCode code,
        string userMessage,
        string rawMessage,
        bool retryable)
    {
        return RoomCreateResult.Failed(new RoomCreateError(code, userMessage, rawMessage, retryable));
    }

#pragma warning disable CS0649

    [Serializable]
    private class CreateRoomRequestPayload
    {
        public string roomName;
        public string hostUserId;
    }

    [Serializable]
    private class RoomServiceResponse
    {
        public bool success;
        public bool isSuccess;
        public string status;

        public string roomId;
        public string id;
        public string roomName;
        public string hostUserId;
        public string tableName;
        public string networkEndpoint;
        public string createdAtUtc;
        public string createdAt;

        public string jobId;
        public string job_id;

        public string code;
        public string message;
        public string error;

        public RoomPayload room;
    }

    [Serializable]
    private class RoomPayload
    {
        public string roomId;
        public string id;
        public string roomName;
        public string name;
        public string hostUserId;
        public string ownerUserId;
        public string tableName;
        public string networkEndpoint;
        public string createdAtUtc;
        public string createdAt;
    }

#pragma warning restore CS0649
}
