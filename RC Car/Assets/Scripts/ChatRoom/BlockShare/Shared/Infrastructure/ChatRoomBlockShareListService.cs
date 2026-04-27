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

        return items;
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
