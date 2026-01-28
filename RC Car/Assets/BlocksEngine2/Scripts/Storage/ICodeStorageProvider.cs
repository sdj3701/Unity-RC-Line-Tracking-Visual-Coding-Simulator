using System.Collections.Generic;

namespace MG_BlocksEngine2.Storage
{
    /// <summary>
    /// 코드 저장소 인터페이스 - 저장소 추상화 레이어
    /// 로컬 파일, DB, 클라우드 등 다양한 저장소 구현 가능
    /// </summary>
    public interface ICodeStorageProvider
    {
        /// <summary>
        /// 코드 저장 (XML + JSON)
        /// </summary>
        /// <param name="fileName">파일명 (확장자 제외)</param>
        /// <param name="xmlContent">XML 내용</param>
        /// <param name="jsonContent">JSON 내용</param>
        /// <returns>저장 성공 여부</returns>
        bool SaveCode(string fileName, string xmlContent, string jsonContent);

        /// <summary>
        /// XML 코드 불러오기
        /// </summary>
        /// <param name="fileName">파일명</param>
        /// <returns>XML 내용 (없으면 null)</returns>
        string LoadXml(string fileName);

        /// <summary>
        /// JSON 코드 불러오기
        /// </summary>
        /// <param name="fileName">파일명</param>
        /// <returns>JSON 내용 (없으면 null)</returns>
        string LoadJson(string fileName);

        /// <summary>
        /// 저장된 파일 목록 반환 (확장자 제외된 파일명)
        /// </summary>
        /// <returns>파일명 리스트</returns>
        List<string> GetFileList();

        /// <summary>
        /// 파일 존재 여부 확인
        /// </summary>
        /// <param name="fileName">파일명</param>
        /// <returns>존재하면 true</returns>
        bool FileExists(string fileName);

        /// <summary>
        /// 저장된 코드 삭제 (XML + JSON 모두)
        /// </summary>
        /// <param name="fileName">파일명</param>
        /// <returns>삭제 성공 여부</returns>
        bool DeleteCode(string fileName);
    }

    // TODO: [향후 확장] DatabaseStorageProvider 구현
    // DB 연결 시 이 인터페이스를 구현하는 DatabaseStorageProvider 클래스 생성
    // 예시:
    // public class DatabaseStorageProvider : ICodeStorageProvider
    // {
    //     private string _apiEndpoint;
    //     private string _userId;
    //     
    //     public bool SaveCode(string fileName, string xmlContent, string jsonContent)
    //     {
    //         // HTTP POST to server
    //     }
    //     
    //     public List<string> GetFileList()
    //     {
    //         // HTTP GET from server
    //     }
    // }
}
