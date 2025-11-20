using System.Globalization;
using MG_BlocksEngine2.Environment; // BE2_VariablesManager


namespace MG_BlocksEngine2.Block.Instruction
{
    public class BE2_Custom_Digital_Pin_Read : BE2_InstructionBase, I_BE2_Instruction
    {
        I_BE2_BlockSectionHeaderInput _input;
        float _value;

        private float ReadFloat(I_BE2_BlockSectionHeaderInput inp)
        {
            var v = inp.InputValues;
            if (!v.isText) return v.floatValue; // 숫자

            var s = v.stringValue;              // 텍스트
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;                  // 숫자 리터럴 텍스트

            return BE2_VariablesManager.instance.GetVariableFloatValue(s); // 변수명으로 조회
        }

        public new void Function()
        {
            var inputs = Section0Inputs;
            if (inputs == null || inputs.Length < 2) { ExecuteNextInstruction(); return; }

            _input = inputs[0];

            _value = ReadFloat(_input);
            TargetObject.Transform.position += TargetObject.Transform.forward * _value;

            ExecuteNextInstruction();
        }
    }
}
