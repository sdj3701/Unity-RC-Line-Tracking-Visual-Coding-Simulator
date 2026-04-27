using System.Collections.Generic;
using System.Threading.Tasks;
using MG_BlocksEngine2.Storage;

public sealed class BE2LocalBlockCodeRepository : ILocalBlockCodeRepository
{
    private readonly IUserLevelSeqResolver _userLevelSeqResolver;

    public BE2LocalBlockCodeRepository(IUserLevelSeqResolver userLevelSeqResolver)
    {
        _userLevelSeqResolver = userLevelSeqResolver;
    }

    public async Task<IReadOnlyList<LocalBlockCodeEntry>> GetEntriesAsync()
    {
        var results = new List<LocalBlockCodeEntry>();
        BE2_CodeStorageManager storageManager = BE2_CodeStorageManager.Instance;
        if (storageManager == null)
            return results;

        List<BE2_CodeStorageFileEntry> fileEntries = await storageManager.GetFileEntriesAsync();
        if (fileEntries != null && fileEntries.Count > 0)
        {
            for (int i = 0; i < fileEntries.Count; i++)
            {
                BE2_CodeStorageFileEntry entry = fileEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FileName))
                    continue;

                int userLevelSeq = entry.UserLevelSeq > 0
                    ? entry.UserLevelSeq
                    : ResolveUserLevelSeq(entry.FileName);

                results.Add(new LocalBlockCodeEntry
                {
                    FileName = entry.FileName.Trim(),
                    UserLevelSeq = userLevelSeq,
                    HasServerSeq = entry.UserLevelSeq > 0
                });
            }

            return results;
        }

        List<string> fileNames = await storageManager.GetFileListAsync();
        if (fileNames == null)
            return results;

        for (int i = 0; i < fileNames.Count; i++)
        {
            string fileName = fileNames[i];
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            results.Add(new LocalBlockCodeEntry
            {
                FileName = fileName.Trim(),
                UserLevelSeq = ResolveUserLevelSeq(fileName),
                HasServerSeq = false
            });
        }

        return results;
    }

    private int ResolveUserLevelSeq(string fileName)
    {
        return _userLevelSeqResolver != null
            ? _userLevelSeqResolver.Resolve(fileName)
            : 1;
    }
}
