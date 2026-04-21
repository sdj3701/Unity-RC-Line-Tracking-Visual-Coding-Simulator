using Fusion;
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

    public int CountRuntimeCars()
    {
        int count = 0;
        foreach (var pair in _bindingByUserId)
        {
            HostCarBinding binding = pair.Value;
            if (binding != null && binding.RuntimeRefs != null && binding.RuntimeRefs.CarObject != null)
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

        if (!ReferenceEquals(binding.RuntimeRefs, refs))
            binding.RuntimeReady = false;

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

        string previousActiveKey = Normalize(binding.ActiveVersionKey);
        HostCodeVersion version = binding.AddOrUpdateCodeVersion(payload, out bool isNewVersion, out bool contentChanged);
        if (version == null)
            return null;

        binding.ActiveVersionKey = Normalize(version.VersionKey);
        binding.SyncActiveCodeFields(version);
        binding.LastError = payload.IsSuccess ? string.Empty : payload.Error;

        bool activeChanged = !string.Equals(previousActiveKey, binding.ActiveVersionKey, StringComparison.Ordinal);
        if (isNewVersion || contentChanged || activeChanged)
            binding.RuntimeReady = false;

        binding.LastUpdatedUtc = DateTime.UtcNow;
        return binding;
    }

    public bool TryHasCodeVersion(string userIdRaw, string shareIdRaw, int savedSeq)
    {
        if (!TryGetBinding(userIdRaw, out HostCarBinding binding) || binding == null)
            return false;

        string versionKey = BuildVersionKey(shareIdRaw, savedSeq);
        if (string.IsNullOrWhiteSpace(versionKey))
            return false;

        return binding.TryGetCodeVersionByKey(versionKey, out _);
    }

    public bool TryActivateExistingCodeVersion(
        string userIdRaw,
        string shareIdRaw,
        int savedSeq,
        out HostCarBinding binding,
        out HostCodeVersion version)
    {
        binding = null;
        version = null;

        if (!TryGetBinding(userIdRaw, out HostCarBinding found) || found == null)
            return false;

        string versionKey = BuildVersionKey(shareIdRaw, savedSeq);
        if (string.IsNullOrWhiteSpace(versionKey))
            return false;

        if (!found.TryGetCodeVersionByKey(versionKey, out HostCodeVersion foundVersion) || foundVersion == null)
            return false;

        found.ActiveVersionKey = versionKey;
        found.SyncActiveCodeFields(foundVersion);
        found.LastUpdatedUtc = DateTime.UtcNow;

        binding = found;
        version = foundVersion;
        return true;
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

    public static string BuildVersionKey(string shareIdRaw, int savedSeq)
    {
        string shareId = Normalize(shareIdRaw);
        if (string.IsNullOrWhiteSpace(shareId) || savedSeq <= 0)
            return string.Empty;

        return $"{shareId}:{savedSeq}";
    }

    private static string Normalize(string raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
    }
}

public sealed class HostCarRuntimeRefs
{
    public GameObject CarObject;
    public NetworkObject NetworkObject;
    public NetworkRCCar NetworkCar;
    public VirtualCarPhysics Physics;
    public BlockCodeExecutor Executor;
    public VirtualArduinoMicro Arduino;
    public PlayerRef OwnerPlayer;
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
    public string ActiveVersionKey;
    public List<HostCodeVersion> CodeVersions = new List<HostCodeVersion>();

    public bool TryGetActiveCodeVersion(out HostCodeVersion version)
    {
        version = null;

        if (CodeVersions == null || CodeVersions.Count <= 0)
            return false;

        string activeKey = string.IsNullOrWhiteSpace(ActiveVersionKey) ? string.Empty : ActiveVersionKey.Trim();
        if (!string.IsNullOrWhiteSpace(activeKey))
        {
            for (int i = 0; i < CodeVersions.Count; i++)
            {
                HostCodeVersion candidate = CodeVersions[i];
                if (candidate == null)
                    continue;

                if (string.Equals(activeKey, candidate.VersionKey, StringComparison.Ordinal))
                {
                    version = candidate;
                    return true;
                }
            }
        }

        for (int i = CodeVersions.Count - 1; i >= 0; i--)
        {
            HostCodeVersion candidate = CodeVersions[i];
            if (candidate == null)
                continue;

            version = candidate;
            ActiveVersionKey = candidate.VersionKey;
            return true;
        }

        return false;
    }

    public bool TryGetCodeVersionByKey(string versionKeyRaw, out HostCodeVersion version)
    {
        version = null;

        if (CodeVersions == null || CodeVersions.Count <= 0)
            return false;

        string versionKey = string.IsNullOrWhiteSpace(versionKeyRaw) ? string.Empty : versionKeyRaw.Trim();
        if (string.IsNullOrWhiteSpace(versionKey))
            return false;

        for (int i = 0; i < CodeVersions.Count; i++)
        {
            HostCodeVersion candidate = CodeVersions[i];
            if (candidate == null)
                continue;

            if (string.Equals(versionKey, candidate.VersionKey, StringComparison.Ordinal))
            {
                version = candidate;
                return true;
            }
        }

        return false;
    }

    public HostCodeVersion AddOrUpdateCodeVersion(
        ResolvedCodePayload payload,
        out bool isNewVersion,
        out bool contentChanged)
    {
        isNewVersion = false;
        contentChanged = false;

        if (payload == null)
            return null;

        if (CodeVersions == null)
            CodeVersions = new List<HostCodeVersion>();

        string shareId = string.IsNullOrWhiteSpace(payload.ShareId) ? string.Empty : payload.ShareId.Trim();
        string xml = string.IsNullOrWhiteSpace(payload.Xml) ? string.Empty : payload.Xml;
        string json = string.IsNullOrWhiteSpace(payload.Json) ? string.Empty : payload.Json;
        string versionKey = HostCarBindingStore.BuildVersionKey(shareId, payload.SavedSeq);
        if (string.IsNullOrWhiteSpace(versionKey))
            return null;

        string nextHash = ComputeStableHash(json);

        for (int i = 0; i < CodeVersions.Count; i++)
        {
            HostCodeVersion existing = CodeVersions[i];
            if (existing == null)
                continue;

            if (!string.Equals(existing.VersionKey, versionKey, StringComparison.Ordinal))
                continue;

            contentChanged =
                !string.Equals(existing.JsonHash, nextHash, StringComparison.Ordinal) ||
                !string.Equals(existing.Xml, xml, StringComparison.Ordinal);

            existing.ShareId = shareId;
            existing.SavedSeq = payload.SavedSeq;
            existing.Xml = xml;
            existing.Json = json;
            existing.JsonHash = nextHash;
            existing.ResolvedAtUtc = payload.ResolvedAtUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            return existing;
        }

        var created = new HostCodeVersion
        {
            VersionKey = versionKey,
            ShareId = shareId,
            SavedSeq = payload.SavedSeq,
            Xml = xml,
            Json = json,
            JsonHash = nextHash,
            ResolvedAtUtc = payload.ResolvedAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        CodeVersions.Add(created);
        isNewVersion = true;
        contentChanged = true;
        return created;
    }

    public void SyncActiveCodeFields(HostCodeVersion active)
    {
        if (active == null)
        {
            LatestShareId = string.Empty;
            LatestSavedSeq = 0;
            Xml = string.Empty;
            Json = string.Empty;
            HasCode = false;
            return;
        }

        LatestShareId = string.IsNullOrWhiteSpace(active.ShareId) ? string.Empty : active.ShareId.Trim();
        LatestSavedSeq = active.SavedSeq;
        Xml = string.IsNullOrWhiteSpace(active.Xml) ? string.Empty : active.Xml;
        Json = string.IsNullOrWhiteSpace(active.Json) ? string.Empty : active.Json;
        HasCode = !string.IsNullOrWhiteSpace(Json);
    }

    private static string ComputeStableHash(string value)
    {
        string text = string.IsNullOrEmpty(value) ? string.Empty : value;
        unchecked
        {
            const uint fnvOffset = 2166136261;
            const uint fnvPrime = 16777619;
            uint hash = fnvOffset;

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= fnvPrime;
            }

            return hash.ToString("X8");
        }
    }
}

public sealed class HostCodeVersion
{
    public string VersionKey;
    public string ShareId;
    public int SavedSeq;
    public string Xml;
    public string Json;
    public string JsonHash;
    public DateTime ResolvedAtUtc;
    public DateTime CreatedAtUtc;
    public DateTime UpdatedAtUtc;
}

