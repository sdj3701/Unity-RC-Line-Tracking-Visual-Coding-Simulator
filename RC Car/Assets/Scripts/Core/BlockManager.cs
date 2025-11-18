using UnityEngine;

public class BlockManager : MonoBehaviour
{
    public BlockView StartBlock;

    private BlockInterpreter interpreter = new();

    [ContextMenu("Run Blocks")]
    public void RunBlocks()
    {
        if (StartBlock == null) return;
        interpreter.Execute(StartBlock.GetNode());
    }
}
