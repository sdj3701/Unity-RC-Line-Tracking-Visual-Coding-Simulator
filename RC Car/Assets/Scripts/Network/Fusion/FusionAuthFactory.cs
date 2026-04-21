using System;
using System.Collections.Generic;
using Auth;
using Auth.Models;
using Fusion.Photon.Realtime;
using UnityEngine;

namespace RC.Network.Fusion
{
    public enum FusionAuthMode
    {
        CustomAuth,
        UserIdOnly
    }

    /// <summary>
    /// Builds Photon Fusion custom-auth values from the authenticated API session.
    /// </summary>
    public static class FusionAuthFactory
    {
        public const string UserIdParameter = "userId";
        public const string TokenParameter = "token";
        public const string RoomIdParameter = "roomId";
        public const string SessionNameParameter = "sessionName";
        public const string AppVersionParameter = "appVersion";

        /// <summary>
        /// Creates values for StartGameArgs.AuthValues. Throws when the API login state is incomplete.
        /// </summary>
        public static AuthenticationValues CreateFromAuthManager(
            string sessionName = null,
            bool usePostData = false,
            FusionAuthMode authMode = FusionAuthMode.CustomAuth)
        {
            if (!TryCreateFromAuthManager(out AuthenticationValues authValues, out string errorMessage, sessionName, usePostData, authMode))
                throw new InvalidOperationException(errorMessage);

            return authValues;
        }

        /// <summary>
        /// Creates values for StartGameArgs.AuthValues without throwing.
        /// </summary>
        public static bool TryCreateFromAuthManager(
            out AuthenticationValues authValues,
            out string errorMessage,
            string sessionName = null,
            bool usePostData = false,
            FusionAuthMode authMode = FusionAuthMode.CustomAuth)
        {
            authValues = null;

            if (!TryCreatePayloadFromAuthManager(out FusionAuthPayload payload, out errorMessage, sessionName))
                return false;

            return TryCreate(payload, out authValues, out errorMessage, usePostData, authMode);
        }

        /// <summary>
        /// Reads the current AuthManager session and returns a validated custom-auth payload.
        /// </summary>
        public static bool TryCreatePayloadFromAuthManager(
            out FusionAuthPayload payload,
            out string errorMessage,
            string sessionName = null)
        {
            payload = null;
            errorMessage = null;

            AuthManager authManager = AuthManager.Instance;
            if (authManager == null)
            {
                errorMessage = "AuthManager.Instance is null. Login must complete before connecting to Photon Fusion.";
                return false;
            }

            if (!authManager.IsAuthenticated)
            {
                errorMessage = "Current API session is not authenticated. Photon Fusion connection is blocked.";
                return false;
            }

            UserInfo currentUser = authManager.CurrentUser;
            if (currentUser == null)
            {
                errorMessage = "Authenticated user info is missing. Photon Fusion connection is blocked.";
                return false;
            }

            string userId = Normalize(currentUser.userId);
            if (string.IsNullOrWhiteSpace(userId))
            {
                errorMessage = "Authenticated userId is empty. Photon Fusion connection is blocked.";
                return false;
            }

            string accessToken = Normalize(authManager.GetAccessToken());
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                errorMessage = "Access token is empty. Photon Fusion connection is blocked.";
                return false;
            }

            payload = new FusionAuthPayload(userId, accessToken)
            {
                RoomId = Normalize(sessionName),
                SessionName = Normalize(sessionName),
                AppVersion = Normalize(Application.version)
            };

            return true;
        }

        /// <summary>
        /// Creates Photon Realtime auth values from an already validated payload.
        /// </summary>
        public static AuthenticationValues Create(
            FusionAuthPayload payload,
            bool usePostData = false,
            FusionAuthMode authMode = FusionAuthMode.CustomAuth)
        {
            if (!TryCreate(payload, out AuthenticationValues authValues, out string errorMessage, usePostData, authMode))
                throw new ArgumentException(errorMessage, nameof(payload));

            return authValues;
        }

        /// <summary>
        /// Creates Photon Realtime auth values from an already validated payload without throwing.
        /// </summary>
        public static bool TryCreate(
            FusionAuthPayload payload,
            out AuthenticationValues authValues,
            out string errorMessage,
            bool usePostData = false,
            FusionAuthMode authMode = FusionAuthMode.CustomAuth)
        {
            authValues = null;

            if (!ValidatePayload(payload, out errorMessage))
                return false;

            authValues = new AuthenticationValues(payload.UserId);

            if (authMode == FusionAuthMode.UserIdOnly)
            {
                return true;
            }

            authValues.AuthType = CustomAuthenticationType.Custom;

            if (usePostData)
            {
                authValues.SetAuthPostData(payload.ToAuthPostData());
                return true;
            }

            authValues.AddAuthParameter(UserIdParameter, payload.UserId);
            authValues.AddAuthParameter(TokenParameter, payload.AccessToken);
            AddOptionalAuthParameter(authValues, RoomIdParameter, payload.RoomId);
            AddOptionalAuthParameter(authValues, SessionNameParameter, payload.SessionName);
            AddOptionalAuthParameter(authValues, AppVersionParameter, payload.AppVersion);
            return true;
        }

        private static bool ValidatePayload(FusionAuthPayload payload, out string errorMessage)
        {
            if (payload == null)
            {
                errorMessage = "Fusion auth payload is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload.UserId))
            {
                errorMessage = "Fusion auth payload userId is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                errorMessage = "Fusion auth payload access token is empty.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static void AddOptionalAuthParameter(AuthenticationValues authValues, string key, string value)
        {
            if (authValues == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return;

            authValues.AddAuthParameter(key, value);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>
    /// Minimal API-authenticated identity payload used by Photon custom authentication.
    /// </summary>
    public sealed class FusionAuthPayload
    {
        public FusionAuthPayload(string userId, string accessToken)
        {
            UserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
            AccessToken = string.IsNullOrWhiteSpace(accessToken) ? string.Empty : accessToken.Trim();
        }

        public string UserId { get; private set; }
        public string AccessToken { get; private set; }
        public string RoomId { get; set; }
        public string SessionName { get; set; }
        public string AppVersion { get; set; }

        public Dictionary<string, object> ToAuthPostData()
        {
            var data = new Dictionary<string, object>
            {
                { FusionAuthFactory.UserIdParameter, UserId },
                { FusionAuthFactory.TokenParameter, AccessToken }
            };

            AddOptional(data, FusionAuthFactory.RoomIdParameter, RoomId);
            AddOptional(data, FusionAuthFactory.SessionNameParameter, SessionName);
            AddOptional(data, FusionAuthFactory.AppVersionParameter, AppVersion);
            return data;
        }

        public override string ToString()
        {
            return $"userId={UserId}, session={SessionName}, appVersion={AppVersion}, token={BuildTokenPreview(AccessToken)}";
        }

        private static void AddOptional(Dictionary<string, object> data, string key, string value)
        {
            if (data == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return;

            data[key] = value.Trim();
        }

        private static string BuildTokenPreview(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "(empty)";

            string trimmed = token.Trim();
            if (trimmed.Length <= 8)
                return "***";

            return trimmed.Substring(0, 4) + "...***..." + trimmed.Substring(trimmed.Length - 4);
        }
    }
}
