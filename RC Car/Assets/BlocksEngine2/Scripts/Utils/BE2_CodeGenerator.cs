using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.Block.Instruction;
using MG_BlocksEngine2.Environment;
using MG_BlocksEngine2.Core;

namespace MG_BlocksEngine2.Utils
{
    public static class BE2_CodeGenerator
    {
        public static string GenerateCode(I_BE2_ProgrammingEnv env)
        {
            StringBuilder sb = new StringBuilder();
            env.UpdateBlocksList();

            foreach (var block in env.BlocksList)
            {
                if (block.Type == BlockTypeEnum.trigger)
                {
                    GenerateTrigger(block, sb);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static void GenerateTrigger(I_BE2_Block block, StringBuilder sb)
        {
            string name = GetCSharpName(block);
            sb.AppendLine($"void {name}()");
            sb.AppendLine("{");
            
            // Triggers usually have the code in the first section's body (or the only body available)
            TraverseSections(block, sb, 1);
            
            sb.AppendLine("}");
        }

        private static void TraverseSections(I_BE2_Block block, StringBuilder sb, int indent)
        {
            foreach (var section in block.Layout.SectionsArray)
            {
                if (section.Body != null)
                {
                    section.Body.UpdateChildBlocksList();
                    foreach (var child in section.Body.ChildBlocksArray)
                    {
                        GenerateStatement(child, sb, indent);
                    }
                }
            }
        }

        private static void GenerateStatement(I_BE2_Block block, StringBuilder sb, int indent)
        {
            string indentation = new string(' ', indent * 4);
            string typeName = block.Instruction.GetType().Name;

            if (typeName == "BE2_Ins_If")
            {
                string condition = GetInputExpression(block.Layout.SectionsArray[0].Header.InputsArray[0]);
                sb.AppendLine($"{indentation}if ({condition})");
                sb.AppendLine($"{indentation}{{");
                TraverseSectionBody(block.Layout.SectionsArray[0], sb, indent + 1);
                sb.AppendLine($"{indentation}}}");
            }
            else if (typeName == "BE2_Ins_IfElse")
            {
                // If part
                string condition = GetInputExpression(block.Layout.SectionsArray[0].Header.InputsArray[0]);
                sb.AppendLine($"{indentation}if ({condition})");
                sb.AppendLine($"{indentation}{{");
                TraverseSectionBody(block.Layout.SectionsArray[0], sb, indent + 1);
                sb.AppendLine($"{indentation}}}");
                
                // Else part
                sb.AppendLine($"{indentation}else");
                sb.AppendLine($"{indentation}{{");
                TraverseSectionBody(block.Layout.SectionsArray[1], sb, indent + 1);
                sb.AppendLine($"{indentation}}}");
            }
            else if (typeName == "BE2_Ins_Repeat")
            {
                string count = GetInputExpression(block.Layout.SectionsArray[0].Header.InputsArray[0]);
                sb.AppendLine($"{indentation}for (int i = 0; i < {count}; i++)");
                sb.AppendLine($"{indentation}{{");
                TraverseSectionBody(block.Layout.SectionsArray[0], sb, indent + 1);
                sb.AppendLine($"{indentation}}}");
            }
            else if (typeName == "BE2_Ins_RepeatForever")
            {
                sb.AppendLine($"{indentation}while (true)");
                sb.AppendLine($"{indentation}{{");
                TraverseSectionBody(block.Layout.SectionsArray[0], sb, indent + 1);
                sb.AppendLine($"{indentation}}}");
            }
            else if (typeName == "BE2_Ins_RepeatUntil")
            {
                string condition = GetInputExpression(block.Layout.SectionsArray[0].Header.InputsArray[0]);
                sb.AppendLine($"{indentation}while (!({condition}))");
                sb.AppendLine($"{indentation}{{");
                TraverseSectionBody(block.Layout.SectionsArray[0], sb, indent + 1);
                sb.AppendLine($"{indentation}}}");
            }
            else
            {
                // Standard function call
                string funcName = GetCSharpName(block);
                List<string> args = new List<string>();
                
                // Collect inputs from all sections (usually just section 0 for simple blocks)
                foreach (var section in block.Layout.SectionsArray)
                {
                    foreach (var input in section.Header.InputsArray)
                    {
                        args.Add(GetInputExpression(input));
                    }
                }
                
                sb.AppendLine($"{indentation}{funcName}({string.Join(", ", args)});");
            }
        }

        private static void TraverseSectionBody(I_BE2_BlockSection section, StringBuilder sb, int indent)
        {
            if (section.Body != null)
            {
                section.Body.UpdateChildBlocksList();
                foreach (var child in section.Body.ChildBlocksArray)
                {
                    GenerateStatement(child, sb, indent);
                }
            }
        }

        private static string GetInputExpression(I_BE2_BlockSectionHeaderInput input)
        {
            I_BE2_Block operationBlock = input.Transform.GetComponent<I_BE2_Block>(); // Check if an operation block is attached
            // Wait, inputs usually have the operation block as a child or attached?
            // BE2_BlocksSerializer checks: input.Transform.GetComponent<I_BE2_Block>()
            // But inputs are usually Spots.
            // Let's check BE2_BlocksSerializer again.
            // I_BE2_Block inputBlock = input.Transform.GetComponent<I_BE2_Block>();
            // This implies the input object ITSELF is the block? No, that's unlikely.
            // The input is a Spot. The block is dropped ON the spot.
            // But BE2_BlocksSerializer says: input.Transform.GetComponent<I_BE2_Block>()
            // Maybe the block becomes a component of the input spot?
            // Or maybe it's a child?
            
            // Let's assume the operation block is a child of the input transform.
            if (input.Transform.childCount > 0)
            {
                I_BE2_Block opBlock = input.Transform.GetComponentInChildren<I_BE2_Block>();
                if (opBlock != null)
                {
                    return GenerateOperation(opBlock);
                }
            }

            // If no block, return the value
            // Check if string value needs quotes
            // For now, return as is, or try to parse.
            // If it looks like a number, return number. Else quotes.
            string val = input.InputValues.stringValue;
            if (float.TryParse(val, out _)) return val;
            if (val == "true" || val == "false") return val;
            return $"\"{val}\"";
        }

        private static string GenerateOperation(I_BE2_Block block)
        {
            string typeName = block.Instruction.GetType().Name;
            
            // Basic operations
            // This is a simplified mapping. Real mapping would need to check specific classes.
            // Assuming standard naming or just using function calls for ops too.
            
            // If it's a known operator
            // BE2_Ins_Add, BE2_Ins_Subtract... (Check actual names)
            // I'll use a generic function call format for operations too, unless I know it's an operator.
            
            string funcName = GetCSharpName(block);
            List<string> args = new List<string>();
            foreach (var section in block.Layout.SectionsArray)
            {
                foreach (var input in section.Header.InputsArray)
                {
                    args.Add(GetInputExpression(input));
                }
            }

            // Map common operators
            if (funcName == "Add") return $"({args[0]} + {args[1]})";
            if (funcName == "Subtract") return $"({args[0]} - {args[1]})";
            if (funcName == "Multiply") return $"({args[0]} * {args[1]})";
            if (funcName == "Divide") return $"({args[0]} / {args[1]})";
            if (funcName == "Greater") return $"({args[0]} > {args[1]})";
            if (funcName == "Less") return $"({args[0]} < {args[1]})";
            if (funcName == "Equal") return $"({args[0]} == {args[1]})";
            
            return $"{funcName}({string.Join(", ", args)})";
        }

        private static string GetCSharpName(I_BE2_Block block)
        {
            // Use the Instruction class name, removing "BE2_Ins_" or "BE2_Cst_" prefix
            string name = block.Instruction.GetType().Name;
            name = name.Replace("BE2_Ins_", "").Replace("BE2_Cst_", "").Replace("BE2_Op_", "");
            return name;
        }
    }
}
