// using System.Globalization;
// using MG_BlocksEngine2.Environment; // BE2_VariablesManager

// namespace MG_BlocksEngine2.Block.Instruction
// {
//     public class BE2_Custom_Pin_pWM : BE2_InstructionBase, I_BE2_Instruction
//     {
//         I_BE2_BlockSectionHeaderInput _input0;
//         I_BE2_BlockSectionHeaderInput _input1;
//         float _value0;
//         float _value1;

//         private float ReadFloat(I_BE2_BlockSectionHeaderInput inp)
//         {
//             var v = inp.InputValues;
//             if (!v.isText) return v.floatValue; // 숫자

//             var s = v.stringValue;              // 텍스트
//             if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
//                 return parsed;                  // 숫자 리터럴 텍스트

//             return BE2_VariablesManager.instance.GetVariableFloatValue(s); // 변수명으로 조회
//         }

//         public new void Function()
//         {
//             var inputs = Section0Inputs;
//             if (inputs == null || inputs.Length < 2) { ExecuteNextInstruction(); return; }

//             _input0 = inputs[0];
//             _input1 = inputs[1];

//             _value0 = ReadFloat(_input0);
//             _value1 = ReadFloat(_input1);

//             // 테스트용 이동이라면:
//             TargetObject.Transform.position += TargetObject.Transform.forward * _value0;
//             TargetObject.Transform.position += TargetObject.Transform.forward * _value1;

//             ExecuteNextInstruction();
//         }
//     }
// }

