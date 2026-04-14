using System;
using RC.App.Defines;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Auth
{
    /// <summary>
    /// Login scene에 배치된 UGUI/TMP 입력 UI를 AuthManager 로그인 흐름에 바인딩한다.
    /// </summary>
    public class AuthManualTokenFallbackUI : MonoBehaviour
    {
        private const string LoginSceneName = AppScenes.Login;
        private const string TestLoginSceneName = "00_TestLogin";

        private const string LoginPanelName = "Panel Login";
        private const string UserIdInputName = "InputField ID";
        private const string PasswordInputName = "InputField Passward";
        private const string ConfirmButtonName = "ButConfirm";
        private const string CancelButtonName = "ButCancel";
        private const string LegacyLoginButtonName = "Login Button";
        private const string LegacyQuitButtonName = "Exit Button ";
        private const string LoadingPanelName = "Panel";
        private const string StatusTextName = "Status Text";

        [Header("Resolved Scene UI")]
        [SerializeField] private GameObject _loginPanelRoot;
        [SerializeField] private TMP_InputField _userIdInput;
        [SerializeField] private TMP_InputField _passwordInput;
        [SerializeField] private Button _loginButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private GameObject _loadingOverlay;
        [SerializeField] private TMP_Text _statusText;

        private TMP_Text _loadingOverlayText;
        private string _statusMessage = string.Empty;
        private bool _isSubmitting;
        private bool _missingBindingLogged;

        /// <summary>
        /// 앱 로드 후 로그인 UI 바인더를 자동 생성한다.
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
        private static bool ShouldSkipForTestFlow()
        {
            if (FindObjectOfType<TestAuthManager>() != null)
                return true;

            return SceneManager.GetActiveScene().name.Equals(TestLoginSceneName, StringComparison.Ordinal);
        }

        private void Start()
        {
            TryBindSceneUi();
            RefreshUiState();
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
            UnbindSceneUiEvents();
        }

        /// <summary>
        /// 씬 전환 시 UI 참조를 다시 해석하고 상태를 초기화한다.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _isSubmitting = false;
            _missingBindingLogged = false;
            _statusMessage = scene.name.Equals(LoginSceneName, StringComparison.Ordinal)
                ? string.Empty
                : _statusMessage;

            UnbindSceneUiEvents();
            ClearSceneReferences();
            TryBindSceneUi();
            RefreshUiState();
        }

        /// <summary>
        /// 현재 씬의 로그인 UI를 찾아 이벤트를 바인딩한다.
        /// </summary>
        private void TryBindSceneUi()
        {
            if (!IsLoginSceneActive())
                return;

            _loginPanelRoot = ResolveSceneObject(_loginPanelRoot, LoginPanelName);
            _userIdInput = ResolveSceneComponent(_userIdInput, UserIdInputName);
            _passwordInput = ResolveSceneComponent(_passwordInput, PasswordInputName);
            _loginButton = ResolveSceneComponent(_loginButton, ConfirmButtonName, LegacyLoginButtonName);
            _quitButton = ResolveSceneComponent(_quitButton, CancelButtonName, LegacyQuitButtonName);
            _loadingOverlay = ResolveSceneObject(_loadingOverlay, LoadingPanelName);
            _statusText = ResolveStatusText();
            _loadingOverlayText = _loadingOverlay != null
                ? _loadingOverlay.GetComponentInChildren<TMP_Text>(true)
                : null;

            EnsurePasswordInputMask();
            BindSceneUiEvents();
            LogMissingBindingsOnce();
        }

        /// <summary>
        /// 버튼/입력 필드 이벤트를 현재 씬 인스턴스에 다시 바인딩한다.
        /// </summary>
        private void BindSceneUiEvents()
        {
            UnbindSceneUiEvents();

            if (_loginButton != null)
                _loginButton.onClick.AddListener(OnClickLogin);

            if (_quitButton != null)
                _quitButton.onClick.AddListener(OnClickQuit);

            if (_userIdInput != null)
            {
                _userIdInput.onValueChanged.AddListener(OnInputChanged);
                _userIdInput.onSubmit.AddListener(OnInputSubmitted);
            }

            if (_passwordInput != null)
            {
                _passwordInput.onValueChanged.AddListener(OnInputChanged);
                _passwordInput.onSubmit.AddListener(OnInputSubmitted);
            }
        }

        /// <summary>
        /// 바인딩한 현재 씬 UI 이벤트를 해제한다.
        /// </summary>
        private void UnbindSceneUiEvents()
        {
            if (_loginButton != null)
                _loginButton.onClick.RemoveListener(OnClickLogin);

            if (_quitButton != null)
                _quitButton.onClick.RemoveListener(OnClickQuit);

            if (_userIdInput != null)
            {
                _userIdInput.onValueChanged.RemoveListener(OnInputChanged);
                _userIdInput.onSubmit.RemoveListener(OnInputSubmitted);
            }

            if (_passwordInput != null)
            {
                _passwordInput.onValueChanged.RemoveListener(OnInputChanged);
                _passwordInput.onSubmit.RemoveListener(OnInputSubmitted);
            }
        }

        private void OnClickLogin()
        {
            if (_isSubmitting)
                return;

            SubmitLogin();
        }

        private void OnClickQuit()
        {
            QuitApp();
        }

        private void OnInputChanged(string _)
        {
            if (_isSubmitting)
                return;

            SetStatusMessage(string.Empty);
        }

        private void OnInputSubmitted(string _)
        {
            if (_isSubmitting)
                return;

            SubmitLogin();
        }

        /// <summary>
        /// 현재 입력값을 이용해 ID/PW 로그인을 요청한다.
        /// </summary>
        private async void SubmitLogin()
        {
            if (!IsLoginSceneActive())
                return;

            string userId = _userIdInput != null ? _userIdInput.text.Trim() : string.Empty;
            string password = _passwordInput != null ? _passwordInput.text : string.Empty;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                SetStatusMessage(AuthErrorMapper.ToUserMessage(AuthErrorMapper.ValidationError));
                return;
            }

            var authManager = AuthManager.Instance;
            if (authManager == null)
            {
                SetStatusMessage("AuthManager not found.");
                return;
            }

            if (authManager.IsAuthenticating)
            {
                SetStatusMessage(AuthErrorMapper.ToUserMessage(AuthErrorMapper.AuthenticationBusy));
                return;
            }

            _isSubmitting = true;
            SetStatusMessage("Logging in...");
            RefreshUiState();

            try
            {
                var result = await authManager.LoginWithCredentialsAsync(userId, password);
                if (!result.IsSuccess)
                    SetStatusMessage(result.ErrorMessage);
            }
            catch (Exception ex)
            {
                SetStatusMessage(AuthErrorMapper.ToUserMessage(AuthErrorMapper.NetworkError, ex.Message));
            }
            finally
            {
                _isSubmitting = false;
                RefreshUiState();
            }
        }

        /// <summary>
        /// 현재 인증/입력 상태를 기준으로 씬 UI의 표시 상태를 갱신한다.
        /// </summary>
        private void RefreshUiState()
        {
            bool shouldShow = IsLoginSceneActive();
            bool isAuthenticated = AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated;
            bool panelVisible = shouldShow && !isAuthenticated;
            bool busy = panelVisible && (_isSubmitting || (AuthManager.Instance != null && AuthManager.Instance.IsAuthenticating));

            if (_loginPanelRoot != null)
                _loginPanelRoot.SetActive(panelVisible);

            if (_userIdInput != null)
                _userIdInput.interactable = panelVisible && !busy;

            if (_passwordInput != null)
                _passwordInput.interactable = panelVisible && !busy;

            if (_loginButton != null)
                _loginButton.interactable = panelVisible && !busy;

            if (_quitButton != null)
                _quitButton.interactable = panelVisible && !busy;

            SetLoadingOverlayVisible(busy);
            ApplyStatusMessage();
        }

        /// <summary>
        /// 비밀번호 입력 필드를 마스킹 모드로 강제한다.
        /// 씬 설정값이 비어 있어도 런타임에서 안전하게 맞춘다.
        /// </summary>
        private void EnsurePasswordInputMask()
        {
            if (_passwordInput == null)
                return;

            if (_passwordInput.contentType != TMP_InputField.ContentType.Password)
            {
                _passwordInput.contentType = TMP_InputField.ContentType.Password;
                _passwordInput.ForceLabelUpdate();
            }
        }

        /// <summary>
        /// 상태 메시지를 내부 캐시와 UI 텍스트에 반영한다.
        /// </summary>
        private void SetStatusMessage(string message)
        {
            _statusMessage = string.IsNullOrWhiteSpace(message)
                ? string.Empty
                : message.Trim();

            ApplyStatusMessage();
        }

        private void ApplyStatusMessage()
        {
            if (_statusText == null)
                return;

            bool shouldShow = IsLoginSceneActive() &&
                              !string.IsNullOrWhiteSpace(_statusMessage) &&
                              (_loginPanelRoot == null || _loginPanelRoot.activeSelf);

            _statusText.text = shouldShow ? _statusMessage : string.Empty;
            _statusText.gameObject.SetActive(shouldShow);
        }

        private void SetLoadingOverlayVisible(bool visible)
        {
            if (_loadingOverlay == null)
                return;

            if (visible)
                _loadingOverlay.transform.SetAsLastSibling();

            _loadingOverlay.SetActive(visible);

            if (_loadingOverlayText != null)
                _loadingOverlayText.text = visible ? "Logging..." : string.Empty;
        }

        private static bool IsLoginSceneActive()
        {
            return SceneManager.GetActiveScene().name.Equals(LoginSceneName, StringComparison.Ordinal);
        }

        private TMP_Text ResolveStatusText()
        {
            if (_statusText != null)
                return _statusText;

            TMP_Text found = FindSceneComponent<TMP_Text>(StatusTextName);
            if (found != null)
                return found;

            if (_loginPanelRoot == null)
                return null;

            var statusObject = new GameObject(StatusTextName, typeof(RectTransform), typeof(TextMeshProUGUI));
            var statusRect = statusObject.GetComponent<RectTransform>();
            statusRect.SetParent(_loginPanelRoot.transform, false);
            statusRect.anchorMin = new Vector2(0.5f, 0.5f);
            statusRect.anchorMax = new Vector2(0.5f, 0.5f);
            statusRect.pivot = new Vector2(0.5f, 0.5f);
            statusRect.anchoredPosition = new Vector2(0f, -160f);
            statusRect.sizeDelta = new Vector2(800f, 72f);

            var statusText = statusObject.GetComponent<TextMeshProUGUI>();
            TMP_Text referenceText = null;
            if (_passwordInput != null && _passwordInput.textComponent != null)
                referenceText = _passwordInput.textComponent;
            else if (_userIdInput != null && _userIdInput.textComponent != null)
                referenceText = _userIdInput.textComponent;
            else if (_loginPanelRoot != null)
                referenceText = _loginPanelRoot.GetComponentInChildren<TMP_Text>(true);

            if (referenceText != null)
            {
                statusText.font = referenceText.font;
                statusText.fontSharedMaterial = referenceText.fontSharedMaterial;
            }

            statusText.fontSize = 28f;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.enableWordWrapping = true;
            statusText.raycastTarget = false;
            statusText.color = new Color(0.78f, 0.22f, 0.22f);
            statusObject.SetActive(false);

            return statusText;
        }

        private static T ResolveSceneComponent<T>(T current, params string[] objectNames) where T : Component
        {
            if (current != null)
                return current;

            for (int i = 0; i < objectNames.Length; i++)
            {
                T found = FindSceneComponent<T>(objectNames[i]);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static GameObject ResolveSceneObject(GameObject current, params string[] objectNames)
        {
            if (current != null)
                return current;

            for (int i = 0; i < objectNames.Length; i++)
            {
                GameObject found = FindSceneObject(objectNames[i]);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static T FindSceneComponent<T>(string objectName) where T : Component
        {
            GameObject obj = FindSceneObject(objectName);
            return obj != null ? obj.GetComponent<T>() : null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            GameObject inactiveMatch = null;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < transforms.Length; j++)
                {
                    Transform transform = transforms[j];
                    if (!transform.name.Equals(objectName, StringComparison.Ordinal))
                        continue;

                    if (transform.gameObject.activeInHierarchy)
                        return transform.gameObject;

                    inactiveMatch ??= transform.gameObject;
                }
            }

            return inactiveMatch;
        }

        private void LogMissingBindingsOnce()
        {
            if (_missingBindingLogged || !IsLoginSceneActive())
                return;

            if (_loginPanelRoot != null &&
                _userIdInput != null &&
                _passwordInput != null &&
                _loginButton != null &&
                _quitButton != null)
            {
                return;
            }

            _missingBindingLogged = true;

            Debug.LogWarning(
                "[AuthManualTokenFallbackUI] Login scene UI bindings are incomplete. " +
                $"panel={_loginPanelRoot != null}, userId={_userIdInput != null}, password={_passwordInput != null}, " +
                $"loginButton={_loginButton != null}, quitButton={_quitButton != null}");
        }

        private void ClearSceneReferences()
        {
            _loginPanelRoot = null;
            _userIdInput = null;
            _passwordInput = null;
            _loginButton = null;
            _quitButton = null;
            _loadingOverlay = null;
            _statusText = null;
            _loadingOverlayText = null;
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
    }
}
