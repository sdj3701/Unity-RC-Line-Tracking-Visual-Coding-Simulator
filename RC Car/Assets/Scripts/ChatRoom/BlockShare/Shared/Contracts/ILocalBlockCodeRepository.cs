using System.Collections.Generic;
using System.Threading.Tasks;

public interface ILocalBlockCodeRepository
{
    Task<IReadOnlyList<LocalBlockCodeEntry>> GetEntriesAsync();
}
