using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Auth
{
    /// <summary>
    /// Runtime login UI for ID/PW authentication on Login scene.
    /// </summary>
    public class AuthManualTokenFallbackUI : MonoBehaviour
    {
        private const string LoginSceneName = "00_Login";
        private const string TestLoginSceneName = "00_TestLogin";
        private const float PanelWidth = 980f;
        private const float PanelHeight = 420f;
        private const int TitleFontSize = 30;
        private const int BodyFontSize = 30;
        private const int StatusFontSize = 25;

        private string _userId = string.Empty;
        private string _password = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isSubmitting;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;

        /// <summary>
        /// 앱 로드 후 로그인 IMGUI 오브젝트를 자동 생성한다.
        /// 테스트 인증 플로우에서는 생성하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (ShouldSkipForTestFlow())
                return;

            if (FindObjectOfType<AuthManualTokenFallbackUI>() != null)
                return;

            var go = new GameObject(nameof(AuthManualTokenFallbackUI));
            DontDestroyOnLoad(go);
            go.AddComponent<AuthManualTokenFallbackUI>();
        }

        /// <summary>
        /// 현재 실행이 테스트 인증 플로우인지 판별한다.
        /// </summary>
        /// <returns>테스트 플로우면 true</returns>
        private static bool ShouldSkipForTestFlow()
        {
            if (FindObjectOfType<TestAuthManager>() != null)
                return true;

            return SceneManager.GetActiveScene().name.Equals(TestLoginSceneName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 씬 로드 이벤트를 구독한다.
        /// </summary>
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 씬 로드 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 씬 전환 시 UI 상태를 초기화한다.
        /// </summary>
        /// <param name="scene">로드 완료된 씬</param>
        /// <param name="mode">씬 로드 모드</param>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _isSubmitting = false;

            if (!scene.name.Equals(LoginSceneName, StringComparison.Ordinal))
                _statusMessage = string.Empty;
        }

        /// <summary>
        /// 로그인 입력 UI(ID/PW)와 버튼을 렌더링하고 클릭 이벤트를 처리한다.
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

            GUI.Box(panelRect, "Account Login", _boxStyle);

            float x = panelRect.x + 20f;
            float y = panelRect.y + 70f;
            float width = panelRect.width - 40f;

            GUI.Label(new Rect(x, y, width, 36f), "ID", _labelStyle);
            y += 42f;
            _userId = GUI.TextField(new Rect(x, y, width, 48f), _userId, _textFieldStyle);

            y += 62f;
            GUI.Label(new Rect(x, y, width, 36f), "Password", _labelStyle);
            y += 42f;
            _password = GUI.PasswordField(new Rect(x, y, width, 48f), _password, '*', _textFieldStyle);

            y += 58f;
            if (!string.IsNullOrEmpty(_statusMessage))
                GUI.Label(new Rect(x, y, width, 56f), _statusMessage, _statusStyle);

            float buttonY = panelRect.yMax - 68f;
            if (GUI.Button(new Rect(x, buttonY, 220f, 48f), _isSubmitting ? "Logging in..." : "Login", _buttonStyle))
            {
                if (!_isSubmitting)
                    SubmitLogin();
            }

            if (GUI.Button(new Rect(x + 234f, buttonY, 160f, 48f), "Quit", _buttonStyle))
                QuitApp();
        }

        /// <summary>
        /// 현재 프레임에서 로그인 UI를 표시해야 하는지 판단한다.
        /// </summary>
        /// <returns>표시 필요 시 true</returns>
        private bool ShouldShow()
        {
            if (!SceneManager.GetActiveScene().name.Equals(LoginSceneName, StringComparison.Ordinal))
                return false;

            var authManager = AuthManager.Instance;
            if (authManager == null)
            {
                _statusMessage = "AuthManager not found.";
                return true;
            }

            if (authManager.IsAuthenticated)
                return false;

            return true;
        }

        /// <summary>
        /// 입력된 ID/PW를 검증하고 비동기 로그인 요청을 수행한다.
        /// </summary>
        private async void SubmitLogin()
        {
            // 흐름 주석:
            // 1) UI 입력값 정규화(userId/password)
            // 2) AuthManager에 로그인 요청 위임
            // 3) 실패 메시지는 UI 상태 텍스트에 반영
            string userId = _userId.Trim();
            string password = _password;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                _statusMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.ValidationError);
                return;
            }

            var authManager = AuthManager.Instance;
            if (authManager == null)
            {
                _statusMessage = "AuthManager not found.";
                return;
            }

            if (authManager.IsAuthenticating)
            {
                _statusMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.AuthenticationBusy);
                return;
            }

            _isSubmitting = true;
            _statusMessage = "Logging in...";

            try
            {
                var result = await authManager.LoginWithCredentialsAsync(userId, password);
                if (!result.IsSuccess)
                    _statusMessage = result.ErrorMessage;
            }
            catch (Exception ex)
            {
                _statusMessage = AuthErrorMapper.ToUserMessage(AuthErrorMapper.NetworkError, ex.Message);
            }
            finally
            {
                _isSubmitting = false;
            }
        }

        /// <summary>
        /// 앱을 종료한다. 에디터에서는 Play 모드를 중단한다.
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
        /// IMGUI 스타일 객체를 최초 1회 생성하고 재사용한다.
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
