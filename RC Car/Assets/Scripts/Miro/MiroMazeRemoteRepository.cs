using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Remote API를 통해 미로 데이터를 저장/로드하는 저장소.
/// Unity 클라이언트는 DB에 직접 연결하지 않고 서버 API를 통해서만 통신한다.
/// </summary>
public class MiroMazeRemoteRepository : MonoBehaviour
{
    [Header("API Endpoint")]
    [Tooltip("원격 API 기본 주소. 예: https://api.example.com")]
    public string apiBaseUrl = "http://localhost:5000";
    [Tooltip("미로 저장 API 경로. 예: /api/miro/mazes")]
    public string saveEndpoint = "/api/miro/mazes";
    [Tooltip("최신 미로 로드 API 경로. 예: /api/miro/mazes/latest")]
    public string latestEndpoint = "/api/miro/mazes/latest";
    [Tooltip("연결 상태 점검 API 경로. 예: /health")]
    public string healthEndpoint = "/health";

    [Header("Request")]
    [Tooltip("인증 서버를 사용하는 경우 Bearer 토큰(JWT 등)을 설정한다.")]
    public string bearerToken = "";
    [Min(1)] public int timeoutSeconds = 10;

    [Header("Debug")]
    [SerializeField] bool logRemoteRequests = true;

    /// <summary>
    /// Remote 저장 요청 바디 포맷.
    /// </summary>
    [Serializable]
    class RemoteSaveRequest
    {
        public string clientSavedAtUtc = "";
        public MiroMazeData mazeData;
    }

    /// <summary>
    /// 서버가 미로 데이터를 래핑해서 응답할 때 사용하는 포맷.
    /// </summary>
    [Serializable]
    class RemoteMazeEnvelope
    {
        public bool success = false;
        public string message = "";
        public MiroMazeData mazeData = null;
    }

    /// <summary>
    /// 원격 API에 미로 데이터를 저장한다.
    /// </summary>
    public IEnumerator SaveMaze(MiroMazeData data, Action<bool, string> onCompleted)
    {
        if (!ValidateMazeData(data, out string validationMessage))
        {
            onCompleted?.Invoke(false, $"Save canceled: {validationMessage}");
            yield break;
        }

        string url = BuildUrl(saveEndpoint);
        RemoteSaveRequest payload = new RemoteSaveRequest
        {
            clientSavedAtUtc = DateTime.UtcNow.ToString("o"),
            mazeData = data
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            ApplyDefaultHeaders(request);

            yield return request.SendWebRequest();

            bool success = IsSuccessResponse(request);
            string message = BuildResponseMessage(request, "save");
            if (logRemoteRequests)
            {
                if (success)
                {
                    Debug.Log($"[MiroMazeRemoteRepository] Remote save succeeded. {message}");
                }
                else
                {
                    Debug.LogWarning($"[MiroMazeRemoteRepository] Remote save failed. {message}");
                }
            }

            onCompleted?.Invoke(success, message);
        }
    }

