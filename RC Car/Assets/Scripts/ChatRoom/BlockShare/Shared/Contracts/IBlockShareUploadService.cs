using System.Threading.Tasks;

public interface IBlockShareUploadService
{
    Task<BlockShareUploadResult> UploadAsync(BlockShareUploadRequest request);
}
