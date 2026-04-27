using System.Text.RegularExpressions;
using UnityEngine;

public sealed class FileNameUserLevelSeqResolver : IUserLevelSeqResolver
{
    private static readonly Regex NumberPattern = new Regex("\\d+", RegexOptions.Compiled);

    private readonly int _fallbackUserLevelSeq;

    public FileNameUserLevelSeqResolver(int fallbackUserLevelSeq)
    {
        _fallbackUserLevelSeq = Mathf.Max(1, fallbackUserLevelSeq);
    }

    public int Resolve(string fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            Match match = NumberPattern.Match(fileName);
            if (match.Success && int.TryParse(match.Value, out int parsed) && parsed > 0)
                return parsed;
        }

        return _fallbackUserLevelSeq;
    }
}