    /// <summary>
    /// 원격 API에서 최신 미로 데이터를 읽어온다.
    /// </summary>
    public IEnumerator TryLoadLatest(Action<bool, MiroMazeData, string> onCompleted)
    {
        string url = BuildUrl(latestEndpoint);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;
            ApplyDefaultHeaders(request);

            yield return request.SendWebRequest();

            if (!IsSuccessResponse(request))
            {
                string failMessage = BuildResponseMessage(request, "load");
                if (logRemoteRequests)
                {
                    Debug.LogWarning($"[MiroMazeRemoteRepository] Remote load failed. {failMessage}");
                }

                onCompleted?.Invoke(false, null, failMessage);
                yield break;
            }

            string responseJson = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (!TryDeserializeMaze(responseJson, out MiroMazeData loadedData))
            {
                string parseMessage = "Load failed: response JSON parsing failed.";
                if (logRemoteRequests)
                {
                    Debug.LogWarning($"[MiroMazeRemoteRepository] {parseMessage}");
                }

                onCompleted?.Invoke(false, null, parseMessage);
                yield break;
            }

            if (!ValidateMazeData(loadedData, out string validationMessage))
            {
                string invalidMessage = $"Load failed: {validationMessage}";
                if (logRemoteRequests)
                {
                    Debug.LogWarning($"[MiroMazeRemoteRepository] {invalidMessage}");
                }

                onCompleted?.Invoke(false, null, invalidMessage);
                yield break;
            }

            if (logRemoteRequests)
            {
                Debug.Log("[MiroMazeRemoteRepository] Remote load succeeded.");
            }

            onCompleted?.Invoke(true, loadedData, "Remote load succeeded.");
        }
    }

    /// <summary>
    /// health endpoint를 호출해 원격 API 연결 상태를 점검한다.
    /// </summary>
    public IEnumerator TestConnection(Action<bool, string> onCompleted)
    {
        string url = BuildUrl(healthEndpoint);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;
            ApplyDefaultHeaders(request);

            yield return request.SendWebRequest();

            bool success = IsSuccessResponse(request);
            string message = BuildResponseMessage(request, "connection");
            if (logRemoteRequests)
            {
                if (success)
                {
                    Debug.Log($"[MiroMazeRemoteRepository] Connection test succeeded. {message}");
                }
                else
                {
                    Debug.LogWarning($"[MiroMazeRemoteRepository] Connection test failed. {message}");
                }
            }

            onCompleted?.Invoke(success, message);
        }
    }

    /// <summary>
    /// API 공통 헤더(인증/Accept/Content-Type)를 요청에 적용한다.
    /// </summary>
    void ApplyDefaultHeaders(UnityWebRequest request)
    {
        if (request == null)
        {
            return;
        }

        request.SetRequestHeader("Accept", "application/json");
        if (request.uploadHandler != null)
        {
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
        }

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
        }
    }

    /// <summary>
    /// 응답 JSON을 MiroMazeData로 변환한다.
    /// 직접 포맷과 envelope 포맷 둘 다 대응한다.
    /// </summary>
    bool TryDeserializeMaze(string responseJson, out MiroMazeData data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return false;
        }

        try
        {
            MiroMazeData direct = JsonUtility.FromJson<MiroMazeData>(responseJson);
            if (LooksLikeMazeData(direct))
            {
                data = direct;
                return true;
            }

            RemoteMazeEnvelope envelope = JsonUtility.FromJson<RemoteMazeEnvelope>(responseJson);
            if (envelope != null && LooksLikeMazeData(envelope.mazeData))
            {
                data = envelope.mazeData;
                return true;
            }
        }
        catch (Exception ex)
        {
            if (logRemoteRequests)
            {
                Debug.LogWarning($"[MiroMazeRemoteRepository] JSON parsing exception: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 서버 응답 코드와 UnityWebRequest 결과를 기준으로 성공 여부를 판단한다.
    /// </summary>
    bool IsSuccessResponse(UnityWebRequest request)
    {
        if (request == null)
        {
            return false;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            return false;
        }

        long code = request.responseCode;
        return code >= 200 && code < 300;
    }

    /// <summary>
    /// 로그 및 상위 호출자에 전달할 응답 메시지를 생성한다.
    /// </summary>
    string BuildResponseMessage(UnityWebRequest request, string requestName)
    {
        if (request == null)
        {
            return $"{requestName} failed: request is null.";
        }

        long code = request.responseCode;
        string error = request.error ?? "";
        string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";

        if (IsSuccessResponse(request))
        {
            return $"{requestName} succeeded (HTTP {code}).";
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return $"{requestName} failed (HTTP {code}): {error}";
        }

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            return $"{requestName} failed (HTTP {code}): {TrimForLog(responseText, 200)}";
        }

        return $"{requestName} failed (HTTP {code}).";
    }

    /// <summary>
    /// base url과 endpoint를 결합해 최종 요청 URL을 만든다.
    /// </summary>
    string BuildUrl(string endpoint)
    {
        string baseUrl = (apiBaseUrl ?? "").Trim();
        string normalizedEndpoint = (endpoint ?? "").Trim();

        if (normalizedEndpoint.Length > 0 && !normalizedEndpoint.StartsWith("/"))
        {
            normalizedEndpoint = "/" + normalizedEndpoint;
        }

        return baseUrl.TrimEnd('/') + normalizedEndpoint;
    }

    /// <summary>
    /// 로드된 객체가 미로 데이터 형태인지 빠르게 확인한다.
    /// </summary>
    bool LooksLikeMazeData(MiroMazeData data)
    {
        return data != null && data.cells != null && data.cells.Length > 0;
    }

    /// <summary>
    /// 미로 데이터가 저장/로드 가능한 최소 조건을 만족하는지 확인한다.
    /// </summary>
    bool ValidateMazeData(MiroMazeData data, out string message)
    {
        if (data == null)
        {
            message = "maze data is null";
            return false;
        }

        if (data.mazeSize < 5)
        {
            message = "mazeSize must be >= 5";
            return false;
        }

        if (data.cells == null)
        {
            message = "cells array is null";
            return false;
        }

        int expectedCellCount = data.mazeSize * data.mazeSize;
        if (data.cells.Length != expectedCellCount)
        {
            message = $"cells length mismatch (expected {expectedCellCount}, actual {data.cells.Length})";
            return false;
        }

        if (data.cellStepX <= 0f || data.cellStepZ <= 0f)
        {
            message = "cellStepX and cellStepZ must be > 0";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// 긴 문자열을 로그용 길이로 자른다.
    /// </summary>
    string TrimForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength) + "...";
    }
}
