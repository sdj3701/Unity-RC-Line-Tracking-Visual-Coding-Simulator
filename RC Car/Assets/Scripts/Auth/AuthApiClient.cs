using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth.Models;
using UnityEngine;
using UnityEngine.Networking;

namespace Auth
{
    /// <summary>
    /// ID/PW 濡쒓렇??API ?몄텧 ?꾨떞 ?대씪?댁뼵??
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

        /// <summary>
        /// userId/password濡?濡쒓렇??API瑜??몄텧?섍퀬 寃곌낵瑜?LoginResult濡?蹂?섑븳??
        /// </summary>
        public async Task<LoginResult> LoginWithIdPasswordAsync(string userId, string password)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                return CreateFailedResult(
                    AuthErrorMapper.ValidationError,
                    statusCode: 0,
                    fallbackMessage: "?꾩씠?붿? 鍮꾨?踰덊샇瑜??낅젰??二쇱꽭??",
                    retryable: true);
            }

            var payload = new LoginRequest
            {
                // ?쒕쾭 怨꾩빟: userId / password
                userId = userId.Trim(),
                password = password
            };

            string requestJson = JsonUtility.ToJson(payload);
            byte[] body = Encoding.UTF8.GetBytes(requestJson);

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
        /// 濡쒓렇???묐떟???깃났/?ㅽ뙣 紐⑤뜽濡??쒖??뷀븳??
        /// </summary>
        private static LoginResult ParseLoginResult(UnityWebRequest request)
        {
            string responseBody = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            LoginResponse parsedResponse = TryParseResponse(responseBody);

            // ?먮쫫 二쇱꽍:
            // 1) HTTP ?깃났 ?щ? ?뺤씤
            // 2) ?ㅼ뼇???ㅻ챸?먯꽌 access token 異붿텧
            // 3) ?좏겙???덉쑝硫?success ?뚮옒洹몄? 臾닿??섍쾶 濡쒓렇???깃났 泥섎━
            bool hasHttpSuccess = IsHttpSuccess(request);
            string accessToken = ResolveAccessToken(parsedResponse, responseBody);
            string refreshToken = ResolveRefreshToken(parsedResponse, responseBody);

            if (hasHttpSuccess && !string.IsNullOrWhiteSpace(accessToken))
            {
                return new LoginResult
                {
                    IsSuccess = true,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    User = parsedResponse?.user,
                    StatusCode = request.responseCode,
                    Retryable = false
                };
            }

            string errorCode = ResolveErrorCode(request, parsedResponse);
            string fallbackMessage = ResolveServerMessage(parsedResponse, responseBody);
            string errorMessage = AuthErrorMapper.ToUserMessage(errorCode, fallbackMessage);
            bool retryable = ResolveRetryable(parsedResponse, errorCode);

            return new LoginResult
            {
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Retryable = retryable,
                StatusCode = request.responseCode
            };
        }

        private static bool IsHttpSuccess(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.Success &&
                   request.responseCode >= 200 &&
                   request.responseCode < 300;
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

        private static string ResolveAccessToken(LoginResponse parsedResponse, string responseBody)
        {
            if (parsedResponse != null)
            {
                string tokenFromModel = FirstNonEmpty(
                    parsedResponse.accessToken,
                    parsedResponse.token,
                    parsedResponse.access_token,
                    parsedResponse.data?.accessToken,
                    parsedResponse.data?.token,
                    parsedResponse.data?.access_token);

                if (!string.IsNullOrWhiteSpace(tokenFromModel))
                    return tokenFromModel;
            }

            return ExtractJsonStringValue(responseBody, "accessToken", "token", "access_token");
        }

        private static string ResolveRefreshToken(LoginResponse parsedResponse, string responseBody)
        {
            if (parsedResponse != null)
            {
                string refreshFromModel = FirstNonEmpty(
                    parsedResponse.refreshToken,
                    parsedResponse.refresh,
                    parsedResponse.refresh_token,
                    parsedResponse.data?.refreshToken,
                    parsedResponse.data?.refresh,
                    parsedResponse.data?.refresh_token);

                if (!string.IsNullOrWhiteSpace(refreshFromModel))
                    return refreshFromModel;
            }

            return ExtractJsonStringValue(responseBody, "refreshToken", "refresh", "refresh_token");
        }

        private static string ResolveErrorCode(UnityWebRequest request, LoginResponse parsedResponse)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return AuthErrorMapper.NetworkError;
            }

            string serverCode = FirstNonEmpty(parsedResponse?.errorCode, parsedResponse?.code);
            if (!string.IsNullOrWhiteSpace(serverCode))
                return serverCode.Trim().ToUpperInvariant();

            return AuthErrorMapper.FromStatusCode(request.responseCode);
        }

        private static string ResolveServerMessage(LoginResponse parsedResponse, string responseBody)
        {
            string modelMessage = FirstNonEmpty(
                parsedResponse?.message,
                parsedResponse?.error,
                parsedResponse?.detail);

            if (!string.IsNullOrWhiteSpace(modelMessage))
                return modelMessage;

            return ExtractJsonStringValue(responseBody, "message", "error", "detail");
        }

        private static bool ResolveRetryable(LoginResponse parsedResponse, string errorCode)
        {
            if (parsedResponse != null && parsedResponse.retryable)
                return true;

            return errorCode == AuthErrorMapper.NetworkError ||
                   errorCode == AuthErrorMapper.InternalError ||
                   errorCode == AuthErrorMapper.UnknownError;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return null;

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i];
            }

            return null;
        }

        private static string ExtractJsonStringValue(string json, params string[] keys)
        {
            if (string.IsNullOrWhiteSpace(json) || keys == null || keys.Length == 0)
                return null;

            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"])*)\"";
                Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                string value = match.Groups["v"].Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return Regex.Unescape(value);
            }

            return null;
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
