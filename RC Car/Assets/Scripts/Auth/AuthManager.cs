// // Assets/Scripts/Auth/AuthManager.cs
// using System;
// using System.Threading.Tasks;
// using UnityEngine;
// using UnityEngine.Networking;
// using UnityEngine.SceneManagement;
// using Auth.Models;

// namespace Auth
// {
//     /// <summary>
//     /// 인증 전체를 관리하는 매니저.
//     /// 토큰 검증, 저장, 로그인/로그아웃 처리를 담당합니다.
//     /// </summary>
//     public class AuthManager : MonoBehaviour
//     {
//         public static AuthManager Instance { get; private set; }

//         [Header("서버 설정")]
//         [Tooltip("인증 서버 주소")]
//         [SerializeField] private string _serverBaseUrl = "http://ioteacher.com/api/users/me-by-token";
        
//         [Tooltip("토큰 검증 API 엔드포인트")]
//         [SerializeField] private string _validateEndpoint = "/api/auth/validate";

//         [Header("씬 설정")]
//         [Tooltip("인증 실패 시 이동할 씬")]
//         [SerializeField] private string _loginSceneName = "LoginScene";
        
//         [Tooltip("인증 성공 시 이동할 씬")]
//         [SerializeField] private string _gameSceneName = "GameScene";

//         [Header("디버그")]
//         [Tooltip("에디터에서 테스트용 토큰으로 자동 인증")]
//         [SerializeField] private bool _useTestTokenInEditor = true;
//         [SerializeField] private string _testToken = "test-token-12345";

//         // 인증 상태
//         public bool IsAuthenticated { get; private set; }
//         public UserInfo CurrentUser { get; private set; }

//         // 이벤트
//         public event Action OnLoginSuccess;
//         public event Action<string> OnLoginFailed;

//         // 저장된 토큰
//         private string _accessToken;
//         private string _refreshToken;

//         private void Awake()
//         {
//             // 싱글톤 패턴
//             if (Instance != null)
//             {
//                 Destroy(gameObject);
//                 return;
//             }
//             Instance = this;
//             DontDestroyOnLoad(gameObject);
//         }

//         private void Start()
//         {
// #if UNITY_EDITOR
//             // 에디터 테스트용
//             if (_useTestTokenInEditor)
//             {
//                 Debug.Log("🧪 에디터 테스트: 테스트 토큰으로 인증 시도");
//                 AuthenticateWithToken(_testToken);
//                 return;
//             }
// #endif
//             // 저장된 토큰으로 자동 로그인 시도
//             TryAutoLogin();
//         }

//         /// <summary>
//         /// 저장된 토큰이 있으면 자동 로그인 시도
//         /// </summary>
//         private async void TryAutoLogin()
//         {
//             string savedToken = PlayerPrefs.GetString("auth_access_token", "");
            
//             if (!string.IsNullOrEmpty(savedToken))
//             {
//                 Debug.Log("💾 저장된 토큰으로 자동 로그인 시도...");
//                 await AuthenticateWithTokenAsync(savedToken);
//             }
//         }

//         /// <summary>
//         /// URL Scheme으로 받은 토큰으로 인증 (동기 버전)
//         /// </summary>
//         public async void AuthenticateWithToken(string accessToken, string refreshToken = null)
//         {
//             await AuthenticateWithTokenAsync(accessToken, refreshToken);
//         }

//         /// <summary>
//         /// 토큰으로 인증 (비동기 버전)
//         /// </summary>
//         public async Task<bool> AuthenticateWithTokenAsync(string accessToken, string refreshToken = null)
//         {
//             Debug.Log("🔐 토큰 검증 시작...");

//             _accessToken = accessToken;
//             _refreshToken = refreshToken;

//             try
//             {
//                 // 서버에 토큰 검증 요청
//                 var result = await ValidateTokenWithServer(accessToken);

