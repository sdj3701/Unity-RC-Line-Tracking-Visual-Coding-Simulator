using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BlockInput
{
    public string Name;
    public string Type; // "number", "string", "boolean"
    public string Value;
}

[Serializable]
public class BlockNode
{
    public string Id;
    public string Type;
    public List<BlockInput> Inputs = new();
    public List<BlockNode> NextBlocks = new();
}
