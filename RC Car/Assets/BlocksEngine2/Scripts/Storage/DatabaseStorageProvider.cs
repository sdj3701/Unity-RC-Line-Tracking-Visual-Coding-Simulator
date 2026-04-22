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

            if (!TryNormalizeLevel(fileName, out string level))
            {
                Debug.LogWarning($"[DatabaseStorageProvider] fileName '{fileName}' is empty. Save aborted (local save fallback disabled).");
                return await SaveWithFallbackAsync(fileName, xmlContent, jsonContent, isModified);
            }

            LogDbInfo(
                $"[DatabaseStorageProvider] Save request prepared. level='{level}', xmlLen={SafeLength(xmlContent)}, jsonLen={SafeLength(jsonContent)}, isModified={isModified}");

            UserLevelEntry existing = await FindEntryByLevelAsync(level, accessToken);
            SaveRemoteResult saveResult = await SaveOrUpdateRemoteAsync(existing, level, xmlContent, jsonContent, accessToken);
            if (saveResult != null && saveResult.Success)
            {
                bool verified = await VerifyRemoteSaveAsync(saveResult, level, xmlContent, jsonContent, accessToken);
                if (verified)
                    return true;

                Debug.LogWarning(
                    $"[DatabaseStorageProvider] Save readback verification failed. level='{level}', seq={saveResult.Seq}, method={saveResult.Method}, code={saveResult.ResponseCode}");

                UserLevelEntry retryTarget = saveResult.Seq > 0
                    ? new UserLevelEntry { Seq = saveResult.Seq, HasSeq = true, Level = level, HasLevel = true }
                    : await FindEntryByLevelAsync(level, accessToken);
                SaveRemoteResult jsonRetry = await SaveOrUpdateRemoteJsonOnlyAsync(
                    retryTarget,
                    level,
                    xmlContent,
                    jsonContent,
                    accessToken);
                if (jsonRetry != null && jsonRetry.Success)
                {
                    bool retryVerified = await VerifyRemoteSaveAsync(jsonRetry, level, xmlContent, jsonContent, accessToken);
                    if (retryVerified)
                        return true;
                }
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

            if (!TryNormalizeLevel(fileName, out string level))
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
                LogDbInfo($"[DatabaseStorageProvider] LoadXmlAsync success from DB. level='{level}', len={entry.Xml.Length}");
                return entry.Xml;
            }

            if (entry.HasSeq)
            {
                UserLevelEntry detail = await GetEntryBySeqAsync(entry.Seq, accessToken);
                if (detail != null && !string.IsNullOrEmpty(detail.Xml))
                {
                    LogDbInfo($"[DatabaseStorageProvider] LoadXmlAsync success from DB detail. level='{level}', seq={entry.Seq}, len={detail.Xml.Length}");
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

            if (!TryNormalizeLevel(fileName, out string level))
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
                LogDbInfo($"[DatabaseStorageProvider] LoadJsonAsync success from DB. level='{level}', len={entry.Json.Length}");
                return entry.Json;
            }

            if (entry.HasSeq)
            {
                UserLevelEntry detail = await GetEntryBySeqAsync(entry.Seq, accessToken);
                if (detail != null && !string.IsNullOrEmpty(detail.Json))
                {
                    LogDbInfo($"[DatabaseStorageProvider] LoadJsonAsync success from DB detail. level='{level}', seq={entry.Seq}, len={detail.Json.Length}");
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

            var levelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UserLevelEntry entry in entries)
            {
                if (entry.HasLevel && TryNormalizeLevel(entry.Level, out string level))
                {
                    levelSet.Add(level);
                }
            }

            if (levelSet.Count == 0)
            {
                return await GetFileListWithFallbackAsync();
            }

            List<string> fileNames = new List<string>(levelSet);
            fileNames.Sort(StringComparer.OrdinalIgnoreCase);

            LogDbInfo($"[DatabaseStorageProvider] GetFileListAsync success from DB. count={fileNames.Count}");
            return fileNames;
        }

        public async Task<List<UserLevelFileEntry>> GetFileEntriesAsync()
        {
            var result = new List<UserLevelFileEntry>();

            if (!IsConfigured(_apiBaseUrl))
            {
                return result;
            }

            if (!TryGetAuthInfo(out string _, out string accessToken))
            {
                return result;
            }

            List<UserLevelEntry> entries = await GetMyEntriesAsync(accessToken);
            if (entries.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                UserLevelEntry entry = entries[i];
                if (entry == null || !entry.HasLevel || !TryNormalizeLevel(entry.Level, out string level))
                    continue;

                int seq = 0;
                if (entry.HasSeq && entry.Seq > 0 && entry.Seq <= int.MaxValue)
                    seq = (int)entry.Seq;

                result.Add(new UserLevelFileEntry
                {
                    FileName = level,
                    UserLevelSeq = seq
                });
            }

            result.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
            return result;
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

            if (!TryNormalizeLevel(fileName, out string level))
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

            if (!TryNormalizeLevel(fileName, out string level))
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
                        $"level={(entry.HasLevel ? entry.Level : "n/a")}, xmlLen={xmlLen}, jsonLen={jsonLen}");
                }
            }
        }

        private async Task<SaveRemoteResult> SaveOrUpdateRemoteAsync(UserLevelEntry existing, string level, string xmlContent, string jsonContent, string accessToken)
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

                SaveRemoteResult patched = await SendMultipartAsync(updateUrl, "PATCH", payload, accessToken, "UpdateCode(PATCH multipart)");
                if (patched.Success)
                    return patched.WithSeqIfMissing(existing.Seq);

                SaveRemoteResult patchedJson = await SendJsonAsync(updateUrl, "PATCH", payload, accessToken, "UpdateCode(PATCH json)");
                if (patchedJson.Success)
                    return patchedJson.WithSeqIfMissing(existing.Seq);

                SaveRemoteResult put = await SendMultipartAsync(updateUrl, "PUT", payload, accessToken, "UpdateCode(PUT multipart)");
                if (put.Success)
                    return put.WithSeqIfMissing(existing.Seq);

                SaveRemoteResult putJson = await SendJsonAsync(updateUrl, "PUT", payload, accessToken, "UpdateCode(PUT json)");
                return putJson.WithSeqIfMissing(existing.Seq);
            }

            string createUrl = BuildUserLevelCollectionUrl();
            SaveRemoteResult created = await SendMultipartAsync(createUrl, UnityWebRequest.kHttpVerbPOST, payload, accessToken, "SaveCode(POST multipart)");
            if (created.Success)
                return created;

            return await SendJsonAsync(createUrl, UnityWebRequest.kHttpVerbPOST, payload, accessToken, "SaveCode(POST json)");
        }

        private async Task<SaveRemoteResult> SaveOrUpdateRemoteJsonOnlyAsync(
            UserLevelEntry existing,
            string level,
            string xmlContent,
            string jsonContent,
            string accessToken)
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
                SaveRemoteResult patchedJson = await SendJsonAsync(updateUrl, "PATCH", payload, accessToken, "VerifyRetry(PATCH json)");
                if (patchedJson.Success)
                    return patchedJson.WithSeqIfMissing(existing.Seq);

                SaveRemoteResult putJson = await SendJsonAsync(updateUrl, "PUT", payload, accessToken, "VerifyRetry(PUT json)");
                return putJson.WithSeqIfMissing(existing.Seq);
            }

            string createUrl = BuildUserLevelCollectionUrl();
            return await SendJsonAsync(createUrl, UnityWebRequest.kHttpVerbPOST, payload, accessToken, "VerifyRetry(POST json)");
        }

        private async Task<SaveRemoteResult> SendMultipartAsync(string url, string method, SaveCodeRequest payload, string accessToken, string logTag)
        {
            List<IMultipartFormSection> sections = new List<IMultipartFormSection>();
            foreach (KeyValuePair<string, string> field in BuildSaveFields(payload))
            {
                sections.Add(new MultipartFormDataSection(field.Key, field.Value ?? string.Empty));
            }

            LogDbInfo(
                $"[DatabaseStorageProvider] {logTag} request. method={method}, url={url}, level='{payload.level}', xmlLen={SafeLength(payload.xml)}, jsonLen={SafeLength(payload.json)}");

            using (var request = UnityWebRequest.Post(url, sections))
            {
                request.method = method;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Accept", "application/json");
                RequestResult result = await SendRequestForResultAsync(request, logTag);
                return SaveRemoteResult.FromRequestResult(result, method, url);
            }
        }

        private async Task<SaveRemoteResult> SendJsonAsync(string url, string method, SaveCodeRequest payload, string accessToken, string logTag)
        {
            string requestJson = BuildSaveJsonBody(payload);

            LogDbInfo(
                $"[DatabaseStorageProvider] {logTag} request. method={method}, url={url}, level='{payload.level}', xmlLen={SafeLength(payload.xml)}, jsonLen={SafeLength(payload.json)}");

            using (var request = new UnityWebRequest(url, method))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                RequestResult result = await SendRequestForResultAsync(request, logTag);
                return SaveRemoteResult.FromRequestResult(result, method, url);
            }
        }

        private static List<KeyValuePair<string, string>> BuildSaveFields(SaveCodeRequest payload)
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("level", payload.level ?? string.Empty),
                new KeyValuePair<string, string>("xml", payload.xml ?? string.Empty),
                new KeyValuePair<string, string>("json", payload.json ?? string.Empty),
                new KeyValuePair<string, string>("xmlLongText", payload.xmlLongText ?? string.Empty),
                new KeyValuePair<string, string>("jsonLongText", payload.jsonLongText ?? string.Empty),
                new KeyValuePair<string, string>("xmlData", payload.xml ?? string.Empty),
                new KeyValuePair<string, string>("jsonData", payload.json ?? string.Empty),
                new KeyValuePair<string, string>("xml_data", payload.xml ?? string.Empty),
                new KeyValuePair<string, string>("json_data", payload.json ?? string.Empty),
                new KeyValuePair<string, string>("xml_long_text", payload.xmlLongText ?? string.Empty),
                new KeyValuePair<string, string>("json_long_text", payload.jsonLongText ?? string.Empty)
            };
        }

        private async Task<bool> VerifyRemoteSaveAsync(
            SaveRemoteResult saveResult,
            string level,
            string expectedXml,
            string expectedJson,
            string accessToken)
        {
            UserLevelEntry entry = null;
            if (saveResult != null && saveResult.Seq > 0)
                entry = await GetEntryBySeqAsync(saveResult.Seq, accessToken);

            if (entry == null)
                entry = await FindEntryByLevelAsync(level, accessToken);

            if (entry != null && entry.HasSeq && (string.IsNullOrEmpty(entry.Xml) || string.IsNullOrEmpty(entry.Json)))
            {
                UserLevelEntry detail = await GetEntryBySeqAsync(entry.Seq, accessToken);
                if (detail != null)
                    entry = MergeEntry(entry, detail);
            }

            if (entry == null)
            {
                Debug.LogWarning($"[DatabaseStorageProvider] Save verify failed. level='{level}' entry not found.");
                return false;
            }

            string actualXml = entry.Xml ?? string.Empty;
            string actualJson = NormalizeReturnedJson(entry.Json);
            string normalizedExpectedXml = NormalizeCompareText(expectedXml);
            string normalizedExpectedJson = NormalizeCompareText(NormalizeRawJson(expectedJson));
            string normalizedActualXml = NormalizeCompareText(actualXml);
            string normalizedActualJson = NormalizeCompareText(actualJson);

            bool xmlPresent = !string.IsNullOrWhiteSpace(actualXml);
            bool jsonPresent = !string.IsNullOrWhiteSpace(actualJson);
            bool xmlHashMatch = xmlPresent && StableHash(normalizedActualXml) == StableHash(normalizedExpectedXml);
            bool jsonHashMatch = jsonPresent && StableHash(normalizedActualJson) == StableHash(normalizedExpectedJson);

            LogDbInfo(
                $"[DatabaseStorageProvider] Save readback. level='{level}', seq={(entry.HasSeq ? entry.Seq.ToString() : "n/a")}, " +
                $"xmlLen={actualXml.Length}, jsonLen={actualJson.Length}, xmlHashMatch={xmlHashMatch}, jsonHashMatch={jsonHashMatch}");

            return xmlHashMatch && jsonHashMatch;
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

        private async Task<UserLevelEntry> FindEntryByLevelAsync(string level, string accessToken)
        {
            List<UserLevelEntry> entries = await GetMyEntriesAsync(accessToken);
            foreach (UserLevelEntry entry in entries)
            {
                if (entry.HasLevel && AreSameLevel(entry.Level, level))
                {
                    return entry;
                }
            }

            return null;
        }

        private static async Task<bool> SendRequestAsync(UnityWebRequest request, string logTag)
        {
            RequestResult result = await SendRequestForResultAsync(request, logTag);
            return result.Success;
        }

        private static async Task<RequestResult> SendRequestForResultAsync(UnityWebRequest request, string logTag)
        {
            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

            string body = request.downloadHandler?.text ?? string.Empty;
            bool success = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
            if (!success)
            {
                Debug.LogWarning($"[DatabaseStorageProvider] {logTag} failed. code={request.responseCode}, error={request.error}, body={body}");
            }
            else
            {
                LogDbInfo(
                    $"[DatabaseStorageProvider] {logTag} success. code={request.responseCode}, seq={ExtractSeqFromResponse(body)}, body={TruncateForLog(body)}");
            }

            return new RequestResult
            {
                Success = success,
                ResponseCode = request.responseCode,
                Body = body,
                Error = request.error
            };
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

        private static bool TryNormalizeLevel(string fileName, out string level)
        {
            level = fileName?.Trim();
            if (string.IsNullOrEmpty(level))
            {
                return false;
            }

            return true;
        }

        private static bool AreSameLevel(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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

        private static string BuildSaveJsonBody(SaveCodeRequest payload)
        {
            string normalizedJson = NormalizeRawJson(payload != null ? payload.json : null);
            string xml = payload != null ? payload.xml ?? string.Empty : string.Empty;
            string level = payload != null ? payload.level ?? string.Empty : string.Empty;

            return "{" +
                   $"\"level\":\"{EscapeJsonString(level)}\"," +
                   $"\"xml\":\"{EscapeJsonString(xml)}\"," +
                   $"\"xmlLongText\":\"{EscapeJsonString(xml)}\"," +
                   $"\"xmlData\":\"{EscapeJsonString(xml)}\"," +
                   $"\"xml_data\":\"{EscapeJsonString(xml)}\"," +
                   $"\"json\":{normalizedJson}," +
                   $"\"jsonLongText\":{normalizedJson}," +
                   $"\"jsonData\":{normalizedJson}," +
                   $"\"json_data\":{normalizedJson}," +
                   $"\"json_long_text\":{normalizedJson}" +
                   "}";
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
            bool hasLevel = TryExtractJsonStringOrNumberValue(json, "level", out string level);

            string xml = null;
            bool hasXml = TryExtractJsonStringValue(json, "xml", out xml) ||
                          TryExtractJsonStringValue(json, "xmlLongText", out xml) ||
                          TryExtractJsonStringValue(json, "xmlData", out xml) ||
                          TryExtractJsonStringValue(json, "xml_data", out xml) ||
                          TryExtractJsonStringValue(json, "xml_long_text", out xml);

            string jsonContent = null;
            bool hasJson = TryExtractRawJsonField(json, "json", out jsonContent) ||
                           TryExtractJsonStringValue(json, "json", out jsonContent) ||
                           TryExtractRawJsonField(json, "jsonLongText", out jsonContent) ||
                           TryExtractJsonStringValue(json, "jsonLongText", out jsonContent) ||
                           TryExtractRawJsonField(json, "jsonData", out jsonContent) ||
                           TryExtractJsonStringValue(json, "jsonData", out jsonContent) ||
                           TryExtractRawJsonField(json, "json_data", out jsonContent) ||
                           TryExtractJsonStringValue(json, "json_data", out jsonContent) ||
                           TryExtractRawJsonField(json, "json_long_text", out jsonContent) ||
                           TryExtractJsonStringValue(json, "json_long_text", out jsonContent);

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

        private static bool TryExtractJsonStringOrNumberValue(string json, string fieldName, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            if (TryExtractJsonStringValue(json, fieldName, out string stringValue))
            {
                value = stringValue?.Trim();
                return !string.IsNullOrEmpty(value);
            }

            if (TryExtractJsonLongValue(json, fieldName, out long longValue))
            {
                value = longValue.ToString();
                return true;
            }

            return false;
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

        private static long ExtractSeqFromResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return 0L;

            string[] keys =
            {
                "seq",
                "id",
                "userLevelSeq",
                "user_level_seq",
                "levelSeq",
                "level_seq"
            };

            for (int i = 0; i < keys.Length; i++)
            {
                if (TryExtractJsonLongValue(responseBody, keys[i], out long value) && value > 0)
                    return value;
            }

            List<UserLevelEntry> entries = ParseUserLevelEntries(responseBody);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].HasSeq && entries[i].Seq > 0)
                    return entries[i].Seq;
            }

            return 0L;
        }

        private static UserLevelEntry MergeEntry(UserLevelEntry primary, UserLevelEntry detail)
        {
            if (primary == null)
                return detail;

            if (detail == null)
                return primary;

            return new UserLevelEntry
            {
                Seq = detail.HasSeq ? detail.Seq : primary.Seq,
                HasSeq = detail.HasSeq || primary.HasSeq,
                Level = !string.IsNullOrWhiteSpace(detail.Level) ? detail.Level : primary.Level,
                HasLevel = detail.HasLevel || primary.HasLevel,
                Xml = !string.IsNullOrEmpty(detail.Xml) ? detail.Xml : primary.Xml,
                Json = !string.IsNullOrEmpty(detail.Json) ? detail.Json : primary.Json
            };
        }

        private static string NormalizeReturnedJson(string value)
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2);
                return Regex.Unescape(inner).Replace("\\/", "/").Trim();
            }

            return trimmed;
        }

        private static string NormalizeCompareText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string StableHash(string value)
        {
            string text = value ?? string.Empty;
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint hash = offset;

                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= prime;
                }

                return hash.ToString("X8");
            }
        }

        private static int SafeLength(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : value.Length;
        }

        private static string TruncateForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            const int maxLength = 500;
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength) + "...";
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
            public string level;
            public string xml;
            public string json;
            public string xmlLongText;
            public string jsonLongText;
        }

        private sealed class UserLevelEntry
        {
            public long Seq;
            public bool HasSeq;
            public string Level;
            public bool HasLevel;
            public string Xml;
            public string Json;
        }

        private sealed class RequestResult
        {
            public bool Success;
            public long ResponseCode;
            public string Body;
            public string Error;
        }

        private sealed class SaveRemoteResult
        {
            public bool Success;
            public long Seq;
            public long ResponseCode;
            public string ResponseBody;
            public string Error;
            public string Method;
            public string Url;

            public SaveRemoteResult WithSeqIfMissing(long fallbackSeq)
            {
                if (Seq <= 0 && fallbackSeq > 0)
                    Seq = fallbackSeq;

                return this;
            }

            public static SaveRemoteResult FromRequestResult(RequestResult result, string method, string url)
            {
                if (result == null)
                    result = new RequestResult();

                string body = result.Body ?? string.Empty;
                return new SaveRemoteResult
                {
                    Success = result.Success,
                    Seq = ExtractSeqFromResponse(body),
                    ResponseCode = result.ResponseCode,
                    ResponseBody = body,
                    Error = result.Error,
                    Method = method ?? string.Empty,
                    Url = url ?? string.Empty
                };
            }
        }

        public sealed class UserLevelFileEntry
        {
            public string FileName;
            public int UserLevelSeq;
        }
    }
}
