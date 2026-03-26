using System.Collections.Generic;
using UnityEngine;

public sealed class HostRuntimeBinder
{
    public bool TryApplyJson(BlockCodeExecutor executor, string json, string sourceTag, out string error)
    {
        error = string.Empty;

        if (executor == null)
        {
            error = "executor is null";
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "json is empty";
            return false;
        }

        bool loaded = executor.LoadProgramFromJson(json, sourceTag);
        if (!loaded)
        {
            error = "LoadProgramFromJson failed";
            return false;
        }

        return true;
    }

    public void StartCar(VirtualCarPhysics physics)
    {
        if (physics == null)
            return;

        physics.StartRunning();
    }

    public void StopCar(VirtualCarPhysics physics)
    {
        if (physics == null)
            return;

        physics.StopRunning();
    }

    public void StopAll(HostCarBindingStore store)
    {
        if (store == null)
            return;

        foreach (HostCarBinding binding in store.GetAllOrderedBySlot())
        {
            if (binding == null || binding.RuntimeRefs == null)
                continue;

            StopCar(binding.RuntimeRefs.Physics);
        }
    }
}

