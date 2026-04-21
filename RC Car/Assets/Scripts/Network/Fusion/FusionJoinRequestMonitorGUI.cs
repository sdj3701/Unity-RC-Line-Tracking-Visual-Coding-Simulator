using System;
using System.Collections.Generic;
using RC.App.Defines;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RC.Network.Fusion
{
    public sealed class FusionJoinRequestMonitorGUI : MonoBehaviour
    {
        [SerializeField] private bool _showGui = true;
        [SerializeField] private bool _hostOnly = true;
        [SerializeField] private Vector2 _windowPosition = new Vector2(24f, 24f);
        [SerializeField] private Vector2 _windowSize = new Vector2(760f, 420f);
        [SerializeField] private int _fontSize = 18;

        private Rect _windowRect;
        private Vector2 _scrollPosition;
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private int _cachedFontSize = -1;

        private void Awake()
        {
            _windowRect = new Rect(_windowPosition.x, _windowPosition.y, _windowSize.x, _windowSize.y);
        }

        private void OnGUI()
        {
            if (!_showGui)
                return;

            FusionConnectionManager manager = FusionConnectionManager.Instance;
            if (manager == null || !manager.IsInGameSession || manager.Runner == null)
                return;

            if (_hostOnly && !manager.Runner.IsServer)
                return;

            if (manager.PendingJoinRequests.Count == 0 && manager.JoinApprovalMode != FusionJoinApprovalMode.Manual)
                return;

            EnsureStyles();

            _windowRect = GUI.Window(
                GetInstanceID(),
                _windowRect,
                DrawWindow,
                "Photon Join Requests",
                _windowStyle);
        }

        private void DrawWindow(int windowId)
        {
            FusionConnectionManager manager = FusionConnectionManager.Instance;
            if (manager == null)
                return;

            float buttonHeight = Mathf.Max(36f, _fontSize + 14f);
            float rowHeight = Mathf.Max(32f, _fontSize + 10f);

            GUILayout.BeginVertical();

            string countLabel = BuildPlayerCountLabel(manager);
            GUILayout.Label($"Approval Mode: {manager.JoinApprovalMode}", _labelStyle, GUILayout.Height(rowHeight));
            GUILayout.Label(countLabel, _labelStyle, GUILayout.Height(rowHeight));

            GUILayout.Space(8f);
            GUILayout.Label($"Pending Requests: {manager.PendingJoinRequests.Count}", _labelStyle, GUILayout.Height(rowHeight));

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(Mathf.Max(120f, _windowRect.height - 150f)));
            IReadOnlyList<FusionPendingJoinRequestInfo> requests = manager.PendingJoinRequests;
            if (requests.Count == 0)
            {
                GUILayout.Label("(No pending Photon join requests)", _labelStyle, GUILayout.Height(rowHeight));
            }
            else
            {
                for (int i = 0; i < requests.Count; i++)
                    DrawRequestRow(manager, requests[i], buttonHeight, rowHeight);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 42f));
        }

        private void DrawRequestRow(
            FusionConnectionManager manager,
            FusionPendingJoinRequestInfo request,
            float buttonHeight,
            float rowHeight)
        {
            if (request == null)
                return;

            GUILayout.BeginVertical(GUI.skin.box);

            string displayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? "(no name)"
                : request.DisplayName;
            string userId = string.IsNullOrWhiteSpace(request.UserId)
                ? "(unknown user)"
                : request.UserId;

            GUILayout.Label($"user={userId}, name={displayName}", _labelStyle, GUILayout.Height(rowHeight));
            GUILayout.Label($"remote={request.RemoteAddress}, requested={request.RequestedAtUtc}", _labelStyle, GUILayout.Height(rowHeight));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Accept", _buttonStyle, GUILayout.Height(buttonHeight)))
                manager.ApproveJoinRequest(request.RequestId);

            if (GUILayout.Button("Reject", _buttonStyle, GUILayout.Height(buttonHeight)))
                manager.RejectJoinRequest(request.RequestId);

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6f);
        }

        private static string BuildPlayerCountLabel(FusionConnectionManager manager)
        {
            if (manager == null || manager.Runner == null || manager.Runner.IsShutdown || !manager.Runner.IsRunning)
                return "Player Count: unavailable";

            FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
            int fallbackPlayerCount = context != null ? context.PlayerCount : (manager.Runner.IsServer ? 1 : 0);
            int fallbackMaxPlayers = context != null ? context.MaxPlayers : 0;
            int playerCount = FusionPlayerCountUtility.ResolveCurrentPlayerCount(manager.Runner, fallbackPlayerCount);
            int maxPlayers = FusionPlayerCountUtility.ResolveMaxPlayers(manager.Runner, fallbackMaxPlayers);
            return $"Player Count: {playerCount}/{maxPlayers}";
        }

        private void EnsureStyles()
        {
            int fontSize = Mathf.Max(1, _fontSize);
            if (_windowStyle != null && _labelStyle != null && _buttonStyle != null && _cachedFontSize == fontSize)
                return;

            _cachedFontSize = fontSize;
            _windowStyle = new GUIStyle(GUI.skin.window) { fontSize = fontSize };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapForNetworkScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.name, AppScenes.NetworkCarTest, StringComparison.Ordinal))
                return;

            if (FindObjectOfType<HostJoinRequestMonitorUI>() != null)
                return;

            if (FindObjectOfType<FusionJoinRequestMonitorGUI>() != null)
                return;

            var go = new GameObject("FusionJoinRequestMonitorGUI");
            go.AddComponent<FusionJoinRequestMonitorGUI>();
        }
    }
}
