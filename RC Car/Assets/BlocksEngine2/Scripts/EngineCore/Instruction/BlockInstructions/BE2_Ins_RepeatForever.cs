
namespace MG_BlocksEngine2.Block.Instruction
{
    public class BE2_Ins_RepeatForever : BE2_InstructionBase, I_BE2_Instruction
    {
        //protected override void OnAwake()
        //{
        //
        //}

        //protected override void OnStart()
        //{
        //    
        //}

        public new void Function()
        {
            ExecuteSection(0);
        }
    }
}