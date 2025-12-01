using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.EditorScript;
using TMPro;
using System.Linq;
using MG_BlocksEngine2.Utils;
using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Block.Instruction;

namespace MG_BlocksEngine2.Environment
{
    // v2.12 - added Function Blocks manager component
    public class BE2_VerticalFunctionBlocksManager : BE2_FunctionBlocksManager
    {
        public override void CreateFunctionBlock(List<Serializer.DefineItem> items)
        {
            I_BE2_ProgrammingEnv programmingEnv = BE2_ExecutionManager.Instance.ProgrammingEnvsList.Find(x => x.Visible == true);

            BE2_Ins_DefineFunction templateDefine = DefineFunctionBlockTemplate.GetComponent<BE2_Ins_DefineFunction>();

            BE2_Ins_DefineFunction defineBlockIns = Instantiate<BE2_Ins_DefineFunction>(templateDefine, Vector3.zero, Quaternion.identity, programmingEnv.Transform);
            I_BE2_BlockLayout layoutDefine = defineBlockIns.GetComponent<I_BE2_BlockLayout>();

            defineBlockIns.name = templateDefine.name;

            // v2.12.1 - bugfix: instantiation position of Define Block not relative to ProgrammingEnv
            defineBlockIns.transform.localPosition = positionToInstantiate;

            List<string> alreadyUsedVariableNames = new List<string>();
            foreach (Serializer.DefineItem item in items)
            {
                if (item.type == "label")
                {
                    GameObject labelDefine = Instantiate(BE2_Inspector.Instance.LabelTextTemplate, Vector3.zero, Quaternion.identity,
                                                    layoutDefine.SectionsArray[0].Header.RectTransform);
                    labelDefine.GetComponentInChildren<TMP_Text>().text = item.value;

                }
                else
                {
                    GameObject inputDefine = Instantiate(templateDefineLocalVariable, Vector3.zero, Quaternion.identity,
                                                    layoutDefine.SectionsArray[0].Header.RectTransform);

                    string variableName = item.value;
                    int variableNameCount = alreadyUsedVariableNames.Where(s => s == variableName).Count();
                    alreadyUsedVariableNames.Add(variableName);
                    if (variableNameCount > 0)
                    {
                        variableName += " (" + variableNameCount + ")";
                    }

                    inputDefine.GetComponentInChildren<TMP_Text>().text = variableName;
                }
            }

            BE2_BlockUtils.UnloadPrefab();

            // Set defineID using label items (user-entered text), joined by '_'
            string builtId = BuildDefineIdFromItems(items);
            if (!string.IsNullOrEmpty(builtId))
            {
                defineBlockIns.defineID = builtId;
                defineBlockIns.onDefineChange?.Invoke();
            }

            CreateSelectionFunction(items, defineBlockIns);

        }

        public override void CreateSelectionFunction(List<Serializer.DefineItem> items, BE2_Ins_DefineFunction defineBlockIns)
        {
            // v2.12 - bugfix: variables and function blocks not being loaded if the corresponding selectino blocks was not active
            bool panelIsActive = functionBlocksPanel.gameObject.activeSelf;
            functionBlocksPanel.gameObject.SetActive(true);

            GameObject selectionFunctionBlock = Instantiate(templateSelectionBlock, Vector3.zero, Quaternion.identity, functionBlocksPanel);
            I_BE2_BlockLayout selectionLayout = selectionFunctionBlock.GetComponent<I_BE2_BlockLayout>();

            foreach (Serializer.DefineItem item in items)
            {
                if (item.type == "label")
                {
                    GameObject label = Instantiate(BE2_Inspector.Instance.LabelTextTemplate, Vector3.zero, Quaternion.identity,
                                                   selectionLayout.SectionsArray[0].Header.RectTransform);
                    label.GetComponent<TMP_Text>().text = item.value;
                }
                else
                {
                    GameObject input = Instantiate(BE2_Inspector.Instance.InputFieldTemplate, Vector3.zero, Quaternion.identity,
                                                    selectionLayout.SectionsArray[0].Header.RectTransform);
                    input.GetComponent<TMP_InputField>().text = "";

                    BE2_BlockUtils.RemoveEngineComponents(input.transform);

                    input.AddComponent<BE2_BlockSectionHeader_InputField>();
                }
            }

            selectionLayout.UpdateLayout();
            selectionFunctionBlock.GetComponent<BE2_DragSelectionFunction>().defineFunctionInstruction = defineBlockIns;

            functionBlocksPanel.gameObject.SetActive(panelIsActive);
        }

        string BuildDefineIdFromItems(List<Serializer.DefineItem> items)
        {
            if (items == null) return string.Empty;
            var labelParts = new List<string>();
            foreach (var it in items)
            {
                if (it != null && it.type == "label" && !string.IsNullOrEmpty(it.value))
                {
                    labelParts.Add(SanitizeName(it.value));
                }
            }
            if (labelParts.Count == 0) return string.Empty;
            return string.Join("_", labelParts.ToArray());
        }

        string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            if (!(char.IsLetter(raw[0]) || raw[0] == '_')) sb.Append('_');
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }
            return sb.ToString();
        }
    }
}
