using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class ChatRoomBlockShareListService : IBlockShareListService
{
    public Task<IReadOnlyList<BlockShareListItemViewModel>> FetchListAsync(
        string roomId,
        int page,
        int size,
        string accessTokenOverride = null)
    {
        string targetRoomId = Normalize(roomId);
        if (string.IsNullOrWhiteSpace(targetRoomId))
            throw new ArgumentException("roomId is empty.", nameof(roomId));

        ChatRoomManager manager = ChatRoomManager.Instance;
        if (manager == null)
            throw new InvalidOperationException("ChatRoomManager.Instance is null.");

        if (manager.IsBusy)
            throw new InvalidOperationException("ChatRoomManager is busy.");

        int targetPage = Math.Max(1, page);
        int targetSize = Math.Max(1, size);
        var taskSource = new TaskCompletionSource<IReadOnlyList<BlockShareListItemViewModel>>();

        void Cleanup()
        {
            manager.OnBlockShareListFetchSucceeded -= HandleSucceeded;
            manager.OnBlockShareListFetchFailed -= HandleFailed;
            manager.OnBlockShareListFetchCanceled -= HandleCanceled;
        }

        void HandleSucceeded(ChatRoomBlockShareListInfo info)
        {
            string incomingRoomId = info != null ? Normalize(info.RoomId) : string.Empty;
            if (!string.Equals(targetRoomId, incomingRoomId, StringComparison.Ordinal))
                return;

            Cleanup();
            taskSource.TrySetResult(BuildItems(info));
        }

        void HandleFailed(string failedRoomId, int failedPage, int failedSize, string message)
        {
            if (!string.Equals(targetRoomId, Normalize(failedRoomId), StringComparison.Ordinal))
                return;

            Cleanup();
            taskSource.TrySetException(new InvalidOperationException(
                string.IsNullOrWhiteSpace(message) ? "Block share list fetch failed." : message));
        }

        void HandleCanceled(string canceledRoomId, int canceledPage, int canceledSize)
        {
            if (!string.Equals(targetRoomId, Normalize(canceledRoomId), StringComparison.Ordinal))
                return;

            Cleanup();
            taskSource.TrySetCanceled();
        }

        manager.OnBlockShareListFetchSucceeded += HandleSucceeded;
        manager.OnBlockShareListFetchFailed += HandleFailed;
        manager.OnBlockShareListFetchCanceled += HandleCanceled;

        try
        {
            manager.FetchBlockShares(
                targetRoomId,
                targetPage,
                targetSize,
                string.IsNullOrWhiteSpace(accessTokenOverride) ? null : accessTokenOverride.Trim());
        }
        catch
        {
            Cleanup();
            throw;
        }

        return taskSource.Task;
    }

    private static IReadOnlyList<BlockShareListItemViewModel> BuildItems(ChatRoomBlockShareListInfo info)
    {
        ChatRoomBlockShareInfo[] sourceItems = info != null && info.Items != null
            ? info.Items
            : Array.Empty<ChatRoomBlockShareInfo>();

        var items = new List<BlockShareListItemViewModel>(sourceItems.Length);
        for (int i = 0; i < sourceItems.Length; i++)
        {
            ChatRoomBlockShareInfo item = sourceItems[i];
            if (item == null)
                continue;

            string fileName = Normalize(item.Message);
            items.Add(new BlockShareListItemViewModel
            {
                ShareId = Normalize(item.BlockShareId),
                RoomId = Normalize(item.RoomId),
                UserId = Normalize(item.UserId),
                UserLevelSeq = item.UserLevelSeq,
                FileName = fileName,
                CreatedAtUtc = Normalize(item.CreatedAtUtc),
                DisplayLabel = BuildDisplayLabel(item.UserId, fileName, item.UserLevelSeq)
            });
        }

        return CollapseLatestItemsPerUser(items);
    }

    private static IReadOnlyList<BlockShareListItemViewModel> CollapseLatestItemsPerUser(
        List<BlockShareListItemViewModel> items)
    {
        if (items == null || items.Count <= 1)
            return items ?? (IReadOnlyList<BlockShareListItemViewModel>)Array.Empty<BlockShareListItemViewModel>();

        var bestByUser = new Dictionary<string, BlockShareListItemViewModel>(StringComparer.Ordinal);
        var indexByUser = new Dictionary<string, int>(StringComparer.Ordinal);
        var passthrough = new List<(int Index, BlockShareListItemViewModel Item)>();

        for (int i = 0; i < items.Count; i++)
        {
            BlockShareListItemViewModel candidate = items[i];
            if (candidate == null)
                continue;

            string userId = Normalize(candidate.UserId);
            if (string.IsNullOrWhiteSpace(userId))
            {
                passthrough.Add((i, candidate));
                continue;
            }

            if (!bestByUser.TryGetValue(userId, out BlockShareListItemViewModel currentBest))
            {
                bestByUser[userId] = candidate;
                indexByUser[userId] = i;
                continue;
            }

            int currentIndex = indexByUser.TryGetValue(userId, out int storedIndex) ? storedIndex : -1;
            if (!IsCandidateNewer(candidate, i, currentBest, currentIndex))
                continue;

            bestByUser[userId] = candidate;
            indexByUser[userId] = i;
        }

        var ordered = new List<(int Index, BlockShareListItemViewModel Item)>(bestByUser.Count + passthrough.Count);
        foreach (KeyValuePair<string, BlockShareListItemViewModel> pair in bestByUser)
        {
            if (pair.Value == null)
                continue;

            int index = indexByUser.TryGetValue(pair.Key, out int storedIndex) ? storedIndex : int.MaxValue;
            ordered.Add((index, pair.Value));
        }

        ordered.AddRange(passthrough);
        ordered.Sort((left, right) => left.Index.CompareTo(right.Index));

        var collapsed = new List<BlockShareListItemViewModel>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Item != null)
                collapsed.Add(ordered[i].Item);
        }

        return collapsed;
    }

    private static bool IsCandidateNewer(
        BlockShareListItemViewModel candidate,
        int candidateIndex,
        BlockShareListItemViewModel currentBest,
        int currentIndex)
    {
        bool hasCandidateTime = TryParseCreatedAtUtc(candidate != null ? candidate.CreatedAtUtc : null, out DateTime candidateTime);
        bool hasCurrentTime = TryParseCreatedAtUtc(currentBest != null ? currentBest.CreatedAtUtc : null, out DateTime currentTime);

        if (hasCandidateTime && hasCurrentTime)
        {
            int compare = DateTime.Compare(candidateTime, currentTime);
            if (compare != 0)
                return compare > 0;
        }
        else if (hasCandidateTime)
        {
            return true;
        }
        else if (hasCurrentTime)
        {
            return false;
        }

        return candidateIndex >= currentIndex;
    }

    private static bool TryParseCreatedAtUtc(string raw, out DateTime value)
    {
        value = default;
        string createdAt = Normalize(raw);
        if (string.IsNullOrWhiteSpace(createdAt))
            return false;

        if (DateTimeOffset.TryParse(createdAt, out DateTimeOffset parsedOffset))
        {
            value = parsedOffset.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(createdAt, out DateTime parsedDateTime))
        {
            value = parsedDateTime.ToUniversalTime();
            return true;
        }

        return false;
    }

    private static string BuildDisplayLabel(string userId, string fileName, int userLevelSeq)
    {
        string left = string.IsNullOrWhiteSpace(userId) ? "-" : userId.Trim();
        string right = !string.IsNullOrWhiteSpace(fileName)
            ? fileName.Trim()
            : (userLevelSeq > 0 ? userLevelSeq.ToString() : "-");
        return $"{left} / {right}";
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
