using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BlockView : MonoBehaviour
{
    public BlockDefinition Definition;
    public TMP_InputField[] InputFields;
    public BlockView NextBlock;
    private BlockNode node;

    void Start()
    {
        node = new BlockNode();
        node.Type = Definition.BlockType;
        foreach (var name in Definition.InputNames)
            node.Inputs.Add(new BlockInput { Name = name, Type = "number", Value = "0" });

        for (int i = 0; i < InputFields.Length; i++)
        {
            int index = i;
            InputFields[i].onValueChanged.AddListener((val) =>
            {
                node.Inputs[index].Value = val;
            });
        }
    }

    public BlockNode GetNode() => node;
}
