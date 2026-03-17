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

        public AuthApiClient(string loginUrl, int timeoutSeconds = 15)
        {
            _loginUrl = loginUrl;
            _timeoutSeconds = Mathf.Max(1, timeoutSeconds);
        }

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
