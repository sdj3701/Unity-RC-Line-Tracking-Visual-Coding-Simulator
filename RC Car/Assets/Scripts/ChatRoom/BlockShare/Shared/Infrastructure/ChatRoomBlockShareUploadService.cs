using System;
using System.Threading.Tasks;

public sealed class ChatRoomBlockShareUploadService : IBlockShareUploadService
{
    public Task<BlockShareUploadResult> UploadAsync(BlockShareUploadRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string roomId = Normalize(request.RoomId);
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("roomId is empty.", nameof(request));

        if (request.UserLevelSeq <= 0)
            throw new ArgumentException("userLevelSeq must be >= 1.", nameof(request));

        ChatRoomManager manager = ChatRoomManager.Instance;
        if (manager == null)
            throw new InvalidOperationException("ChatRoomManager.Instance is null.");

        if (manager.IsBusy)
            throw new InvalidOperationException("ChatRoomManager is busy.");

        var taskSource = new TaskCompletionSource<BlockShareUploadResult>();

        void Cleanup()
        {
            manager.OnBlockShareUploadSucceeded -= HandleSucceeded;
            manager.OnBlockShareUploadFailed -= HandleFailed;
            manager.OnBlockShareUploadCanceled -= HandleCanceled;
        }

        void HandleSucceeded(ChatRoomBlockShareUploadInfo info)
        {
            string incomingRoomId = info != null ? Normalize(info.RoomId) : string.Empty;
            int incomingUserLevelSeq = info != null ? info.UserLevelSeq : 0;
            if (!IsSameRequest(roomId, request.UserLevelSeq, incomingRoomId, incomingUserLevelSeq))
                return;

            Cleanup();
            taskSource.TrySetResult(new BlockShareUploadResult
            {
                RoomId = incomingRoomId,
                UserLevelSeq = incomingUserLevelSeq,
                Message = info != null ? Normalize(info.Message) : string.Empty,
                BlockShareId = info != null ? Normalize(info.BlockShareId) : string.Empty,
                ResponseCode = info != null ? info.ResponseCode : 0L,
                ResponseBody = info != null ? info.ResponseBody : string.Empty
            });
        }

        void HandleFailed(string failedRoomId, int failedUserLevelSeq, string message)
        {
            if (!IsSameRequest(roomId, request.UserLevelSeq, Normalize(failedRoomId), failedUserLevelSeq))
                return;

            Cleanup();
            taskSource.TrySetException(new InvalidOperationException(
                string.IsNullOrWhiteSpace(message) ? "Block share upload failed." : message));
        }

        void HandleCanceled(string canceledRoomId, int canceledUserLevelSeq)
        {
            if (!IsSameRequest(roomId, request.UserLevelSeq, Normalize(canceledRoomId), canceledUserLevelSeq))
                return;

            Cleanup();
            taskSource.TrySetCanceled();
        }

        manager.OnBlockShareUploadSucceeded += HandleSucceeded;
        manager.OnBlockShareUploadFailed += HandleFailed;
        manager.OnBlockShareUploadCanceled += HandleCanceled;

        try
        {
            manager.UploadBlockShare(
                roomId,
                request.UserLevelSeq,
                request.Message ?? string.Empty,
                string.IsNullOrWhiteSpace(request.AccessTokenOverride)
                    ? null
                    : request.AccessTokenOverride.Trim());
        }
        catch
        {
            Cleanup();
            throw;
        }

        return taskSource.Task;
    }

    private static bool IsSameRequest(
        string expectedRoomId,
        int expectedUserLevelSeq,
        string incomingRoomId,
        int incomingUserLevelSeq)
    {
        return string.Equals(expectedRoomId, incomingRoomId, StringComparison.Ordinal) &&
               expectedUserLevelSeq == incomingUserLevelSeq;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
