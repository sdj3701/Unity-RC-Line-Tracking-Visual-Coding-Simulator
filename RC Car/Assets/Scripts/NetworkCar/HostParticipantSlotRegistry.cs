using System;
using System.Collections.Generic;

public sealed class HostParticipantSlotRegistry
{
    private readonly Dictionary<string, HostParticipantSlot> _slotByUserId =
        new Dictionary<string, HostParticipantSlot>(StringComparer.Ordinal);
    private readonly SortedDictionary<int, string> _userIdBySlot =
        new SortedDictionary<int, string>();

    public int MaxCount { get; private set; }

    public bool TryRegisterUser(string userIdRaw, string userNameRaw, out HostParticipantSlot slot)
    {
        slot = null;

        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        if (_slotByUserId.TryGetValue(userId, out HostParticipantSlot existing))
        {
            slot = existing;
            return false;
        }

        int slotIndex = MaxCount + 1;
        string userName = string.IsNullOrWhiteSpace(userNameRaw) ? userId : userNameRaw.Trim();

        var created = new HostParticipantSlot
        {
            UserId = userId,
            UserName = userName,
            SlotIndex = slotIndex,
            ApprovedAtUtc = DateTime.UtcNow
        };

        _slotByUserId[userId] = created;
        _userIdBySlot[slotIndex] = userId;
        MaxCount = slotIndex;
        slot = created;
        return true;
    }

    public bool TryGetSlotByUserId(string userIdRaw, out HostParticipantSlot slot)
    {
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            slot = null;
            return false;
        }

        return _slotByUserId.TryGetValue(userId, out slot);
    }

    public bool TryGetSlotIndexByUserId(string userIdRaw, out int slotIndex)
    {
        slotIndex = 0;
        if (!TryGetSlotByUserId(userIdRaw, out HostParticipantSlot slot) || slot == null)
            return false;

        slotIndex = slot.SlotIndex;
        return slotIndex > 0;
    }

    public bool TryGetUserIdBySlot(int slotIndex, out string userId)
    {
        if (slotIndex <= 0)
        {
            userId = string.Empty;
            return false;
        }

        if (_userIdBySlot.TryGetValue(slotIndex, out userId))
            return !string.IsNullOrWhiteSpace(userId);

        userId = string.Empty;
        return false;
    }

    public IReadOnlyCollection<HostParticipantSlot> GetAll()
    {
        return _slotByUserId.Values;
    }
}

public sealed class HostParticipantSlot
{
    public string UserId;
    public string UserName;
    public int SlotIndex;
    public DateTime ApprovedAtUtc;
}

