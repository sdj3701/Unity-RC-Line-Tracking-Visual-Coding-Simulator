using System.Collections.Generic;
using System.Threading.Tasks;

public interface IBlockShareListService
{
    Task<IReadOnlyList<BlockShareListItemViewModel>> FetchListAsync(
        string roomId,
        int page,
        int size,
        string accessTokenOverride = null);
}