//                 if (result.IsSuccess)
//                 {
//                     IsAuthenticated = true;
//                     CurrentUser = result.User;

//                     // 토큰 로컬 저장 (다음 실행 시 자동 로그인용)
//                     SaveTokenLocally(accessToken, refreshToken);

//                     Debug.Log($"✅ 인증 성공! 유저: {CurrentUser?.username}");
//                     OnLoginSuccess?.Invoke();

//                     // 게임 씬으로 이동
//                     if (!string.IsNullOrEmpty(_gameSceneName))
//                     {
//                         SceneManager.LoadScene(_gameSceneName);
//                     }
//                     return true;
//                 }
//                 else
//                 {
//                     Debug.LogError($"❌ 인증 실패: {result.ErrorMessage}");
//                     OnLoginFailed?.Invoke(result.ErrorMessage);

//                     // 로그인 씬으로 이동
//                     if (!string.IsNullOrEmpty(_loginSceneName))
//                     {
//                         SceneManager.LoadScene(_loginSceneName);
//                     }
//                     return false;
//                 }
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"❌ 인증 중 오류: {e.Message}");
//                 OnLoginFailed?.Invoke(e.Message);
//                 return false;
//             }
//         }

//         /// <summary>
//         /// 서버에 토큰 검증 요청
//         /// </summary>
//         private async Task<AuthResult> ValidateTokenWithServer(string token)
//         {
//             string url = _serverBaseUrl + _validateEndpoint;

//             Debug.Log($"📡 서버 검증 요청: {url}");

//             using (var request = new UnityWebRequest(url, "POST"))
//             {
//                 // Authorization 헤더에 토큰 포함
//                 request.SetRequestHeader("Authorization", $"Bearer {token}");
//                 request.SetRequestHeader("Content-Type", "application/json");
//                 request.downloadHandler = new DownloadHandlerBuffer();

//                 // 비동기 요청
//                 var operation = request.SendWebRequest();

//                 while (!operation.isDone)
//                     await Task.Yield();

//                 if (request.result == UnityWebRequest.Result.Success)
//                 {
//                     Debug.Log($"📩 서버 응답: {request.downloadHandler.text}");

//                     var response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);
                    
//                     return new AuthResult
//                     {
//                         IsSuccess = response.success,
//                         User = response.user,
//                         ErrorMessage = response.error
//                     };
//                 }
//                 else
//                 {
//                     return new AuthResult
//                     {
//                         IsSuccess = false,
//                         ErrorMessage = $"서버 오류: {request.error}"
//                     };
//                 }
//             }
//         }

//         /// <summary>
//         /// 토큰 로컬 저장 (자동 로그인용)
//         /// </summary>
//         private void SaveTokenLocally(string accessToken, string refreshToken)
//         {
//             PlayerPrefs.SetString("auth_access_token", accessToken);
            
//             if (!string.IsNullOrEmpty(refreshToken))
//                 PlayerPrefs.SetString("auth_refresh_token", refreshToken);
            
//             PlayerPrefs.Save();
//             Debug.Log("💾 토큰 저장 완료");
//         }

//         /// <summary>
//         /// API 요청 시 사용할 Access Token 반환
//         /// </summary>
//         public string GetAccessToken() => _accessToken;

//         /// <summary>
//         /// 로그아웃
//         /// </summary>
//         public void Logout()
//         {
//             _accessToken = null;
//             _refreshToken = null;
//             IsAuthenticated = false;
//             CurrentUser = null;

//             // 저장된 토큰 삭제
//             PlayerPrefs.DeleteKey("auth_access_token");
//             PlayerPrefs.DeleteKey("auth_refresh_token");
//             PlayerPrefs.Save();

//             Debug.Log("👋 로그아웃 완료");

//             // 로그인 씬으로 이동
//             if (!string.IsNullOrEmpty(_loginSceneName))
//             {
//                 SceneManager.LoadScene(_loginSceneName);
//             }
//         }
//     }
// }
