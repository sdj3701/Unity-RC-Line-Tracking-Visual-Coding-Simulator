// Assets/Scripts/Auth/ProtocolRegistrar.cs
using UnityEngine;

namespace Auth
{
    /// <summary>
    /// Windows에 URL Scheme 프로토콜을 등록합니다.
    /// 이렇게 하면 브라우저에서 rccar://... 링크를 클릭하면 Unity 앱이 실행됩니다.
    /// </summary>
    public static class ProtocolRegistrar
    {
        // ⭐ 웹 개발자와 협의해서 정할 프로토콜 이름
        // 예: "rccar" → rccar://auth?token=xxx 형태로 호출됨
        public const string PROTOCOL_NAME = "rccar";

        /// <summary>
        /// 프로토콜 등록 (앱 첫 실행 시 호출)
        /// </summary>
        public static void RegisterProtocol()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                // 현재 실행 중인 exe 파일의 경로
                string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

                using (var currentUser = GetCurrentUserRegistryKey())
                using (var key = CreateSubKey(currentUser, $@"Software\Classes\{PROTOCOL_NAME}"))
                {
                    SetRegistryValue(key, "", $"URL:{PROTOCOL_NAME} Protocol");
                    SetRegistryValue(key, "URL Protocol", "");

                    using (var commandKey = CreateSubKey(key, @"shell\open\command"))
                    {
                        // 이 앱을 URL과 함께 실행하도록 등록
                        SetRegistryValue(commandKey, "", $"\"{appPath}\" \"%1\"");
                    }
                }

                Debug.Log($"✅ 프로토콜 '{PROTOCOL_NAME}://' 등록 완료");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 프로토콜 등록 실패: {e.Message}");
            }
#else
            Debug.Log("프로토콜 등록은 Windows 빌드에서만 동작합니다.");
#endif
        }

        /// <summary>
        /// 등록 해제 (앱 삭제 시 호출)
        /// </summary>
        public static void UnregisterProtocol()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                using (var currentUser = GetCurrentUserRegistryKey())
                {
                    DeleteSubKeyTree(currentUser, $@"Software\Classes\{PROTOCOL_NAME}");
                }

                Debug.Log($"✅ 프로토콜 '{PROTOCOL_NAME}://' 등록 해제 완료");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ 프로토콜 등록 해제 실패: {e.Message}");
            }
#endif
        }

        private static System.IDisposable GetCurrentUserRegistryKey()
        {
            var registryType =
                System.Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry") ??
                System.Type.GetType("Microsoft.Win32.Registry, mscorlib");

            if (registryType == null)
            {
                throw new System.PlatformNotSupportedException("Registry API is not available in this runtime.");
            }

            var currentUserProperty = registryType.GetProperty(
                "CurrentUser",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (currentUserProperty == null)
            {
                throw new System.MissingMemberException("Microsoft.Win32.Registry.CurrentUser");
            }

            var currentUser = currentUserProperty.GetValue(null, null);
            if (currentUser is System.IDisposable disposable)
            {
                return disposable;
            }

            throw new System.InvalidOperationException("Could not access HKCU registry key.");
        }

        private static System.IDisposable CreateSubKey(object parentKey, string subKeyPath)
        {
            var createSubKeyMethod = parentKey.GetType().GetMethod("CreateSubKey", new[] { typeof(string) });
            if (createSubKeyMethod == null)
            {
                throw new System.MissingMethodException(parentKey.GetType().FullName, "CreateSubKey(string)");
            }

            var subKey = createSubKeyMethod.Invoke(parentKey, new object[] { subKeyPath });
            if (subKey is System.IDisposable disposable)
            {
                return disposable;
            }

            throw new System.InvalidOperationException($"Could not create registry key: {subKeyPath}");
        }

        private static void SetRegistryValue(object key, string name, string value)
        {
            var setValueMethod = key.GetType().GetMethod("SetValue", new[] { typeof(string), typeof(object) });
            if (setValueMethod == null)
            {
                throw new System.MissingMethodException(key.GetType().FullName, "SetValue(string, object)");
            }

            setValueMethod.Invoke(key, new object[] { name, value });
        }

        private static void DeleteSubKeyTree(object parentKey, string subKeyPath)
        {
            var deleteWithThrowMethod = parentKey.GetType().GetMethod("DeleteSubKeyTree", new[] { typeof(string), typeof(bool) });
            if (deleteWithThrowMethod != null)
            {
                deleteWithThrowMethod.Invoke(parentKey, new object[] { subKeyPath, false });
                return;
            }

            var deleteMethod = parentKey.GetType().GetMethod("DeleteSubKeyTree", new[] { typeof(string) });
            if (deleteMethod == null)
            {
                throw new System.MissingMethodException(parentKey.GetType().FullName, "DeleteSubKeyTree(string)");
            }

            deleteMethod.Invoke(parentKey, new object[] { subKeyPath });
        }
    }
}
