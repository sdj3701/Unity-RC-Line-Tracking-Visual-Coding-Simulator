using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth;
using UnityEngine;
using UnityEngine.Networking;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// 원격 DB 저장소 구현체입니다.
    /// 전송/저장은 POST, 조회/불러오기는 GET을 사용합니다.
    /// </summary>
    public class DatabaseStorageProvider : ICodeStorageProvider
    {
        // URL 자리표시자입니다. 실제 서버 엔드포인트로 교체해서 사용하세요.
        // 저장 POST 바디 필드: userId, fileName, xmlLongText, json, isModified
        // 조회/목록/존재확인 GET 쿼리 필드: userId, fileName
        private const string DefaultSaveCodePostUrl = "https://YOUR_SERVER_HOST/api/code/save";
        private const string DefaultLoadXmlGetUrl = "https://YOUR_SERVER_HOST/api/code/load-xml";
        private const string DefaultLoadJsonGetUrl = "https://YOUR_SERVER_HOST/api/code/load-json";
        private const string DefaultListGetUrl = "https://YOUR_SERVER_HOST/api/code/list";
        private const string DefaultExistsGetUrl = "https://YOUR_SERVER_HOST/api/code/exists";
        private const string DefaultDeletePostUrl = "https://YOUR_SERVER_HOST/api/code/delete";

        private readonly string _saveCodePostUrl;
        private readonly string _loadXmlGetUrl;
        private readonly string _loadJsonGetUrl;
        private readonly string _listGetUrl;
        private readonly string _existsGetUrl;
        private readonly string _deletePostUrl;

        // API가 설정되지 않은 동안에도 로컬 저장/불러오기가 동작하도록 폴백을 유지합니다.
        private readonly ICodeStorageProvider _fallbackProvider;

        public DatabaseStorageProvider(ICodeStorageProvider fallbackProvider = null)
            : this(
                DefaultSaveCodePostUrl,
                DefaultLoadXmlGetUrl,
                DefaultLoadJsonGetUrl,
                DefaultListGetUrl,
                DefaultExistsGetUrl,
                DefaultDeletePostUrl,
                fallbackProvider)
        {
        }

        public DatabaseStorageProvider(
            string saveCodePostUrl,
            string loadXmlGetUrl,
            string loadJsonGetUrl,
            string listGetUrl,
            string existsGetUrl,
            string deletePostUrl,
            ICodeStorageProvider fallbackProvider = null)
        {
            _saveCodePostUrl = saveCodePostUrl;
            _loadXmlGetUrl = loadXmlGetUrl;
            _loadJsonGetUrl = loadJsonGetUrl;
            _listGetUrl = listGetUrl;
            _existsGetUrl = existsGetUrl;
            _deletePostUrl = deletePostUrl;
            _fallbackProvider = fallbackProvider;
        }

        /// <summary>
        /// POST로 DB에 저장합니다: userId + fileName + xmlLongText + json + isModified
        /// </summary>
        public async Task<bool> SaveCodeAsync(string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            if (!IsConfigured(_saveCodePostUrl))
            {
                Debug.LogWarning("[DatabaseStorageProvider] Save URL is not configured. Fallback provider is used.");
                return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                Debug.LogWarning("[DatabaseStorageProvider] Auth info is missing. Fallback provider is used.");
                return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
            }

            string payload = BuildSaveRequestJson(userId, fileName, xmlContent, jsonContent, isModified);

            using (var request = new UnityWebRequest(_saveCodePostUrl, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] body = Encoding.UTF8.GetBytes(payload);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "SaveCode");
                if (ok)
                {
                    if (_fallbackProvider != null)
                    {
                        await _fallbackProvider.SaveCodeAsync(fileName, xmlContent, jsonContent, isModified);
                    }
                    return true;
                }
            }

            return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
        }

        /// <summary>
        /// GET으로 DB에서 XML을 불러옵니다.
        /// </summary>
        public async Task<string> LoadXmlAsync(string fileName)
        {
            if (!IsConfigured(_loadXmlGetUrl))
            {
                return await LoadXmlWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                return await LoadXmlWithFallbackAsync(fileName);
            }

            string url = BuildGetUrl(_loadXmlGetUrl, userId, fileName);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "LoadXml");
                if (ok)
                {
                    string response = request.downloadHandler?.text ?? string.Empty;

                    if (TryExtractJsonStringValue(response, "xmlLongText", out string xmlValue))
                    {
                        return xmlValue;
                    }

                    // 일부 서버는 래핑된 JSON 대신 XML 본문을 그대로 반환할 수 있습니다.
                    string trimmed = response.TrimStart();
                    if (trimmed.StartsWith("<"))
                    {
                        return response;
                    }
                }
            }

            return await LoadXmlWithFallbackAsync(fileName);
        }

        /// <summary>
        /// GET으로 DB에서 JSON을 불러옵니다.
        /// </summary>
        public async Task<string> LoadJsonAsync(string fileName)
        {
            if (!IsConfigured(_loadJsonGetUrl))
            {
                return await LoadJsonWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                return await LoadJsonWithFallbackAsync(fileName);
            }

            string url = BuildGetUrl(_loadJsonGetUrl, userId, fileName);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "LoadJson");
                if (ok)
                {
                    string response = request.downloadHandler?.text ?? string.Empty;

                    if (TryExtractRawJsonField(response, "json", out string jsonObject))
                    {
                        return jsonObject;
                    }

                    if (TryExtractJsonStringValue(response, "json", out string jsonString))
                    {
                        return jsonString;
                    }

                    // 일부 서버는 JSON 내용을 직접 반환할 수 있습니다.
                    string trimmed = response.TrimStart();
                    if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                    {
                        return response;
                    }
                }
            }

            return await LoadJsonWithFallbackAsync(fileName);
        }

        /// <summary>
        /// GET으로 DB에서 저장 파일 목록을 불러옵니다.
        /// </summary>
        public async Task<List<string>> GetFileListAsync()
        {
            if (!IsConfigured(_listGetUrl))
            {
                return await GetFileListWithFallbackAsync();
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                return await GetFileListWithFallbackAsync();
            }

            string url = BuildGetUrl(_listGetUrl, userId, null);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "GetFileList");
                if (ok)
                {
                    string response = request.downloadHandler?.text ?? string.Empty;
                    List<string> files = ParseFileList(response);
                    if (files.Count > 0)
                    {
                        return files;
                    }
                }
            }

            return await GetFileListWithFallbackAsync();
        }

        /// <summary>
        /// GET으로 DB 내 파일 존재 여부를 확인합니다.
        /// </summary>
        public async Task<bool> FileExistsAsync(string fileName)
        {
            if (!IsConfigured(_existsGetUrl))
            {
                return await FileExistsWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                return await FileExistsWithFallbackAsync(fileName);
            }

            string url = BuildGetUrl(_existsGetUrl, userId, fileName);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "FileExists");
                if (ok)
                {
                    string response = request.downloadHandler?.text ?? string.Empty;
                    if (TryExtractJsonBoolValue(response, "exists", out bool exists))
                    {
                        return exists;
                    }

                    string trimmed = response.Trim().ToLowerInvariant();
                    if (trimmed == "true") return true;
                    if (trimmed == "false") return false;
                }
            }

            return await FileExistsWithFallbackAsync(fileName);
        }

        /// <summary>
        /// POST로 DB의 코드를 삭제합니다.
        /// </summary>
        public async Task<bool> DeleteCodeAsync(string fileName)
        {
            if (!IsConfigured(_deletePostUrl))
            {
                return await DeleteWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                return await DeleteWithFallbackAsync(fileName);
            }

            string payload = BuildDeleteRequestJson(userId, fileName);

            using (var request = new UnityWebRequest(_deletePostUrl, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] body = Encoding.UTF8.GetBytes(payload);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "DeleteCode");
                if (ok)
                {
                    if (_fallbackProvider != null)
                    {
                        await _fallbackProvider.DeleteCodeAsync(fileName);
                    }
                    return true;
                }
            }

            return await DeleteWithFallbackAsync(fileName);
        }

        private static async Task<bool> SendRequestAsync(UnityWebRequest request, string logTag)
        {
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

            bool success = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
            if (!success)
            {
                Debug.LogWarning($"[DatabaseStorageProvider] {logTag} failed. code={request.responseCode}, error={request.error}, body={request.downloadHandler?.text}");
            }

            return success;
        }

        private static string BuildSaveRequestJson(string userId, string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            string escapedUserId = EscapeJsonString(userId);
            string escapedFileName = EscapeJsonString(fileName);
            string escapedXml = EscapeJsonString(xmlContent ?? string.Empty);
            string normalizedJson = NormalizeRawJson(jsonContent);
            string modifiedText = isModified ? "true" : "false";

            return
                "{" +
                $"\"userId\":\"{escapedUserId}\"," +
                $"\"fileName\":\"{escapedFileName}\"," +
                $"\"xmlLongText\":\"{escapedXml}\"," +
                $"\"json\":{normalizedJson}," +
                $"\"isModified\":{modifiedText}" +
                "}";
        }

        private static string BuildDeleteRequestJson(string userId, string fileName)
        {
            string escapedUserId = EscapeJsonString(userId);
            string escapedFileName = EscapeJsonString(fileName);

            return
                "{" +
                $"\"userId\":\"{escapedUserId}\"," +
                $"\"fileName\":\"{escapedFileName}\"" +
                "}";
        }

        private static string BuildGetUrl(string baseUrl, string userId, string fileName)
        {
            string url = $"{baseUrl}?userId={UnityWebRequest.EscapeURL(userId)}";
            if (!string.IsNullOrEmpty(fileName))
            {
                url += $"&fileName={UnityWebRequest.EscapeURL(fileName)}";
            }
            return url;
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static string NormalizeRawJson(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return "{}";
            }

            string trimmed = jsonContent.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                return trimmed;
            }

            // 폴백: 객체/배열 형식이 아니면 JSON 문자열로 전송합니다.
            return $"\"{EscapeJsonString(trimmed)}\"";
        }

        private static bool TryExtractJsonStringValue(string json, string fieldName, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            Match match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
            if (!match.Success)
            {
                return false;
            }

            value = Regex.Unescape(match.Groups["value"].Value).Replace("\\/", "/");
            return true;
        }

        private static bool TryExtractJsonBoolValue(string json, string fieldName, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            Match match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            return bool.TryParse(match.Groups["value"].Value, out value);
        }

        private static bool TryExtractRawJsonField(string json, string fieldName, out string rawJson)
        {
            rawJson = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            int fieldIndex = json.IndexOf($"\"{fieldName}\"", StringComparison.Ordinal);
            if (fieldIndex < 0)
            {
                return false;
            }

            int colonIndex = json.IndexOf(':', fieldIndex);
            if (colonIndex < 0)
            {
                return false;
            }

            int start = colonIndex + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
            {
                start++;
            }

            if (start >= json.Length)
            {
                return false;
            }

            char opener = json[start];
            if (opener != '{' && opener != '[')
            {
                return false;
            }

            char closer = opener == '{' ? '}' : ']';
            int depth = 0;
            bool inString = false;

            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (inString)
                {
                    continue;
                }

                if (c == opener)
                {
                    depth++;
                }
                else if (c == closer)
                {
                    depth--;
                    if (depth == 0)
                    {
                        rawJson = json.Substring(start, i - start + 1);
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<string> ParseFileList(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new List<string>();
            }

            string trimmed = responseText.Trim();
            string arrayText = null;

            if (trimmed.StartsWith("["))
            {
                arrayText = trimmed;
            }
            else
            {
                Match arrayMatch = Regex.Match(trimmed, "\"files\"\\s*:\\s*(\u005b.*?\u005d)", RegexOptions.Singleline);
                if (arrayMatch.Success)
                {
                    arrayText = arrayMatch.Groups[1].Value;
                }
            }

            if (string.IsNullOrEmpty(arrayText))
            {
                return new List<string>();
            }

            var matches = Regex.Matches(arrayText, "\"(?<item>(?:\\\\.|[^\"])*)\"");
            List<string> result = new List<string>();
            foreach (Match match in matches)
            {
                result.Add(Regex.Unescape(match.Groups["item"].Value).Replace("\\/", "/"));
            }

            return result;
        }

        private static bool IsConfigured(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return !url.Contains("YOUR_SERVER_HOST", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetAuthInfo(out string userId, out string accessToken)
        {
            userId = AuthManager.Instance?.CurrentUser?.userId;
            accessToken = AuthManager.Instance?.GetAccessToken();

            return !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(accessToken);
        }

        private async Task<bool> SaveWithFallbackAsync(string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            if (_fallbackProvider == null)
            {
                return false;
            }

            return await _fallbackProvider.SaveCodeAsync(fileName, xmlContent, jsonContent, isModified);
        }

        private async Task<string> LoadXmlWithFallbackAsync(string fileName)
        {
            if (_fallbackProvider == null)
            {
                return null;
            }

            return await _fallbackProvider.LoadXmlAsync(fileName);
        }

        private async Task<string> LoadJsonWithFallbackAsync(string fileName)
        {
            if (_fallbackProvider == null)
            {
                return null;
            }

            return await _fallbackProvider.LoadJsonAsync(fileName);
        }

        private async Task<List<string>> GetFileListWithFallbackAsync()
        {
            if (_fallbackProvider == null)
            {
                return new List<string>();
            }

            return await _fallbackProvider.GetFileListAsync();
        }

        private async Task<bool> FileExistsWithFallbackAsync(string fileName)
        {
            if (_fallbackProvider == null)
            {
                return false;
            }

            return await _fallbackProvider.FileExistsAsync(fileName);
        }

        private async Task<bool> DeleteWithFallbackAsync(string fileName)
        {
            if (_fallbackProvider == null)
            {
                return false;
            }

            return await _fallbackProvider.DeleteCodeAsync(fileName);
        }
    }
}

