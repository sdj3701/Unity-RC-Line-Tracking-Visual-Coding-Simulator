using System.Collections.Generic;
using System.Threading.Tasks;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// 코드 저장/불러오기 제공자용 저장소 추상화 인터페이스입니다.
    /// 로컬 파일 I/O와 원격 HTTP I/O를 모두 지원하므로 모든 API를 비동기로 제공합니다.
    /// </summary>
    public interface ICodeStorageProvider
    {
        /// <summary>
        /// XML + JSON 쌍을 저장합니다. `isModified`는 기존 XML과 다를 때만 true입니다.
        /// </summary>
        Task<bool> SaveCodeAsync(string fileName, string xmlContent, string jsonContent, bool isModified);

        /// <summary>
        /// 단일 파일의 XML 내용을 불러옵니다.
        /// </summary>
        Task<string> LoadXmlAsync(string fileName);

        /// <summary>
        /// 단일 파일의 JSON 내용을 불러옵니다.
        /// </summary>
        Task<string> LoadJsonAsync(string fileName);

        /// <summary>
        /// 확장자를 제외한 저장 파일 목록을 반환합니다.
        /// </summary>
        Task<List<string>> GetFileListAsync();

        /// <summary>
        /// 파일 존재 여부를 확인합니다.
        /// </summary>
        Task<bool> FileExistsAsync(string fileName);

        /// <summary>
        /// XML + JSON 쌍을 삭제합니다.
        /// </summary>
        Task<bool> DeleteCodeAsync(string fileName);
    }
}

