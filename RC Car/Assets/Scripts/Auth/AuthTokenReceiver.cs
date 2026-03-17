using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Auth
{
    /// <summary>
    /// Optional deep-link token receiver. Disabled by default for ID/PW login flow.
    /// </summary>
    public class AuthTokenReceiver : MonoBehaviour
    {
        public static AuthTokenReceiver Instance { get; private set; }

        public static event Action<string, string> OnTokenReceived;

        [SerializeField] private bool _enableDeepLinkLogin = false;

        private string _receivedAccessToken;
        private string _receivedRefreshToken;

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

        private bool HasTokenInCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            return args.Any(arg => arg.StartsWith($"{ProtocolRegistrar.PROTOCOL_NAME}://", StringComparison.OrdinalIgnoreCase));
        }

        public void ProcessCommandLineArgs()
        {
            string[] args = Environment.GetCommandLineArgs();

            foreach (string arg in args)
            {
                if (!arg.StartsWith($"{ProtocolRegistrar.PROTOCOL_NAME}://", StringComparison.OrdinalIgnoreCase))
                    continue;

                ParseProtocolUrl(arg);
                break;
            }
        }

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
                AuthManager.Instance?.AuthenticateWithToken(_receivedAccessToken, _receivedRefreshToken);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthTokenReceiver] Failed to parse protocol URL: {e.Message}");
            }
        }

        public string GetAccessToken() => _receivedAccessToken;
        public string GetRefreshToken() => _receivedRefreshToken;
    }
}
