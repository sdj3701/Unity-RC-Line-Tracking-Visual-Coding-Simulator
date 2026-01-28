using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// 로컬 파일 시스템 저장소 구현
    /// persistentDataPath/SavedCodes/ 폴더에 XML + JSON 파일로 저장
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
                Debug.Log($"[LocalStorageProvider] 저장 폴더 생성: {_basePath}");
            }
        }

        private string GetXmlPath(string fileName) => Path.Combine(_basePath, $"{fileName}.xml");
        private string GetJsonPath(string fileName) => Path.Combine(_basePath, $"{fileName}.json");

        public bool SaveCode(string fileName, string xmlContent, string jsonContent)
        {
            try
            {
                EnsureDirectoryExists();
                
                string xmlPath = GetXmlPath(fileName);
                string jsonPath = GetJsonPath(fileName);

                File.WriteAllText(xmlPath, xmlContent);
                Debug.Log($"[LocalStorageProvider] XML 저장 완료: {xmlPath}");

                File.WriteAllText(jsonPath, jsonContent);
                Debug.Log($"[LocalStorageProvider] JSON 저장 완료: {jsonPath}");

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] 저장 실패: {ex.Message}");
                return false;
            }
        }

        public string LoadXml(string fileName)
        {
            try
            {
                string path = GetXmlPath(fileName);
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
                Debug.LogWarning($"[LocalStorageProvider] XML 파일 없음: {path}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] XML 로드 실패: {ex.Message}");
                return null;
            }
        }

        public string LoadJson(string fileName)
        {
            try
            {
                string path = GetJsonPath(fileName);
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
                Debug.LogWarning($"[LocalStorageProvider] JSON 파일 없음: {path}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] JSON 로드 실패: {ex.Message}");
                return null;
            }
        }

        public List<string> GetFileList()
        {
            try
            {
                EnsureDirectoryExists();
                
                DirectoryInfo dirInfo = new DirectoryInfo(_basePath);
                FileInfo[] xmlFiles = dirInfo.GetFiles("*.xml");

                // 확장자 제외한 파일명 반환
                return xmlFiles.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] 파일 목록 조회 실패: {ex.Message}");
                return new List<string>();
            }
        }

        public bool FileExists(string fileName)
        {
            return File.Exists(GetXmlPath(fileName));
        }

        public bool DeleteCode(string fileName)
        {
            try
            {
                string xmlPath = GetXmlPath(fileName);
                string jsonPath = GetJsonPath(fileName);

                bool deleted = false;

                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                    Debug.Log($"[LocalStorageProvider] XML 삭제: {xmlPath}");
                    deleted = true;
                }

                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    Debug.Log($"[LocalStorageProvider] JSON 삭제: {jsonPath}");
                    deleted = true;
                }

                return deleted;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalStorageProvider] 삭제 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 저장 폴더 경로 반환
        /// </summary>
        public string GetBasePath() => _basePath;
    }

    // TODO: [향후 확장] DatabaseStorageProvider 구현 시 참고
    // - 이 클래스와 동일한 인터페이스 구현
    // - BE2_CodeStorageManager.SetStorageProvider()로 교체
}
