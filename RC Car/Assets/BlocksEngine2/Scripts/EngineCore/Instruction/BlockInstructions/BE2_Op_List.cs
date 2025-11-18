using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Attribute;

namespace MG_BlocksEngine2.Block.Instruction
{
    // v2.9 - new block
    [SerializeAsVariable(typeof(BE2_VariablesListManager))]
    public class BE2_Op_List : BE2_InstructionBase, I_BE2_Instruction
    {
        protected override void OnStart()
        {
            _variablesManager = BE2_VariablesListManager.instance;
        }

        BE2_VariablesListManager _variablesManager;

        public new string Operation()
        {
            return _variablesManager.GetListStringValue(Section0Inputs[0].StringValue, 0);
        }
    }
}