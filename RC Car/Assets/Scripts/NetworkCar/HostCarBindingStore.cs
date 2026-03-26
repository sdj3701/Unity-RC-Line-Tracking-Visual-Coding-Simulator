using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class HostCarBindingStore
{
    private readonly Dictionary<string, HostCarBinding> _bindingByUserId =
        new Dictionary<string, HostCarBinding>(StringComparer.Ordinal);

    public int Count => _bindingByUserId.Count;

    public int CountMappedCode()
    {
        int count = 0;
        foreach (var pair in _bindingByUserId)
        {
            HostCarBinding binding = pair.Value;
            if (binding != null && binding.HasCode)
                count++;
        }

        return count;
    }

    public HostCarBinding UpsertParticipant(HostParticipantSlot slot)
    {
        if (slot == null || string.IsNullOrWhiteSpace(slot.UserId))
            return null;

        if (!_bindingByUserId.TryGetValue(slot.UserId, out HostCarBinding binding) || binding == null)
        {
            binding = new HostCarBinding();
            _bindingByUserId[slot.UserId] = binding;
        }

        binding.UserId = slot.UserId;
        binding.UserName = slot.UserName;
        binding.SlotIndex = slot.SlotIndex;
        binding.LastUpdatedUtc = DateTime.UtcNow;
        return binding;
    }

    public HostCarBinding UpsertRuntimeRefs(string userIdRaw, HostCarRuntimeRefs refs, Color assignedColor)
    {
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        if (!_bindingByUserId.TryGetValue(userId, out HostCarBinding binding) || binding == null)
        {
            binding = new HostCarBinding
            {
                UserId = userId,
                UserName = userId
            };
            _bindingByUserId[userId] = binding;
        }

        binding.RuntimeRefs = refs;
        binding.AssignedColor = assignedColor;
        binding.LastUpdatedUtc = DateTime.UtcNow;
        return binding;
    }

    public HostCarBinding UpsertCode(ResolvedCodePayload payload)
    {
        if (payload == null || string.IsNullOrWhiteSpace(payload.UserId))
            return null;

        if (!_bindingByUserId.TryGetValue(payload.UserId, out HostCarBinding binding) || binding == null)
        {
            binding = new HostCarBinding
            {
                UserId = payload.UserId,
                UserName = payload.UserId
            };
            _bindingByUserId[payload.UserId] = binding;
        }

        binding.LatestShareId = payload.ShareId;
        binding.LatestSavedSeq = payload.SavedSeq;
        binding.Xml = payload.Xml;
        binding.Json = payload.Json;
        binding.HasCode = !string.IsNullOrWhiteSpace(binding.Json);
        binding.LastError = payload.IsSuccess ? string.Empty : payload.Error;
        binding.RuntimeReady = false;
        binding.LastUpdatedUtc = DateTime.UtcNow;
        return binding;
    }

    public bool TryGetBinding(string userIdRaw, out HostCarBinding binding)
    {
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            binding = null;
            return false;
        }

        return _bindingByUserId.TryGetValue(userId, out binding) && binding != null;
    }

    public bool TryGetBindingBySlot(int slotIndex, out HostCarBinding binding)
    {
        foreach (var pair in _bindingByUserId)
        {
            HostCarBinding candidate = pair.Value;
            if (candidate == null)
                continue;

            if (candidate.SlotIndex == slotIndex)
            {
                binding = candidate;
                return true;
            }
        }

        binding = null;
        return false;
    }

    public IEnumerable<HostCarBinding> GetAllOrderedBySlot()
    {
        var list = new List<HostCarBinding>(_bindingByUserId.Values);
        list.Sort((left, right) =>
        {
            int l = left != null ? left.SlotIndex : int.MaxValue;
            int r = right != null ? right.SlotIndex : int.MaxValue;
            return l.CompareTo(r);
        });

        return list;
    }
}

public sealed class HostCarRuntimeRefs
{
    public GameObject CarObject;
    public VirtualCarPhysics Physics;
    public BlockCodeExecutor Executor;
    public VirtualArduinoMicro Arduino;
}

public sealed class HostCarBinding
{
    public string UserId;
    public string UserName;
    public int SlotIndex;
    public string LatestShareId;
    public int LatestSavedSeq;
    public string Xml;
    public string Json;
    public bool HasCode;
    public Color AssignedColor;
    public HostCarRuntimeRefs RuntimeRefs;
    public bool RuntimeReady;
    public string LastError;
    public DateTime LastUpdatedUtc;
}

