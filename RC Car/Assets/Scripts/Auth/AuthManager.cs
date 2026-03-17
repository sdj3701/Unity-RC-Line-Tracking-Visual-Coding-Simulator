using System;
using System.Threading.Tasks;
using Auth.Models;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Auth
{
    /// <summary>
    /// 인증 상태, 로그인 흐름, 토큰 검증, 씬 전환을 관리한다.
    /// </summary>
    public class AuthManager : MonoBehaviour
    {
        public static AuthManager Instance { get; private set; }

        [Header("Server")]
        [Tooltip("ID/PW 로그인 API URL")]
        [SerializeField] private string _loginEndpoint = "http://ioteacher.com/api/auth/login";

        [Tooltip("토큰 검증 API URL")]
        [FormerlySerializedAs("_serverBaseUrl")]
        [SerializeField] private string _tokenValidationUrl = "http://ioteacher.com/api/users/me-by-token";

        [Tooltip("요청 타임아웃(초)")]
        [SerializeField] private int _requestTimeoutSeconds = 15;

        [Header("Scenes")]
        [FormerlySerializedAs("_loginSceneName")]
        [SerializeField] private string _loginSceneName = "00_Login";

        [FormerlySerializedAs("_gameSceneName")]
        [SerializeField] private string _gameSceneName = "01_Lobby";

        [Header("Runtime")]
        [SerializeField] private bool _useAutoLogin = true;

        [Header("Editor Test")]
        [SerializeField] private bool _useTestTokenInEditor = false;
        [SerializeField] private string _testToken = string.Empty;

        public bool IsAuthenticated { get; private set; }
        public UserInfo CurrentUser { get; private set; }
        public string LastAuthErrorMessage { get; private set; }

        public event Action OnLoginSuccess;
        public event Action<string> OnLoginFailed;

        private string _accessToken;
        private string _refreshToken;

        private bool _isAuthenticating;
        private bool _isCredentialLoginInProgress;

        private AuthApiClient _authApiClient;

        public bool IsAuthenticating => _isAuthenticating || _isCredentialLoginInProgress;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureApiClient();
        }

        private void Start()
        {
#if UNITY_EDITOR
            if (_useTestTokenInEditor && !string.IsNullOrWhiteSpace(_testToken))
            {
                _ = AuthenticateWithTokenAsync(_testToken);
                return;
            }
#endif
            if (_useAutoLogin)
                TryAutoLogin();
        }

        public async Task<LoginResult> LoginWithCredentialsAsync(string id, string password)
        {
            if (_isCredentialLoginInProgress || _isAuthenticating)
            {
                return new LoginResult
                {
                    IsSuccess = false,
                    ErrorCode = AuthErrorMapper.AuthenticationBusy,
                    ErrorMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.AuthenticationBusy),
                    Retryable = true
                };
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                return new LoginResult
                {
                    IsSuccess = false,
                    ErrorCode = AuthErrorMapper.ValidationError,
                    ErrorMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.ValidationError),
                    Retryable = true
                };
            }

            _isCredentialLoginInProgress = true;

            try
            {
                EnsureApiClient();
                LoginResult loginResult = await _authApiClient.LoginWithIdPasswordAsync(id, password);

                if (!loginResult.IsSuccess)
                {
                    LastAuthErrorMessage = loginResult.ErrorMessage;
                    OnLoginFailed?.Invoke(loginResult.ErrorMessage);
                    return loginResult;
                }

                bool tokenVerified = await AuthenticateWithTokenAsync(loginResult.AccessToken, loginResult.RefreshToken);
                if (tokenVerified)
                {
                    loginResult.User = CurrentUser;
                    return loginResult;
                }

                return new LoginResult
                {
                    IsSuccess = false,
                    ErrorCode = AuthErrorMapper.UnknownError,
                    ErrorMessage = string.IsNullOrWhiteSpace(LastAuthErrorMessage)
                        ? AuthErrorMapper.ToUserMessage(AuthErrorMapper.UnknownError)
                        : LastAuthErrorMessage,
                    Retryable = true,
                    StatusCode = loginResult.StatusCode
                };
            }
            catch (Exception e)
            {
                string message = AuthErrorMapper.ToUserMessage(AuthErrorMapper.NetworkError, e.Message);
                LastAuthErrorMessage = message;
                OnLoginFailed?.Invoke(message);

                return new LoginResult
                {
                    IsSuccess = false,
                    ErrorCode = AuthErrorMapper.NetworkError,
                    ErrorMessage = message,
                    Retryable = true
                };
            }
            finally
            {
                _isCredentialLoginInProgress = false;
            }
        }

        public async void AuthenticateWithToken(string accessToken, string refreshToken = null)
        {
            await AuthenticateWithTokenAsync(accessToken, refreshToken);
        }

        public async Task<bool> AuthenticateWithTokenAsync(
            string accessToken,
            string refreshToken = null,
            bool suppressFailureFeedback = false)
        {
            if (_isAuthenticating)
            {
                Debug.LogWarning("[AuthManager] Token validation is already in progress.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                if (!suppressFailureFeedback)
                {
                    string message = AuthErrorMapper.ToUserMessage(AuthErrorMapper.ValidationError, "Access token is empty.");
                    LastAuthErrorMessage = message;
                    OnLoginFailed?.Invoke(message);
                }
                return false;
            }

            _isAuthenticating = true;
            LastAuthErrorMessage = null;
            _accessToken = accessToken;
            _refreshToken = refreshToken;

            try
            {
                AuthResult result = await ValidateTokenWithServer(accessToken);
                if (result.IsSuccess)
                {
                    IsAuthenticated = true;
                    CurrentUser = result.User;

                    SaveTokenLocally(accessToken, refreshToken);
                    LastAuthErrorMessage = null;

                    OnLoginSuccess?.Invoke();

                    if (!string.IsNullOrWhiteSpace(_gameSceneName))
                        SceneManager.LoadScene(_gameSceneName);

                    return true;
                }

                IsAuthenticated = false;
                CurrentUser = null;
                _accessToken = null;
                _refreshToken = null;
                AuthSessionStore.Clear();

                string failureMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? AuthErrorMapper.ToUserMessage(AuthErrorMapper.UnknownError)
                    : result.ErrorMessage;

                if (!suppressFailureFeedback)
                {
                    Debug.LogError($"[AuthManager] Token validation failed: {failureMessage}");
                    LastAuthErrorMessage = failureMessage;
                    OnLoginFailed?.Invoke(failureMessage);
                    LoadLoginSceneIfNeeded();
                }

                return false;
            }
            catch (Exception e)
            {
                IsAuthenticated = false;
                CurrentUser = null;
                _accessToken = null;
                _refreshToken = null;
                AuthSessionStore.Clear();

                string message = AuthErrorMapper.ToUserMessage(AuthErrorMapper.NetworkError, e.Message);
                if (!suppressFailureFeedback)
                {
                    Debug.LogError($"[AuthManager] Authentication error: {e.Message}");
                    LastAuthErrorMessage = message;
                    OnLoginFailed?.Invoke(message);
                    LoadLoginSceneIfNeeded();
                }

                return false;
            }
            finally
            {
                _isAuthenticating = false;
            }
        }

        private async void TryAutoLogin()
        {
            string savedToken = AuthSessionStore.GetAccessToken();
            string savedRefreshToken = AuthSessionStore.GetRefreshToken();

            if (string.IsNullOrWhiteSpace(savedToken))
                return;

            await AuthenticateWithTokenAsync(savedToken, savedRefreshToken, suppressFailureFeedback: true);
        }

        private async Task<AuthResult> ValidateTokenWithServer(string token)
        {
            using (var request = new UnityWebRequest(_tokenValidationUrl, UnityWebRequest.kHttpVerbGET))
            {
                request.SetRequestHeader("Authorization", $"Bearer {token}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorCode = MapTokenValidationErrorCode(request);
                    return new AuthResult
                    {
                        IsSuccess = false,
                        ErrorMessage = AuthErrorMapper.ToUserMessage(errorCode, request.error)
                    };
                }

                string responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                AuthResult parsedResult = ParseValidationResponse(responseBody);
                if (parsedResult.IsSuccess)
                    return parsedResult;

                return new AuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(parsedResult.ErrorMessage)
                        ? AuthErrorMapper.ToUserMessage(AuthErrorMapper.UnknownError)
                        : parsedResult.ErrorMessage
                };
            }
        }

        private static AuthResult ParseValidationResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return new AuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.UnknownError)
                };
            }

            try
            {
                AuthResponse wrapper = JsonUtility.FromJson<AuthResponse>(responseBody);
                if (wrapper != null)
                {
                    bool wrappedSuccess = wrapper.success && wrapper.user != null && !string.IsNullOrWhiteSpace(wrapper.user.userId);
                    if (wrappedSuccess)
                    {
                        return new AuthResult
                        {
                            IsSuccess = true,
                            User = wrapper.user
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(wrapper.error))
                    {
                        return new AuthResult
                        {
                            IsSuccess = false,
                            ErrorMessage = wrapper.error
                        };
                    }
                }
            }
            catch (Exception)
            {
                // Backend response format may vary.
            }

            try
            {
                UserInfo userInfo = JsonUtility.FromJson<UserInfo>(responseBody);
                if (userInfo != null && !string.IsNullOrWhiteSpace(userInfo.userId))
                {
                    return new AuthResult
                    {
                        IsSuccess = true,
                        User = userInfo
                    };
                }
            }
            catch (Exception)
            {
                // Backend response format may vary.
            }

            return new AuthResult
            {
                IsSuccess = false,
                ErrorMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.UnknownError)
            };
        }

        private static string MapTokenValidationErrorCode(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return AuthErrorMapper.NetworkError;
            }

            if (request.responseCode == 401)
                return AuthErrorMapper.TokenExpiredOrInvalid;

            if (request.responseCode == 500)
                return AuthErrorMapper.InternalError;

            return AuthErrorMapper.UnknownError;
        }

        private void SaveTokenLocally(string accessToken, string refreshToken)
        {
            AuthSessionStore.Save(accessToken, refreshToken);
        }

        private void LoadLoginSceneIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_loginSceneName))
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.name.Equals(_loginSceneName, StringComparison.Ordinal))
                SceneManager.LoadScene(_loginSceneName);
        }

        private void EnsureApiClient()
        {
            if (_authApiClient == null)
                _authApiClient = new AuthApiClient(_loginEndpoint, _requestTimeoutSeconds);
        }

        public string GetAccessToken() => _accessToken;

        public void Logout()
        {
            _accessToken = null;
            _refreshToken = null;
            IsAuthenticated = false;
            CurrentUser = null;
            LastAuthErrorMessage = null;

            AuthSessionStore.Clear();
            LoadLoginSceneIfNeeded();
        }
    }
}
