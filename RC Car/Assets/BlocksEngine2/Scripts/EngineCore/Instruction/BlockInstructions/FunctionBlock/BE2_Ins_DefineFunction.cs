using UnityEngine.Events;

using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.Utils;
using TMPro;

namespace MG_BlocksEngine2.Block.Instruction
{
    // v2.12 - new DefineFunction instruction
    public class BE2_Ins_DefineFunction : BE2_InstructionBase, I_BE2_Instruction
    {
        protected override void OnAwake()
        {
            BE2_MainEventsManager.Instance.TriggerEvent(BE2EventTypesBlock.OnFunctionDefinitionAdded, Block);
            string fromHeader = BuildDefineIdFromHeader();
            defineID = string.IsNullOrEmpty(fromHeader) ? System.Guid.NewGuid().ToString() : fromHeader;
        }

        // v2.12.1 - make sure the inputs are updated on Define Block start
        protected override void OnStart()
        {
            Block.Layout.SectionsArray[0].Header.UpdateInputsArray();
        }

        public string defineID;

        public UnityEvent onDefineChange = new UnityEvent();

        protected override void OnEnableInstruction()
        {
            BE2_MainEventsManager.Instance.StartListening(BE2EventTypesBlock.OnDropAtStack, HandleDefineChange);
            BE2_MainEventsManager.Instance.StartListening(BE2EventTypesBlock.OnDropAtInputSpot, HandleDefineChange);
            BE2_MainEventsManager.Instance.StartListening(BE2EventTypesBlock.OnDragFromStack, HandleDefineChange);
            BE2_MainEventsManager.Instance.StartListening(BE2EventTypesBlock.OnDragFromInputSpot, HandleDefineChange);
        }

        protected override void OnDisableInstruction()
        {
            BE2_MainEventsManager.Instance.StopListening(BE2EventTypesBlock.OnDropAtStack, HandleDefineChange);
            BE2_MainEventsManager.Instance.StopListening(BE2EventTypesBlock.OnDropAtInputSpot, HandleDefineChange);
            BE2_MainEventsManager.Instance.StopListening(BE2EventTypesBlock.OnDragFromStack, HandleDefineChange);
            BE2_MainEventsManager.Instance.StopListening(BE2EventTypesBlock.OnDragFromInputSpot, HandleDefineChange);
        }

        public void HandleDefineChange(I_BE2_Block block)
        {
            BE2_Ins_DefineFunction defineInstruction = block.ParentSection.RectTransform.GetComponentInParent<BE2_Ins_DefineFunction>();
            if (defineInstruction == this)
            {
                onDefineChange.Invoke();
            }
        }

        public int GetLocalVariableIndex(string varName)
        {
            int index = -1;

            I_BE2_BlockSectionHeaderInput[] inputs = Block.Layout.SectionsArray[0].Header.InputsArray;
            int inputsLength = inputs.Length;
            for (int i = 0; i < inputsLength; i++)
            {
                if (inputs[i].Transform.GetComponentInChildren<TMP_Text>().text == varName)
                    return i;
            }

            return index;
        }

        // public new void Function()
        // {

        // }

        void OnDestroy()
        {
            BE2_MainEventsManager.Instance.TriggerEvent(BE2EventTypesBlock.OnFunctionDefinitionRemoved, Block);
            BE2_BlockUtils.RemoveBlock(Block);
        }

        string BuildDefineIdFromHeader()
        {
            if (Block == null || Block.Layout == null || Block.Layout.SectionsArray == null || Block.Layout.SectionsArray.Length == 0)
                return string.Empty;
            var header = Block.Layout.SectionsArray[0].Header;
            if (header == null) return string.Empty;
            header.UpdateItemsArray();
            System.Collections.Generic.List<string> labels = new System.Collections.Generic.List<string>();
            var items = header.ItemsArray;
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null) continue;
                    var isLabel = item.Transform.GetComponentInChildren<MG_BlocksEngine2.UI.FunctionBlock.Label>() != null;
                    if (isLabel)
                    {
                        var t = item.Transform.GetComponentInChildren<TMP_Text>();
                        if (t != null && !string.IsNullOrEmpty(t.text))
                        {
                            labels.Add(SanitizeName(t.text));
                        }
                    }
                }
            }
            if (labels.Count > 0)
            {
                return string.Join("_", labels.ToArray());
            }
            return string.Empty;
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