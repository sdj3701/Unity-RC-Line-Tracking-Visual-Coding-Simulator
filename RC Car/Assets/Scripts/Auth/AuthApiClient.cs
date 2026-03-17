using System;
using System.Text;
using System.Threading.Tasks;
using Auth.Models;
using UnityEngine;
using UnityEngine.Networking;

namespace Auth
{
    /// <summary>
    /// 로그인 API 통신을 전담한다.
    /// </summary>
    public class AuthApiClient
    {
        private readonly string _loginUrl;
        private readonly int _timeoutSeconds;

        /// <summary>
        /// 로그인 API 호출 전용 클라이언트를 생성한다.
        /// </summary>
        /// <param name="loginUrl">ID/PW 로그인 엔드포인트 URL</param>
        /// <param name="timeoutSeconds">요청 타임아웃(초)</param>
        public AuthApiClient(string loginUrl, int timeoutSeconds = 15)
        {
            _loginUrl = loginUrl;
            _timeoutSeconds = Mathf.Max(1, timeoutSeconds);
        }

        /// <summary>
        /// ID/PW를 서버로 전송해 로그인 결과(토큰/에러)를 반환한다.
        /// </summary>
        /// <param name="id">사용자 ID</param>
        /// <param name="password">사용자 비밀번호</param>
        /// <returns>로그인 성공/실패 정보를 담은 <see cref="LoginResult"/></returns>
        public async Task<LoginResult> LoginWithIdPasswordAsync(string id, string password)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                return CreateFailedResult(AuthErrorMapper.ValidationError, 0, "ID/PW is empty.", retryable: true);
            }

            var payload = new LoginRequest
            {
                id = id.Trim(),
                password = password
            };

            string json = JsonUtility.ToJson(payload);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(_loginUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = _timeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await Task.Yield();

                return ParseLoginResult(request);
            }
        }

        /// <summary>
        /// 로그인 API 응답을 도메인 결과 객체로 파싱한다.
        /// </summary>
        /// <param name="request">완료된 UnityWebRequest</param>
        /// <returns>파싱된 로그인 결과</returns>
        private static LoginResult ParseLoginResult(UnityWebRequest request)
        {
            string responseBody = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            LoginResponse parsedResponse = TryParseResponse(responseBody);
            bool hasHttpSuccess = request.result == UnityWebRequest.Result.Success;

            if (hasHttpSuccess &&
                parsedResponse != null &&
                parsedResponse.success &&
                !string.IsNullOrWhiteSpace(parsedResponse.accessToken))
            {
                return new LoginResult
                {
                    IsSuccess = true,
                    AccessToken = parsedResponse.accessToken,
                    RefreshToken = parsedResponse.refreshToken,
                    User = parsedResponse.user,
                    StatusCode = request.responseCode,
                    Retryable = false
                };
            }

            string errorCode = ResolveErrorCode(request, parsedResponse);
            string errorMessage = AuthErrorMapper.ToUserMessage(errorCode, parsedResponse?.message);
            bool retryable = parsedResponse != null
                ? parsedResponse.retryable
                : errorCode == AuthErrorMapper.NetworkError ||
                  errorCode == AuthErrorMapper.InternalError ||
                  errorCode == AuthErrorMapper.UnknownError;

            return new LoginResult
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Retryable = retryable,
                StatusCode = request.responseCode
            };
        }

        /// <summary>
        /// JSON 문자열을 <see cref="LoginResponse"/>로 안전하게 파싱한다.
        /// </summary>
        /// <param name="responseBody">응답 본문(JSON)</param>
        /// <returns>파싱 성공 시 객체, 실패 시 null</returns>
        private static LoginResponse TryParseResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                return JsonUtility.FromJson<LoginResponse>(responseBody);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 네트워크 상태, 서버 에러코드, HTTP 상태코드를 기준으로 최종 에러코드를 결정한다.
        /// </summary>
        /// <param name="request">완료된 요청 객체</param>
        /// <param name="parsedResponse">파싱된 응답 객체(없을 수 있음)</param>
        /// <returns>내부 표준 에러코드</returns>
        private static string ResolveErrorCode(UnityWebRequest request, LoginResponse parsedResponse)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return AuthErrorMapper.NetworkError;
            }

            if (parsedResponse != null && !string.IsNullOrWhiteSpace(parsedResponse.errorCode))
                return parsedResponse.errorCode.Trim().ToUpperInvariant();

            return AuthErrorMapper.FromStatusCode(request.responseCode);
        }

        /// <summary>
        /// 공통 형식의 로그인 실패 결과를 생성한다.
        /// </summary>
        /// <param name="errorCode">내부 에러코드</param>
        /// <param name="statusCode">HTTP 상태코드</param>
        /// <param name="fallbackMessage">서버 메시지가 없을 때 사용할 보조 메시지</param>
        /// <param name="retryable">재시도 가능 여부</param>
        /// <returns>실패 결과 객체</returns>
        private static LoginResult CreateFailedResult(string errorCode, long statusCode, string fallbackMessage, bool retryable)
        {
            return new LoginResult
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = AuthErrorMapper.ToUserMessage(errorCode, fallbackMessage),
                Retryable = retryable,
                StatusCode = statusCode
            };
        }
    }
}
