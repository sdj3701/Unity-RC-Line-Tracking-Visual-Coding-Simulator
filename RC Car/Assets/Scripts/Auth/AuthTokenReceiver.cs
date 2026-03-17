using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Auth
{
    /// <summary>
    /// Optional deep-link token receiver.
    /// Receives and stores tokens from command-line protocol URL.
    /// </summary>
    public class AuthTokenReceiver : MonoBehaviour
    {
        public static AuthTokenReceiver Instance { get; private set; }

        public static event Action<string, string> OnTokenReceived;

        [SerializeField] private bool _enableDeepLinkLogin = false;

        private string _receivedAccessToken;
        private string _receivedRefreshToken;
        private bool _hasProcessedCommandLineArgs;

        /// <summary>
        /// 딥링크 수신기 싱글톤을 준비하고 커맨드라인 인자를 파싱한다.
        /// </summary>
        private void Awake()
        {
            if (!_enableDeepLinkLogin)
                return;

            if (Instance != null)
            {
                if (HasTokenInCommandLine())
                    Instance.ProcessCommandLineArgs();

                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ProtocolRegistrar.RegisterProtocol();
            ProcessCommandLineArgs();
        }

        /// <summary>
        /// 실행 인자에 프로토콜 URL(`rccar://...`)이 포함됐는지 확인한다.
        /// </summary>
        /// <returns>토큰 인자가 존재하면 true</returns>
        private bool HasTokenInCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            return args.Any(arg => arg.StartsWith($"{ProtocolRegistrar.PROTOCOL_NAME}://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 커맨드라인에서 딥링크 URL을 찾아 한 번만 파싱한다.
        /// </summary>
        public void ProcessCommandLineArgs()
        {
            if (_hasProcessedCommandLineArgs)
                return;

            _hasProcessedCommandLineArgs = true;
            string[] args = Environment.GetCommandLineArgs();

            foreach (string arg in args)
            {
                if (!arg.StartsWith($"{ProtocolRegistrar.PROTOCOL_NAME}://", StringComparison.OrdinalIgnoreCase))
                    continue;

                ParseProtocolUrl(arg);
                break;
            }
        }

        /// <summary>
        /// 프로토콜 URL의 쿼리스트링에서 access/refresh 토큰을 추출한다.
        /// </summary>
        /// <param name="url">예: rccar://auth?token=...&amp;refresh=...</param>
        private void ParseProtocolUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string query = uri.Query.TrimStart('?');
                if (string.IsNullOrWhiteSpace(query))
                    return;

                Dictionary<string, string> queryParams = query.Split('&')
                    .Select(p => p.Split('='))
                    .Where(p => p.Length == 2)
                    .ToDictionary(
                        p => Uri.UnescapeDataString(p[0]),
                        p => Uri.UnescapeDataString(p[1]));

                if (queryParams.TryGetValue("token", out string accessToken))
                    _receivedAccessToken = accessToken;

                if (queryParams.TryGetValue("refresh", out string refreshToken))
                    _receivedRefreshToken = refreshToken;

                if (string.IsNullOrWhiteSpace(_receivedAccessToken))
                    return;

                OnTokenReceived?.Invoke(_receivedAccessToken, _receivedRefreshToken);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthTokenReceiver] Failed to parse protocol URL: {e.Message}");
            }
        }

        /// <summary>
        /// 마지막으로 수신된 access token을 반환한다.
        /// </summary>
        public string GetAccessToken() => _receivedAccessToken;

        /// <summary>
        /// 마지막으로 수신된 refresh token을 반환한다.
        /// </summary>
        public string GetRefreshToken() => _receivedRefreshToken;
    }
}
