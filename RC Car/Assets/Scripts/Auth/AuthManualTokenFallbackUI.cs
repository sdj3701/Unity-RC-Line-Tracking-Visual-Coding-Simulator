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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<AuthManualTokenFallbackUI>() != null)
                return;

            var go = new GameObject(nameof(AuthManualTokenFallbackUI));
            DontDestroyOnLoad(go);
            go.AddComponent<AuthManualTokenFallbackUI>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
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

        private async void SubmitLogin()
        {
            string id = _userId.Trim();
            string password = _password;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
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
                var result = await authManager.LoginWithCredentialsAsync(id, password);
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
