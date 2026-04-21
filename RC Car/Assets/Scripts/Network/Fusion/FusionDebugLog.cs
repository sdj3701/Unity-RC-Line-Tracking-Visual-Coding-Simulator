using UnityEngine;

namespace RC.Network.Fusion
{
    public enum FusionDebugFlow
    {
        Auth,
        Connect,
        Lobby,
        Room,
        Scene,
        Runner
    }

    public static class FusionDebugLog
    {
        public static void Info(FusionDebugFlow flow, string message)
        {
            Debug.Log(BuildMessage(flow, message));
        }

        public static void Warning(FusionDebugFlow flow, string message)
        {
            Debug.LogWarning(BuildMessage(flow, message));
        }

        public static void Error(FusionDebugFlow flow, string message)
        {
            Debug.LogError(BuildMessage(flow, message));
        }

        private static string BuildMessage(FusionDebugFlow flow, string message)
        {
            string color = GetColor(flow);
            return $"<color={color}>[Fusion:{flow}]</color> {message}";
        }

        private static string GetColor(FusionDebugFlow flow)
        {
            switch (flow)
            {
                case FusionDebugFlow.Auth:
                    return "#4DA3FF";
                case FusionDebugFlow.Connect:
                    return "#00D1B2";
                case FusionDebugFlow.Lobby:
                    return "#F7B731";
                case FusionDebugFlow.Room:
                    return "#A66CFF";
                case FusionDebugFlow.Scene:
                    return "#2ECC71";
                case FusionDebugFlow.Runner:
                    return "#9AA0A6";
                default:
                    return "#FFFFFF";
            }
        }
    }
}
