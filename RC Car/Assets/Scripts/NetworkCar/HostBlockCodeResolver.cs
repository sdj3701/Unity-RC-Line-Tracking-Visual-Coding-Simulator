using System;
using System.Threading.Tasks;
using UnityEngine;

public sealed class HostBlockCodeResolver
{
    private readonly bool _debugLog;

    public HostBlockCodeResolver(bool debugLog)
    {
        _debugLog = debugLog;
    }

    public async Task<ResolvedCodePayload> ResolveBySavedSeqAsync(
        string userIdRaw,
        string shareIdRaw,
        int savedSeq,
        string accessToken)
    {
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();

        var result = new ResolvedCodePayload
        {
            UserId = userId,
            ShareId = shareId,
            SavedSeq = savedSeq,
            ResolvedAtUtc = DateTime.UtcNow,
            IsSuccess = false
        };

        if (string.IsNullOrWhiteSpace(userId))
        {
            result.Error = "userId is empty";
            return result;
        }

        if (savedSeq <= 0)
        {
            result.Error = "savedSeq must be >= 1";
            return result;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            result.Error = "accessToken is empty";
            return result;
        }

        ChatUserLevelDebugResult verify = await ChatUserLevelDebugApi.FetchBySeqAsync(savedSeq, accessToken);
        if (verify == null)
        {
            result.Error = "debug api result is null";
            return result;
        }

        result.Xml = string.IsNullOrWhiteSpace(verify.Xml) ? string.Empty : verify.Xml;
        result.Json = string.IsNullOrWhiteSpace(verify.Json) ? string.Empty : verify.Json;

        if (string.IsNullOrWhiteSpace(result.Json) && !string.IsNullOrWhiteSpace(result.Xml))
        {
            try
            {
                result.Json = BE2XmlToRuntimeJson.ExportToString(result.Xml) ?? string.Empty;
            }
            catch (Exception e)
            {
                result.Error = $"XML->JSON conversion failed. {e.Message}";
                return result;
            }
        }

        if (string.IsNullOrWhiteSpace(result.Json))
        {
            result.Error = FirstNonEmpty(
                verify.ErrorMessage,
                "resolved json is empty");
            return result;
        }

        result.IsSuccess = true;
        result.Error = string.Empty;

        if (_debugLog)
        {
            Debug.Log(
                $"<color=#00FF66>[HostBlockCodeResolver] Code resolved. user={userId}, shareId={shareId}, savedSeq={savedSeq}, xmlLen={result.Xml.Length}, jsonLen={result.Json.Length}, verifyCode={verify.ResponseCode}</color>");
        }

        return result;
    }

    public ResolvedCodePayload ResolveFromBlockShareDetail(
        string userIdRaw,
        ChatRoomBlockShareInfo detailInfo)
    {
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        string shareId = detailInfo != null && !string.IsNullOrWhiteSpace(detailInfo.BlockShareId)
            ? detailInfo.BlockShareId.Trim()
            : string.Empty;
        int savedSeq = detailInfo != null ? detailInfo.UserLevelSeq : 0;

        var result = new ResolvedCodePayload
        {
            UserId = userId,
            ShareId = shareId,
            SavedSeq = savedSeq,
            ResolvedAtUtc = DateTime.UtcNow,
            IsSuccess = false
        };

        if (detailInfo == null)
        {
            result.Error = "detailInfo is null";
            return result;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            result.Error = "userId is empty";
            return result;
        }

        if (string.IsNullOrWhiteSpace(shareId))
        {
            result.Error = "shareId is empty";
            return result;
        }

        if (savedSeq <= 0)
        {
            result.Error = "userLevelSeq must be >= 1";
            return result;
        }

        result.Xml = string.IsNullOrWhiteSpace(detailInfo.Xml) ? string.Empty : detailInfo.Xml.Trim();
        result.Json = string.IsNullOrWhiteSpace(detailInfo.Json) ? string.Empty : detailInfo.Json.Trim();

        if (string.IsNullOrWhiteSpace(result.Json) && !string.IsNullOrWhiteSpace(result.Xml))
        {
            try
            {
                result.Json = BE2XmlToRuntimeJson.ExportToString(result.Xml) ?? string.Empty;
            }
            catch (Exception e)
            {
                result.Error = $"XML->JSON conversion failed. {e.Message}";
                return result;
            }
        }

        if (string.IsNullOrWhiteSpace(result.Json))
        {
            result.Error = "detail json is empty";
            return result;
        }

        result.IsSuccess = true;
        result.Error = string.Empty;

        if (_debugLog)
        {
            Debug.Log(
                $"<color=#00FF66>[HostBlockCodeResolver] Detail code resolved. user={userId}, shareId={shareId}, savedSeq={savedSeq}, xmlLen={result.Xml.Length}, jsonLen={result.Json.Length}</color>");
        }

        return result;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return string.Empty;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        }

        return string.Empty;
    }
}

public sealed class ResolvedCodePayload
{
    public string UserId;
    public string ShareId;
    public int SavedSeq;
    public string Xml;
    public string Json;
    public bool IsSuccess;
    public string Error;
    public DateTime ResolvedAtUtc;
}

