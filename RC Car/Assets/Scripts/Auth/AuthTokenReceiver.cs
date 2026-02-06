// // Assets/Scripts/Auth/AuthTokenReceiver.cs
// using System;
// using System.Linq;
// using UnityEngine;

// namespace Auth
// {
//     /// <summary>
//     /// URL Scheme으로 전달된 토큰을 수신하고 파싱합니다.
//     /// 
//     /// 웹에서 rccar://auth?token=xxx&refresh=yyy 형태로 호출하면
//     /// 이 스크립트가 토큰을 추출합니다.
//     /// </summary>
//     public class AuthTokenReceiver : MonoBehaviour
//     {
//         public static AuthTokenReceiver Instance { get; private set; }

//         // 토큰을 받았을 때 발생하는 이벤트
//         public static event Action<string, string> OnTokenReceived;

//         // 수신한 토큰 저장
//         private string _receivedAccessToken;
//         private string _receivedRefreshToken;

//         private void Awake()
//         {
//             // 싱글톤 패턴
//             if (Instance != null)
//             {
//                 // 이미 인스턴스가 있으면 토큰만 확인하고 파괴
//                 if (HasTokenInCommandLine())
//                 {
//                     Instance.ProcessCommandLineArgs();
//                 }
//                 Destroy(gameObject);
//                 return;
//             }

//             Instance = this;
//             DontDestroyOnLoad(gameObject);

//             // 첫 실행 시 프로토콜 등록
//             ProtocolRegistrar.RegisterProtocol();

//             // ⭐ 여기서 토큰을 받습니다!
//             ProcessCommandLineArgs();
//         }

//         /// <summary>
//         /// 명령줄 인수에서 토큰 URL이 있는지 확인
//         /// </summary>
//         private bool HasTokenInCommandLine()
//         {
//             string[] args = Environment.GetCommandLineArgs();
//             return args.Any(arg => arg.StartsWith($"{ProtocolRegistrar.PROTOCOL_NAME}://"));
//         }

//         /// <summary>
//         /// ⭐ 핵심: 명령줄 인수에서 토큰 추출
//         /// 
//         /// Windows에서 rccar://auth?token=xxx 링크를 클릭하면
//         /// Unity 앱이 이렇게 실행됩니다:
//         /// YourApp.exe "rccar://auth?token=xxx&refresh=yyy"
//         /// 
//         /// 이 URL이 명령줄 인수로 들어옵니다!
//         /// </summary>
//         public void ProcessCommandLineArgs()
//         {
//             string[] args = Environment.GetCommandLineArgs();

//             Debug.Log($"📨 받은 명령줄 인수: {string.Join(", ", args)}");

//             foreach (string arg in args)
//             {
//                 // rccar://로 시작하는 인수 찾기
//                 if (arg.StartsWith($"{ProtocolRegistrar.PROTOCOL_NAME}://"))
//                 {
//                     Debug.Log($"🔗 URL Scheme 발견: {arg}");
//                     ParseProtocolUrl(arg);
//                     break;
//                 }
//             }
//         }

//         /// <summary>
//         /// URL에서 토큰 추출
//         /// 예: rccar://auth?token=abc123&refresh=xyz789
//         /// </summary>
//         private void ParseProtocolUrl(string url)
//         {
//             try
//             {
//                 Uri uri = new Uri(url);

//                 // Query String 파싱 (?token=xxx&refresh=yyy 부분)
//                 string query = uri.Query.TrimStart('?');
                
//                 if (string.IsNullOrEmpty(query))
//                 {
//                     Debug.LogWarning("⚠️ URL에 쿼리 파라미터가 없습니다.");
//                     return;
//                 }

//                 // token=xxx&refresh=yyy 형태를 Dictionary로 변환
//                 var queryParams = query.Split('&')
//                     .Select(p => p.Split('='))
//                     .Where(p => p.Length == 2)
//                     .ToDictionary(
//                         p => Uri.UnescapeDataString(p[0]),
//                         p => Uri.UnescapeDataString(p[1])
//                     );

//                 // 토큰 추출
//                 if (queryParams.TryGetValue("token", out string token))
//                 {
//                     _receivedAccessToken = token;
//                     Debug.Log("✅ Access Token 수신 완료");
//                 }

//                 if (queryParams.TryGetValue("refresh", out string refresh))
//                 {
//                     _receivedRefreshToken = refresh;
//                     Debug.Log("✅ Refresh Token 수신 완료");
//                 }

//                 // 토큰이 있으면 이벤트 발생
//                 if (!string.IsNullOrEmpty(_receivedAccessToken))
//                 {
//                     OnTokenReceived?.Invoke(_receivedAccessToken, _receivedRefreshToken);
                    
//                     // AuthManager에게 인증 요청
//                     AuthManager.Instance?.AuthenticateWithToken(_receivedAccessToken, _receivedRefreshToken);
//                 }
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"❌ URL 파싱 실패: {e.Message}");
//             }
//         }

//         /// <summary>
//         /// 현재 받은 Access Token 반환
//         /// </summary>
//         public string GetAccessToken() => _receivedAccessToken;

//         /// <summary>
//         /// 현재 받은 Refresh Token 반환
//         /// </summary>
//         public string GetRefreshToken() => _receivedRefreshToken;
//     }
// }
