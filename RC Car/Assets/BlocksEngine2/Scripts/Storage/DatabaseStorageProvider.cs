using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth;
using UnityEngine;
using UnityEngine.Networking;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// user-level REST API 연동 저장소 구현체입니다.
    /// 내부 인터페이스는 fileName 기반이므로 원격 저장 시 fileName을 level(int)로 해석합니다.
    /// </summary>
        public class DatabaseStorageProvider : ICodeStorageProvider
        {
        private const string DefaultApiBaseUrl = "http://ioteacher.com";
        private const string DefaultUserLevelPath = "/api/user-level";
        private const string DefaultUserLevelMePath = "/api/user-level/me";

        private readonly string _apiBaseUrl;
        private readonly string _userLevelPath;
            private readonly string _userLevelMePath;
            private readonly ICodeStorageProvider _fallbackProvider;

            private static void LogDbInfo(string message)
            {
                Debug.Log($"<color=cyan>{message}</color>");
            }

        public DatabaseStorageProvider(ICodeStorageProvider fallbackProvider = null)
            : this(DefaultApiBaseUrl, DefaultUserLevelPath, DefaultUserLevelMePath, fallbackProvider)
        {
        }

        public DatabaseStorageProvider(
            string apiBaseUrl,
            string userLevelPath,
            string userLevelMePath,
            ICodeStorageProvider fallbackProvider = null)
        {
            _apiBaseUrl = apiBaseUrl;
            _userLevelPath = NormalizePath(userLevelPath);
            _userLevelMePath = NormalizePath(userLevelMePath);
            _fallbackProvider = fallbackProvider;
        }

        /// <summary>
        /// POST /api/user-level 또는 PATCH/PUT /api/user-level/{seq}로 XML+JSON+level을 저장합니다.
        /// </summary>
        public async Task<bool> SaveCodeAsync(string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                Debug.LogWarning("[DatabaseStorageProvider] API base URL is not configured. Save aborted (local save fallback disabled).");
                return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                Debug.LogWarning("[DatabaseStorageProvider] Auth info is missing. Save aborted (local save fallback disabled).");
                return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
            }

            if (!TryParseLevel(fileName, out int level))
            {
                Debug.LogWarning($"[DatabaseStorageProvider] fileName '{fileName}' is not a numeric level. Save aborted (local save fallback disabled).");
                return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
            }

            UserLevelEntry existing = await FindEntryByLevelAsync(level, accessToken);
            bool saved = await SaveOrUpdateRemoteAsync(existing, level, xmlContent, jsonContent, accessToken);
            if (saved)
            {
                return true;
            }

            return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
        }

        /// <summary>
        /// GET /api/user-level/me 또는 GET /api/user-level/{seq}를 통해 XML을 불러옵니다.
        /// </summary>
        public async Task<string> LoadXmlAsync(string fileName)
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                return await LoadXmlWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                return await LoadXmlWithFallbackAsync(fileName);
            }

            if (!TryParseLevel(fileName, out int level))
            {
                return await LoadXmlWithFallbackAsync(fileName);
            }

            UserLevelEntry entry = await FindEntryByLevelAsync(level, accessToken);
            if (entry == null)
            {
                return await LoadXmlWithFallbackAsync(fileName);
            }

            if (!string.IsNullOrEmpty(entry.Xml))
            {
                LogDbInfo($"[DatabaseStorageProvider] LoadXmlAsync success from DB. level={level}, len={entry.Xml.Length}");
                return entry.Xml;
            }

            if (entry.HasSeq)
            {
                UserLevelEntry detail = await GetEntryBySeqAsync(entry.Seq, accessToken);
                if (detail != null && !string.IsNullOrEmpty(detail.Xml))
                {
                    LogDbInfo($"[DatabaseStorageProvider] LoadXmlAsync success from DB detail. level={level}, seq={entry.Seq}, len={detail.Xml.Length}");
                    return detail.Xml;
                }
            }

            return await LoadXmlWithFallbackAsync(fileName);
        }

        /// <summary>
        /// GET /api/user-level/me 또는 GET /api/user-level/{seq}를 통해 JSON을 불러옵니다.
        /// </summary>
        public async Task<string> LoadJsonAsync(string fileName)
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                return await LoadJsonWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                return await LoadJsonWithFallbackAsync(fileName);
            }

            if (!TryParseLevel(fileName, out int level))
            {
                return await LoadJsonWithFallbackAsync(fileName);
            }

            UserLevelEntry entry = await FindEntryByLevelAsync(level, accessToken);
            if (entry == null)
            {
                return await LoadJsonWithFallbackAsync(fileName);
            }

            if (!string.IsNullOrEmpty(entry.Json))
            {
                LogDbInfo($"[DatabaseStorageProvider] LoadJsonAsync success from DB. level={level}, len={entry.Json.Length}");
                return entry.Json;
            }

            if (entry.HasSeq)
            {
                UserLevelEntry detail = await GetEntryBySeqAsync(entry.Seq, accessToken);
                if (detail != null && !string.IsNullOrEmpty(detail.Json))
                {
                    LogDbInfo($"[DatabaseStorageProvider] LoadJsonAsync success from DB detail. level={level}, seq={entry.Seq}, len={detail.Json.Length}");
                    return detail.Json;
                }
            }

            return await LoadJsonWithFallbackAsync(fileName);
        }

        /// <summary>
        /// GET /api/user-level/me 결과에서 level 목록을 반환합니다.
        /// </summary>
        public async Task<List<string>> GetFileListAsync()
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                return await GetFileListWithFallbackAsync();
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                return await GetFileListWithFallbackAsync();
            }

            List<UserLevelEntry> entries = await GetMyEntriesAsync(accessToken);
            if (entries.Count == 0)
            {
                return await GetFileListWithFallbackAsync();
            }

            var levelSet = new HashSet<int>();
            foreach (UserLevelEntry entry in entries)
            {
                if (entry.HasLevel)
                {
                    levelSet.Add(entry.Level);
                }
            }

            if (levelSet.Count == 0)
            {
                return await GetFileListWithFallbackAsync();
            }

            List<int> levels = new List<int>(levelSet);
            levels.Sort();

            List<string> fileNames = new List<string>(levels.Count);
            foreach (int level in levels)
            {
                fileNames.Add(level.ToString());
            }

            LogDbInfo($"[DatabaseStorageProvider] GetFileListAsync success from DB. count={fileNames.Count}");
            return fileNames;
        }

        /// <summary>
        /// level 단위 데이터 존재 여부를 확인합니다.
        /// </summary>
        public async Task<bool> FileExistsAsync(string fileName)
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                return await FileExistsWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                return await FileExistsWithFallbackAsync(fileName);
            }

            if (!TryParseLevel(fileName, out int level))
            {
                return await FileExistsWithFallbackAsync(fileName);
            }

            UserLevelEntry entry = await FindEntryByLevelAsync(level, accessToken);
            if (entry != null)
            {
                return true;
            }

            return await FileExistsWithFallbackAsync(fileName);
        }

        /// <summary>
        /// DELETE /api/user-level/{seq}로 level 데이터를 삭제합니다.
        /// </summary>
        public async Task<bool> DeleteCodeAsync(string fileName)
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                return await DeleteWithFallbackAsync(fileName);
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                return await DeleteWithFallbackAsync(fileName);
            }

            if (!TryParseLevel(fileName, out int level))
            {
                return await DeleteWithFallbackAsync(fileName);
            }

            UserLevelEntry existing = await FindEntryByLevelAsync(level, accessToken);
            if (existing == null || !existing.HasSeq)
            {
                return await DeleteWithFallbackAsync(fileName);
            }

            string url = BuildUserLevelDetailUrl(existing.Seq);
            using (var request = UnityWebRequest.Delete(url))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool deleted = await SendRequestAsync(request, "DeleteCode");
                if (deleted)
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

        /// <summary>
        /// GET /api/user-level/me 호출 결과를 상세 로그로 출력합니다.
        /// </summary>
        public async Task DebugLogGetMyEntriesAsync()
        {
            if (!IsConfigured(_apiBaseUrl))
            {
                Debug.LogWarning("[DatabaseStorageProvider][DebugGetMe] API base URL is not configured.");
                return;
            }

            if (!TryGetAuthInfo(out string userId, out string accessToken))
            {
                Debug.LogWarning("[DatabaseStorageProvider][DebugGetMe] Auth info is missing.");
                return;
            }

            string url = BuildUserLevelMeUrl();
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                LogDbInfo($"[DatabaseStorageProvider][DebugGetMe] Request: GET {url}, userId={userId}");

                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                string body = request.downloadHandler?.text ?? string.Empty;
                bool success = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;

                LogDbInfo($"[DatabaseStorageProvider][DebugGetMe] Response: status={request.responseCode}, success={success}, body={body}");

                List<UserLevelEntry> entries = ParseUserLevelEntries(body);
                LogDbInfo($"[DatabaseStorageProvider][DebugGetMe] Parsed entries count={entries.Count}");

                for (int i = 0; i < entries.Count; i++)
                {
                    UserLevelEntry entry = entries[i];
                    int xmlLen = entry.Xml == null ? 0 : entry.Xml.Length;
                    int jsonLen = entry.Json == null ? 0 : entry.Json.Length;
                    LogDbInfo(
                        $"[DatabaseStorageProvider][DebugGetMe] item[{i}] seq={(entry.HasSeq ? entry.Seq.ToString() : "n/a")}, " +
                        $"level={(entry.HasLevel ? entry.Level.ToString() : "n/a")}, xmlLen={xmlLen}, jsonLen={jsonLen}");
                }
            }
        }

        private async Task<bool> SaveOrUpdateRemoteAsync(UserLevelEntry existing, int level, string xmlContent, string jsonContent, string accessToken)
        {
            var payload = new SaveCodeRequest
            {
                level = level,
                xml = xmlContent ?? string.Empty,
                json = NormalizeRawJson(jsonContent),
                xmlLongText = xmlContent ?? string.Empty,
                jsonLongText = NormalizeRawJson(jsonContent)
            };

            if (existing != null && existing.HasSeq)
            {
                string updateUrl = BuildUserLevelDetailUrl(existing.Seq);

                bool patched = await SendMultipartAsync(updateUrl, "PATCH", payload, accessToken, "UpdateCode(PATCH)");
                if (patched)
                {
                    return true;
                }

                bool put = await SendMultipartAsync(updateUrl, "PUT", payload, accessToken, "UpdateCode(PUT)");
                return put;
            }

            string createUrl = BuildUserLevelCollectionUrl();
            return await SendMultipartAsync(createUrl, UnityWebRequest.kHttpVerbPOST, payload, accessToken, "SaveCode(POST)");
        }

        private async Task<bool> SendMultipartAsync(string url, string method, SaveCodeRequest payload, string accessToken, string logTag)
        {
            List<IMultipartFormSection> sections = new List<IMultipartFormSection>();
            foreach (KeyValuePair<string, string> field in BuildSaveFields(payload))
            {
                sections.Add(new MultipartFormDataSection(field.Key, field.Value ?? string.Empty));
            }

            using (var request = UnityWebRequest.Post(url, sections))
            {
                request.method = method;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Accept", "application/json");
                return await SendRequestAsync(request, logTag);
            }
        }

        private static List<KeyValuePair<string, string>> BuildSaveFields(SaveCodeRequest payload)
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("level", payload.level.ToString()),
                new KeyValuePair<string, string>("xml", payload.xml ?? string.Empty),
                new KeyValuePair<string, string>("json", payload.json ?? string.Empty),
                new KeyValuePair<string, string>("xmlLongText", payload.xmlLongText ?? string.Empty),
                new KeyValuePair<string, string>("jsonLongText", payload.jsonLongText ?? string.Empty)
            };
        }

        private async Task<List<UserLevelEntry>> GetMyEntriesAsync(string accessToken)
        {
            string url = BuildUserLevelMeUrl();
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "GetMyEntries");
                if (!ok)
                {
                    return new List<UserLevelEntry>();
                }

                string response = request.downloadHandler?.text ?? string.Empty;
                return ParseUserLevelEntries(response);
            }
        }

        private async Task<UserLevelEntry> GetEntryBySeqAsync(long seq, string accessToken)
        {
            string url = BuildUserLevelDetailUrl(seq);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                bool ok = await SendRequestAsync(request, "GetEntryBySeq");
                if (!ok)
                {
                    return null;
                }

                string response = request.downloadHandler?.text ?? string.Empty;
                List<UserLevelEntry> entries = ParseUserLevelEntries(response);
                if (entries.Count > 0)
                {
                    return entries[0];
                }

                return null;
            }
        }

        private async Task<UserLevelEntry> FindEntryByLevelAsync(int level, string accessToken)
        {
            List<UserLevelEntry> entries = await GetMyEntriesAsync(accessToken);
            foreach (UserLevelEntry entry in entries)
            {
                if (entry.HasLevel && entry.Level == level)
                {
                    return entry;
                }
            }

            return null;
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

        private string BuildUserLevelCollectionUrl()
        {
            return CombineUrl(_apiBaseUrl, _userLevelPath);
        }

        private string BuildUserLevelMeUrl()
        {
            return CombineUrl(_apiBaseUrl, _userLevelMePath);
        }

        private string BuildUserLevelDetailUrl(long seq)
        {
            return $"{BuildUserLevelCollectionUrl().TrimEnd('/')}/{seq}";
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            string safeBase = (baseUrl ?? string.Empty).TrimEnd('/');
            string safePath = NormalizePath(path);
            return $"{safeBase}{safePath}";
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim();
            if (!trimmed.StartsWith("/"))
            {
                trimmed = "/" + trimmed;
            }

            return trimmed.TrimEnd('/');
        }

        private static bool IsConfigured(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                return false;
            }

            return !apiBaseUrl.Contains("YOUR_SERVER_HOST", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetAuthInfo(out string userId, out string accessToken)
        {
            userId = AuthManager.Instance?.CurrentUser?.userId;
            accessToken = AuthManager.Instance?.GetAccessToken();

            return !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(accessToken);
        }

        private static bool TryParseLevel(string fileName, out int level)
        {
            level = 0;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            return int.TryParse(fileName.Trim(), out level);
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

            return $"\"{EscapeJsonString(trimmed)}\"";
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

        private static List<UserLevelEntry> ParseUserLevelEntries(string responseText)
        {
            var result = new List<UserLevelEntry>();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return result;
            }

            string trimmed = responseText.Trim();
            if (trimmed.StartsWith("["))
            {
                ParseArrayEntries(trimmed, result);
                return result;
            }

            string[] arrayKeys = { "data", "items", "content", "list", "result", "rows" };
            foreach (string key in arrayKeys)
            {
                if (TryExtractRawJsonField(trimmed, key, out string rawArray) && rawArray.TrimStart().StartsWith("["))
                {
                    ParseArrayEntries(rawArray, result);
                    if (result.Count > 0)
                    {
                        return result;
                    }
                }
            }

            string[] objectKeys = { "data", "item", "result" };
            foreach (string key in objectKeys)
            {
                if (TryExtractRawJsonField(trimmed, key, out string rawObject) && rawObject.TrimStart().StartsWith("{"))
                {
                    if (TryParseEntry(rawObject, out UserLevelEntry nested))
                    {
                        result.Add(nested);
                        return result;
                    }
                }
            }

            if (TryParseEntry(trimmed, out UserLevelEntry single))
            {
                result.Add(single);
            }

            return result;
        }

        private static void ParseArrayEntries(string arrayJson, List<UserLevelEntry> destination)
        {
            List<string> objectJsonList = SplitTopLevelObjects(arrayJson);
            foreach (string objectJson in objectJsonList)
            {
                if (TryParseEntry(objectJson, out UserLevelEntry entry))
                {
                    destination.Add(entry);
                }
            }
        }

        private static List<string> SplitTopLevelObjects(string jsonArray)
        {
            var objects = new List<string>();
            if (string.IsNullOrWhiteSpace(jsonArray))
            {
                return objects;
            }

            int arrayStart = jsonArray.IndexOf('[');
            if (arrayStart < 0)
            {
                return objects;
            }

            bool inString = false;
            int depth = 0;
            int objectStart = -1;

            for (int i = arrayStart + 1; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];
                if (c == '"' && !IsEscaped(jsonArray, i))
                {
                    inString = !inString;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        objectStart = i;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objectStart >= 0)
                    {
                        objects.Add(jsonArray.Substring(objectStart, i - objectStart + 1));
                        objectStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }

            return objects;
        }

        private static bool IsEscaped(string text, int index)
        {
            int slashCount = 0;
            for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            {
                slashCount++;
            }

            return slashCount % 2 == 1;
        }

        private static bool TryParseEntry(string json, out UserLevelEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            bool hasSeq = TryExtractJsonLongValue(json, "seq", out long seq) || TryExtractJsonLongValue(json, "id", out seq);
            bool hasLevel = TryExtractJsonIntValue(json, "level", out int level);

            string xml = null;
            bool hasXml = TryExtractJsonStringValue(json, "xml", out xml) || TryExtractJsonStringValue(json, "xmlLongText", out xml);

            string jsonContent = null;
            bool hasJson = TryExtractRawJsonField(json, "json", out jsonContent) || TryExtractJsonStringValue(json, "json", out jsonContent);

            if (!hasSeq && !hasLevel && !hasXml && !hasJson)
            {
                return false;
            }

            entry = new UserLevelEntry
            {
                Seq = seq,
                HasSeq = hasSeq,
                Level = level,
                HasLevel = hasLevel,
                Xml = xml,
                Json = jsonContent
            };

            return true;
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

        private static bool TryExtractJsonIntValue(string json, string fieldName, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            Match match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"?(?<value>-?\\d+)\"?");
            if (!match.Success)
            {
                return false;
            }

            return int.TryParse(match.Groups["value"].Value, out value);
        }

        private static bool TryExtractJsonLongValue(string json, string fieldName, out long value)
        {
            value = 0L;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            Match match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"?(?<value>-?\\d+)\"?");
            if (!match.Success)
            {
                return false;
            }

            return long.TryParse(match.Groups["value"].Value, out value);
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

                if (c == '"' && !IsEscaped(json, i))
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

        private async Task<bool> SaveWithFallbackAsync(string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            // Intentionally disabled: remote save must not write to local storage.
            await Task.CompletedTask;
            return false;
        }

        private async Task<string> LoadXmlWithFallbackAsync(string fileName)
        {
            // Intentionally disabled: remote load must not read from local storage.
            await Task.CompletedTask;
            return null;
        }

        private async Task<string> LoadJsonWithFallbackAsync(string fileName)
        {
            // Intentionally disabled: remote load must not read from local storage.
            await Task.CompletedTask;
            return null;
        }

        private async Task<List<string>> GetFileListWithFallbackAsync()
        {
            // Intentionally disabled: remote file-list load must not read from local storage.
            await Task.CompletedTask;
            return new List<string>();
        }

        private async Task<bool> FileExistsWithFallbackAsync(string fileName)
        {
            // Intentionally disabled: remote existence check must not read from local storage.
            await Task.CompletedTask;
            return false;
        }

        private async Task<bool> DeleteWithFallbackAsync(string fileName)
        {
            if (_fallbackProvider == null)
            {
                return false;
            }

            return await _fallbackProvider.DeleteCodeAsync(fileName);
        }

        [Serializable]
        private sealed class SaveCodeRequest
        {
            public int level;
            public string xml;
            public string json;
            public string xmlLongText;
            public string jsonLongText;
        }

        private sealed class UserLevelEntry
        {
            public long Seq;
            public bool HasSeq;
            public int Level;
            public bool HasLevel;
            public string Xml;
            public string Json;
        }
    }
}
