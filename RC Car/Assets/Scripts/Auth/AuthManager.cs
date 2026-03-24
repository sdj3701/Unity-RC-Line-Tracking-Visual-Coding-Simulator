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
        [SerializeField] private bool _useAutoLogin = false;
        [SerializeField] private bool _useDeepLinkLogin = true;

        [Header("Editor Test")]
        [SerializeField] private bool _useTestTokenInEditor = false;
        [SerializeField] private string _testToken = string.Empty;

        [Header("Debug")]
        [Tooltip("로그인 성공 시 access/refresh token과 UserInfo를 Debug.Log로 출력한다.")]
        [SerializeField] private bool _debugLogLoginPayload = true;

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

        /// <summary>
        /// 인증 매니저 싱글톤을 초기화하고 공용 의존성(API 클라이언트/이벤트 구독)을 준비한다.
        /// </summary>
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
            AuthTokenReceiver.OnTokenReceived += OnDeepLinkTokenReceived;
        }

        /// <summary>
        /// 앱 시작 시 인증 시작 경로를 결정한다.
        /// 에디터 테스트 토큰 -> 딥링크 토큰 -> 자동 로그인 순으로 우선 처리한다.
        /// </summary>
        private async void Start()
        {
#if UNITY_EDITOR
            if (_useTestTokenInEditor && !string.IsNullOrWhiteSpace(_testToken))
            {
                bool editorTokenSuccess = await AuthenticateWithTokenAsync(
                    _testToken,
                    refreshToken: null,
                    suppressFailureFeedback: true);

                if (!editorTokenSuccess)
                    Debug.LogWarning("[AuthManager] Editor test token is invalid or expired.");
                return;
            }
#endif
            if (await TryAuthenticateDeepLinkTokenAtStartupAsync())
                return;

            if (_useAutoLogin)
                TryAutoLogin();
        }

        /// <summary>
        /// 씬/오브젝트 파괴 시 딥링크 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this)
                AuthTokenReceiver.OnTokenReceived -= OnDeepLinkTokenReceived;
        }

        /// <summary>
        /// ID/PW 로그인 API를 호출하고, 응답 토큰으로 최종 인증까지 완료한다.
        /// </summary>
        /// <param name="userId">사용자 로그인 ID</param>
        /// <param name="password">사용자 비밀번호</param>
        /// <returns>로그인 API + 토큰 검증 결과를 담은 <see cref="LoginResult"/></returns>
        public async Task<LoginResult> LoginWithCredentialsAsync(string userId, string password)
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

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
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
                // 흐름 주석:
                // 1) ID/PW 로그인 API 호출
                // 2) 응답 토큰(access/refresh) 추출
                // 3) access token으로 최종 검증 후 인증 상태 확정
                EnsureApiClient();
                LoginResult loginResult = await _authApiClient.LoginWithIdPasswordAsync(userId, password);

                if (!loginResult.IsSuccess)
                {
                    LastAuthErrorMessage = loginResult.ErrorMessage;
                    OnLoginFailed?.Invoke(loginResult.ErrorMessage);
                    return loginResult;
                }

                if (string.IsNullOrWhiteSpace(loginResult.AccessToken))
                {
                    string message = AuthErrorMapper.ToUserMessage(
                        AuthErrorMapper.UnknownError,
                        "로그인 응답에 access token이 없습니다.");

                    LastAuthErrorMessage = message;
                    OnLoginFailed?.Invoke(message);

                    return new LoginResult
                    {
                        IsSuccess = false,
                        ErrorCode = AuthErrorMapper.UnknownError,
                        ErrorMessage = message,
                        Retryable = true,
                        StatusCode = loginResult.StatusCode
                    };
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

        /// <summary>
        /// 토큰 인증 비동기 함수를 간편하게 호출하기 위한 래퍼.
        /// </summary>
        /// <param name="accessToken">검증할 access token</param>
        /// <param name="refreshToken">선택적 refresh token</param>
        public async void AuthenticateWithToken(string accessToken, string refreshToken = null)
        {
            await AuthenticateWithTokenAsync(accessToken, refreshToken);
        }

        /// <summary>
        /// 서버 토큰 검증을 수행하고 인증 상태, 로컬 세션 저장, 씬 전환을 일괄 처리한다.
        /// </summary>
        /// <param name="accessToken">검증할 access token</param>
        /// <param name="refreshToken">저장할 refresh token(선택)</param>
        /// <param name="suppressFailureFeedback">실패 시 사용자 피드백/씬 이동 억제 여부</param>
        /// <returns>인증 성공 여부</returns>
        public async Task<bool> AuthenticateWithTokenAsync(
            string accessToken,
            string refreshToken = null,
            bool suppressFailureFeedback = false)
        {
            // 흐름 주석:
            // 1) access token 입력 검증
            // 2) 서버 토큰 검증 API 호출
            // 3) 성공 시 인증 상태/세션 저장/씬 이동
            // 4) 실패 시 상태 초기화 후 로그인 씬 복귀(옵션)
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
                    DebugLogLoginPayload(accessToken, refreshToken, result.User);
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

        /// <summary>
        /// PlayerPrefs에 저장된 토큰으로 조용한 자동 로그인을 시도한다.
        /// </summary>
        private async void TryAutoLogin()
        {
            string savedToken = AuthSessionStore.GetAccessToken();
            string savedRefreshToken = AuthSessionStore.GetRefreshToken();

            if (string.IsNullOrWhiteSpace(savedToken))
                return;

            await AuthenticateWithTokenAsync(savedToken, savedRefreshToken, suppressFailureFeedback: true);
        }

        /// <summary>
        /// 앱 시작 시 이미 파싱된 딥링크 토큰이 있으면 1회 인증을 시도한다.
        /// </summary>
        /// <returns>딥링크 토큰으로 인증 성공했으면 true</returns>
        private async Task<bool> TryAuthenticateDeepLinkTokenAtStartupAsync()
        {
            if (!_useDeepLinkLogin)
                return false;

            if (AuthTokenReceiver.Instance == null)
                return false;

            string accessToken = AuthTokenReceiver.Instance.GetAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
                return false;

            string refreshToken = AuthTokenReceiver.Instance.GetRefreshToken();
            bool success = await AuthenticateWithTokenAsync(
                accessToken,
                refreshToken,
                suppressFailureFeedback: true);

            if (!success)
                Debug.LogWarning("[AuthManager] Deep-link token is invalid or expired.");

            return success;
        }

        /// <summary>
        /// 런타임 중 새 딥링크 토큰 수신 이벤트를 처리해 추가 인증을 수행한다.
        /// </summary>
        /// <param name="accessToken">딥링크에서 전달된 access token</param>
        /// <param name="refreshToken">딥링크에서 전달된 refresh token</param>
        private async void OnDeepLinkTokenReceived(string accessToken, string refreshToken)
        {
            if (!_useDeepLinkLogin)
                return;

            if (string.IsNullOrWhiteSpace(accessToken))
                return;

            if (IsAuthenticating)
            {
                Debug.LogWarning("[AuthManager] Ignoring deep-link token because another authentication flow is in progress.");
                return;
            }

            bool success = await AuthenticateWithTokenAsync(
                accessToken,
                refreshToken,
                suppressFailureFeedback: true);

            if (!success)
                Debug.LogWarning("[AuthManager] Received deep-link token is invalid or expired.");
        }

        /// <summary>
        /// 토큰 검증 API를 호출하고 응답을 <see cref="AuthResult"/>로 변환한다.
        /// </summary>
        /// <param name="token">검증할 access token</param>
        /// <returns>검증 결과(성공 시 사용자 정보 포함)</returns>
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

        /// <summary>
        /// 토큰 검증 API 응답 본문을 유연하게 파싱한다.
        /// 래퍼 구조(AuthResponse)와 직접 UserInfo 구조를 순차적으로 시도한다.
        /// </summary>
        /// <param name="responseBody">서버 응답 본문(JSON)</param>
        /// <returns>파싱 성공/실패가 반영된 인증 결과</returns>
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

        /// <summary>
        /// UnityWebRequest 실패 상태를 내부 인증 에러 코드로 매핑한다.
        /// </summary>
        /// <param name="request">완료된 토큰 검증 요청 객체</param>
        /// <returns>내부 에러 코드 문자열</returns>
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

        /// <summary>
        /// 인증 성공 토큰을 로컬 세션 저장소에 기록한다.
        /// </summary>
        /// <param name="accessToken">저장할 access token</param>
        /// <param name="refreshToken">저장할 refresh token</param>
        private void SaveTokenLocally(string accessToken, string refreshToken)
        {
            AuthSessionStore.Save(accessToken, refreshToken);
        }

        /// <summary>
        /// 로그인 성공 시 디버깅용 인증 페이로드를 출력한다.
        /// </summary>
        private void DebugLogLoginPayload(string accessToken, string refreshToken, UserInfo user)
        {
            if (!_debugLogLoginPayload)
                return;

            string safeRefreshToken = string.IsNullOrWhiteSpace(refreshToken) ? "(empty)" : refreshToken;
            string userJson = user == null ? "null" : JsonUtility.ToJson(user, true);

            // 흐름 주석:
            // 로그인 성공 직후 토큰과 사용자 정보를 한 번에 출력해
            // API 응답/파싱/검증 결과를 빠르게 확인한다.
            Debug.Log(
                $"[AuthManager] Login Success Payload\n" +
                $"AccessToken: {accessToken}\n" +
                $"RefreshToken: {safeRefreshToken}\n" +
                $"UserInfo: {userJson}");
        }

        /// <summary>
        /// 현재 씬이 로그인 씬이 아닐 때만 로그인 씬으로 복귀시킨다.
        /// </summary>
        private void LoadLoginSceneIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_loginSceneName))
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.name.Equals(_loginSceneName, StringComparison.Ordinal))
                SceneManager.LoadScene(_loginSceneName);
        }

        /// <summary>
        /// 로그인 API 클라이언트가 비어 있을 때 지연 생성한다.
        /// </summary>
        private void EnsureApiClient()
        {
            if (_authApiClient == null)
                _authApiClient = new AuthApiClient(_loginEndpoint, _requestTimeoutSeconds);
        }

        /// <summary>
        /// 현재 메모리에 보관 중인 access token을 반환한다.
        /// </summary>
        public string GetAccessToken() => _accessToken;

        /// <summary>
        /// 인증 상태/토큰을 초기화하고 저장소를 비운 뒤 로그인 씬으로 복귀한다.
        /// </summary>
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
