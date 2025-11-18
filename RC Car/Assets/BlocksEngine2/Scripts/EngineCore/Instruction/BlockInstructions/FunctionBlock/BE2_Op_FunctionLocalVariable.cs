using TMPro;

namespace MG_BlocksEngine2.Block.Instruction
{
    // v2.12 - new FunctionLocalVariable instruction
    public class BE2_Op_FunctionLocalVariable : BE2_InstructionBase, I_BE2_Instruction
    {
        public BE2_Ins_DefineFunction defineInstruction;
        public BE2_Block blockToObserve;
        TMP_Text _text;

        protected override void OnAwake()
        {
            _text = GetComponentInChildren<TMP_Text>();
            if (_text)
                varName = _text.text;
        }

        public string varName = "";

        public new string Operation()
        {
            if (defineInstruction)
            {
                int index = defineInstruction.GetLocalVariableIndex(varName);
                string value = (blockToObserve.Instruction as BE2_Ins_FunctionBlock).localValues[index];
                return value;
            }

            return "";
        }
    }
}