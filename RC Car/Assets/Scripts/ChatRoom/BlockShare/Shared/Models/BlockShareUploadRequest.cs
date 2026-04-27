using System;

[Serializable]
public sealed class BlockShareUploadRequest
{
    public string RoomId;
    public int UserLevelSeq;
    public string Message;
    public string AccessTokenOverride;
}
