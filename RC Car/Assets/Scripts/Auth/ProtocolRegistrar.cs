// // Assets/Scripts/Auth/ProtocolRegistrar.cs
// using UnityEngine;

// #if UNITY_STANDALONE_WIN && !UNITY_EDITOR
// using Microsoft.Win32;
// #endif

// namespace Auth
// {
//     /// <summary>
//     /// Windows에 URL Scheme 프로토콜을 등록합니다.
//     /// 이렇게 하면 브라우저에서 rccar://... 링크를 클릭하면 Unity 앱이 실행됩니다.
//     /// </summary>
//     public static class ProtocolRegistrar
//     {
//         // ⭐ 웹 개발자와 협의해서 정할 프로토콜 이름
//         // 예: "rccar" → rccar://auth?token=xxx 형태로 호출됨
//         public const string PROTOCOL_NAME = "rccar";

//         /// <summary>
//         /// 프로토콜 등록 (앱 첫 실행 시 호출)
//         /// </summary>
//         public static void RegisterProtocol()
//         {
// #if UNITY_STANDALONE_WIN && !UNITY_EDITOR
//             try
//             {
//                 // 현재 실행 중인 exe 파일의 경로
//                 string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                
//                 // Windows 레지스트리에 등록
//                 using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{PROTOCOL_NAME}"))
//                 {
//                     key.SetValue("", $"URL:{PROTOCOL_NAME} Protocol");
//                     key.SetValue("URL Protocol", "");
                    
//                     using (var commandKey = key.CreateSubKey(@"shell\open\command"))
//                     {
//                         // 이 앱을 URL과 함께 실행하도록 등록
//                         commandKey.SetValue("", $"\"{appPath}\" \"%1\"");
//                     }
//                 }
                
//                 Debug.Log($"✅ 프로토콜 '{PROTOCOL_NAME}://' 등록 완료");
//             }
//             catch (System.Exception e)
//             {
//                 Debug.LogError($"❌ 프로토콜 등록 실패: {e.Message}");
//             }
// #else
//             Debug.Log("프로토콜 등록은 Windows 빌드에서만 동작합니다.");
// #endif
//         }

//         /// <summary>
//         /// 등록 해제 (앱 삭제 시 호출)
//         /// </summary>
//         public static void UnregisterProtocol()
//         {
// #if UNITY_STANDALONE_WIN && !UNITY_EDITOR
//             try
//             {
//                 Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{PROTOCOL_NAME}", false);
//                 Debug.Log($"✅ 프로토콜 '{PROTOCOL_NAME}://' 등록 해제 완료");
//             }
//             catch (System.Exception e)
//             {
//                 Debug.LogError($"❌ 프로토콜 등록 해제 실패: {e.Message}");
//             }
// #endif
//         }
//     }
// }
