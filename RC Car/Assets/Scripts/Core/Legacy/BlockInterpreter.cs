using UnityEngine;

public class BlockInterpreter
{
    public void Execute(BlockNode node)
    {
        if (node == null) return;

        switch (node.Type)
        {
            case "Move":
                float distance = float.Parse(node.Inputs.Find(i => i.Name == "distance").Value);
                PlayerController.Instance.Move(distance);
                break;

            case "Rotate":
                float angle = float.Parse(node.Inputs.Find(i => i.Name == "angle").Value);
                PlayerController.Instance.Rotate(angle);
                break;
        }

        foreach (var next in node.NextBlocks)
            Execute(next);
    }
}
