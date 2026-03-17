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

        private static bool ShouldSkipForPrimaryFlow()
        {
            if (FindObjectOfType<AuthManager>() != null)
                return true;

            return SceneManager.GetActiveScene().name.Equals(PrimaryLoginSceneName, StringComparison.Ordinal);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _sceneLoadedAt = Time.unscaledTime;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneLoadedAt = Time.unscaledTime;
            _isSubmitting = false;

            if (!scene.name.Equals(LoginSceneName, StringComparison.Ordinal))
                _statusMessage = string.Empty;
        }

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

        private static void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

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
