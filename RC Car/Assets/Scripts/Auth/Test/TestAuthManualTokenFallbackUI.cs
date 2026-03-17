using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Auth
{
    /// <summary>
    /// Runtime fallback UI used when deep-link token is missing.
    /// Shows manual token input and quit button on Login scene.
    /// </summary>
    public class TestAuthManualTokenFallbackUI : MonoBehaviour
    {
        private const string LoginSceneName = "00_TestLogin";
        private const string PrimaryLoginSceneName = "00_Login";
        private const float PanelWidth = 1200f;
        private const float PanelHeight = 460f;
        private const float UiDelaySeconds = 0.35f;
        private const int TitleFontSize = 30;
        private const int BodyFontSize = 30;
        private const int StatusFontSize = 26;

        private string _accessToken = string.Empty;
        private string _refreshToken = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isSubmitting;
        private float _sceneLoadedAt;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;

        /// <summary>
        /// 테스트 로그인 씬에서 수동 토큰 입력 UI를 자동 생성한다.
        /// 메인(AuthManager) 플로우가 활성화된 경우에는 생성하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (ShouldSkipForPrimaryFlow())
                return;

            if (FindObjectOfType<TestAuthManualTokenFallbackUI>() != null)
                return;

            var go = new GameObject(nameof(TestAuthManualTokenFallbackUI));
            DontDestroyOnLoad(go);
            go.AddComponent<TestAuthManualTokenFallbackUI>();
        }

        /// <summary>
        /// 현재 실행이 메인 인증 플로우인지 판별해 테스트 UI 생성 여부를 결정한다.
        /// </summary>
        /// <returns>메인 플로우면 true</returns>
        private static bool ShouldSkipForPrimaryFlow()
        {
            if (FindObjectOfType<AuthManager>() != null)
                return true;

            return SceneManager.GetActiveScene().name.Equals(PrimaryLoginSceneName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 씬 로드 이벤트를 구독하고 유예 시간 기준점을 초기화한다.
        /// </summary>
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _sceneLoadedAt = Time.unscaledTime;
        }

        /// <summary>
        /// 씬 로드 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 씬 전환 시 상태 메시지/제출 상태/유예 타이머를 초기화한다.
        /// </summary>
        /// <param name="scene">로드 완료된 씬</param>
        /// <param name="mode">씬 로드 모드</param>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneLoadedAt = Time.unscaledTime;
            _isSubmitting = false;

            if (!scene.name.Equals(LoginSceneName, StringComparison.Ordinal))
                _statusMessage = string.Empty;
        }

        /// <summary>
        /// 테스트용 수동 토큰 로그인 UI를 렌더링하고 버튼 입력을 처리한다.
        /// </summary>
        private void OnGUI()
        {
            if (!ShouldShow())
                return;

            EnsureStyles();

            var panelRect = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                (Screen.height - PanelHeight) * 0.5f,
                PanelWidth,
                PanelHeight);

            GUI.Box(panelRect, "Manual Token Login", _boxStyle);

            float x = panelRect.x + 20f;
            float y = panelRect.y + 70f;
            float width = panelRect.width - 40f;

            GUI.Label(new Rect(x, y, width, 36f), "Access Token", _labelStyle);
            y += 42f;
            _accessToken = GUI.TextField(new Rect(x, y, width, 48f), _accessToken, _textFieldStyle);

            y += 60f;
            GUI.Label(new Rect(x, y, width, 36f), "Refresh Token (Optional)", _labelStyle);
            y += 42f;
            _refreshToken = GUI.TextField(new Rect(x, y, width, 48f), _refreshToken, _textFieldStyle);

            y += 58f;
            if (!string.IsNullOrEmpty(_statusMessage))
                GUI.Label(new Rect(x, y, width, 56f), _statusMessage, _statusStyle);

            float buttonY = panelRect.yMax - 68f;
            if (GUI.Button(new Rect(x, buttonY, 220f, 48f), _isSubmitting ? "Validating..." : "Login", _buttonStyle))
            {
                if (!_isSubmitting)
                    SubmitToken();
            }

            if (GUI.Button(new Rect(x + 234f, buttonY, 160f, 48f), "Quit", _buttonStyle))
            {
                QuitApp();
            }
        }

        /// <summary>
        /// 수동 토큰 입력 UI를 보여줄지 판단한다.
        /// 자동 인증 흐름과 충돌하지 않도록 유예 시간/인증 상태를 함께 확인한다.
        /// </summary>
        /// <returns>표시 필요 시 true</returns>
        private bool ShouldShow()
        {
            if (!SceneManager.GetActiveScene().name.Equals(LoginSceneName, StringComparison.Ordinal))
                return false;

            var authManager = TestAuthManager.Instance;
            if (authManager != null)
            {
                if (authManager.IsAuthenticated)
                    return false;

                if (authManager.IsAuthenticating)
                    return false;
            }

            // Give auto login / deep-link flow a short grace period.
            if (Time.unscaledTime - _sceneLoadedAt < UiDelaySeconds)
                return false;

            if (AuthTokenReceiver.Instance != null &&
                !string.IsNullOrEmpty(AuthTokenReceiver.Instance.GetAccessToken()))
            {
                return false;
            }

            if (string.IsNullOrEmpty(_statusMessage))
                _statusMessage = "No deep-link token found. Enter token manually or quit.";

            return true;
        }

        /// <summary>
        /// 수동 입력된 토큰으로 테스트 인증을 비동기로 수행한다.
        /// </summary>
        private async void SubmitToken()
        {
            string accessToken = _accessToken.Trim();
            if (string.IsNullOrEmpty(accessToken))
            {
                _statusMessage = "Access token is required.";
                return;
            }

            var authManager = TestAuthManager.Instance;
            if (authManager == null)
            {
                _statusMessage = "AuthManager not found.";
                return;
            }

            _isSubmitting = true;
            _statusMessage = "Validating token...";

            try
            {
                string refreshToken = string.IsNullOrWhiteSpace(_refreshToken) ? null : _refreshToken.Trim();
                bool success = await authManager.AuthenticateWithTokenAsync(accessToken, refreshToken);
                if (!success)
                    _statusMessage = "Authentication failed. Check token and try again.";
            }
            catch (Exception ex)
            {
                _statusMessage = $"Authentication error: {ex.Message}";
            }
            finally
            {
                _isSubmitting = false;
            }
        }

        /// <summary>
        /// 앱 종료(에디터에서는 Play 모드 종료).
        /// </summary>
        private static void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 테스트 로그인 UI의 IMGUI 스타일을 최초 1회 생성한다.
        /// </summary>
        private void EnsureStyles()
        {
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = TitleFontSize,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(18, 18, 18, 18)
                };
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = BodyFontSize
                };
            }

            if (_statusStyle == null)
            {
                _statusStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = StatusFontSize,
                    wordWrap = true
                };
                _statusStyle.normal.textColor = new Color(0.92f, 0.72f, 0.2f);
            }

            if (_textFieldStyle == null)
            {
                _textFieldStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = BodyFontSize,
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }

            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = BodyFontSize,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }
    }
}
