using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBlock", menuName = "Block/Definition")]
public class BlockDefinition : ScriptableObject
{
    public string BlockType;
    public Sprite Icon;
    public List<string> InputNames;
}
