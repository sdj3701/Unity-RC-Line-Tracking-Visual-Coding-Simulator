using UnityEngine;

public sealed class HostCarColorAllocator
{
    // Red -> Orange -> Yellow -> Green -> Cyan -> Blue -> Magenta
    private static readonly Color[] Palette =
    {
        new Color(1f, 0.2f, 0.2f, 1f),
        new Color(1f, 0.5f, 0.1f, 1f),
        new Color(1f, 0.9f, 0.2f, 1f),
        new Color(0.2f, 0.95f, 0.3f, 1f),
        new Color(0.2f, 0.9f, 1f, 1f),
        new Color(0.25f, 0.45f, 1f, 1f),
        new Color(0.9f, 0.35f, 1f, 1f)
    };

    public Color Resolve(int slotIndex)
    {
        if (Palette.Length == 0)
            return Color.white;

        int normalized = Mathf.Max(1, slotIndex) - 1;
        return Palette[normalized % Palette.Length];
    }
}

