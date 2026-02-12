using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// 로컬 파일 저장소 구현체입니다.
    /// persistentDataPath/SavedCodes 경로에 XML + JSON을 저장합니다.
    /// </summary>
    public class LocalStorageProvider : ICodeStorageProvider
    {
        private readonly string _basePath;

        public LocalStorageProvider()
        {
            _basePath = Path.Combine(Application.persistentDataPath, "SavedCodes");
            EnsureDirectoryExists();
        }

        public LocalStorageProvider(string customPath)
        {
            _basePath = customPath;
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
                Debug.Log($"[LocalStorageProvider] Save directory created: {_basePath}");
            }
        }

        private string GetXmlPath(string fileName) => Path.Combine(_basePath, $"{fileName}.xml");
        private string GetJsonPath(string fileName) => Path.Combine(_basePath, $"{fileName}.json");

        /// <summary>
        /// XML + JSON을 로컬에 저장하고 실행기에서 사용하는 런타임 파일을 갱신합니다.
        /// </summary>
        public async Task<bool> SaveCodeAsync(string fileName, string xmlContent, string jsonContent, bool isModified)
        {
            try
            {
                EnsureDirectoryExists();

                string xmlPath = GetXmlPath(fileName);
                string jsonPath = GetJsonPath(fileName);

                string safeXml = xmlContent ?? string.Empty;
                string safeJson = jsonContent ?? "{}";

                // 실제 파일 생성 부분
                await Task.Run(() => File.WriteAllText(xmlPath, safeXml));
                await Task.Run(() => File.WriteAllText(jsonPath, safeJson));

                // BlockCodeExecutor가 즉시 다시 로드할 수 있도록 런타임 파일을 동기화합니다.
                string runtimeJsonPath = Path.Combine(Application.persistentDataPath, "BlocksRuntime.json");
                string runtimeXmlPath = Path.Combine(Application.persistentDataPath, "BlocksRuntime.xml");

                await Task.Run(() => File.WriteAllText(runtimeJsonPath, safeJson));
                await Task.Run(() => File.WriteAllText(runtimeXmlPath, safeXml));

                Debug.Log($"[LocalStorageProvider] Saved '{fileName}' (isModified={isModified})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] Save failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 로컬 저장소에서 XML을 불러옵니다.
        /// </summary>
        public async Task<string> LoadXmlAsync(string fileName)
        {
            try
            {
                string path = GetXmlPath(fileName);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[LocalStorageProvider] XML not found: {path}");
                    return null;
                }

                return await Task.Run(() => File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] XML load failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 로컬 저장소에서 JSON을 불러옵니다.
        /// </summary>
        public async Task<string> LoadJsonAsync(string fileName)
        {
            try
            {
                string path = GetJsonPath(fileName);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[LocalStorageProvider] JSON not found: {path}");
                    return null;
                }

                return await Task.Run(() => File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] JSON load failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 확장자를 제외한 XML 파일 목록을 반환합니다.
        /// </summary>
        public Task<List<string>> GetFileListAsync()
        {
            try
            {
                EnsureDirectoryExists();

                DirectoryInfo dirInfo = new DirectoryInfo(_basePath);
                FileInfo[] xmlFiles = dirInfo.GetFiles("*.xml");
                List<string> fileList = xmlFiles.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToList();
                return Task.FromResult(fileList);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] File list load failed: {ex.Message}");
                return Task.FromResult(new List<string>());
            }
        }

        /// <summary>
        /// 로컬 XML 파일 존재 여부를 확인합니다.
        /// </summary>
        public Task<bool> FileExistsAsync(string fileName)
        {
            bool exists = File.Exists(GetXmlPath(fileName));
            return Task.FromResult(exists);
        }

        /// <summary>
        /// 로컬 XML + JSON 쌍을 삭제합니다.
        /// </summary>
        public Task<bool> DeleteCodeAsync(string fileName)
        {
            try
            {
                string xmlPath = GetXmlPath(fileName);
                string jsonPath = GetJsonPath(fileName);
                bool deleted = false;

                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                    deleted = true;
                }

                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    deleted = true;
                }

                return Task.FromResult(deleted);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] Delete failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public string GetBasePath() => _basePath;
    }
}

