using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using MG_BlocksEngine2.DragDrop;
using MG_BlocksEngine2.Utils;

namespace MG_BlocksEngine2.Block
{
    // v2.13 - Added class OuterArea to enable drop under non trigger blocks and dragging blocks as a group 
    public abstract class BE2_OuterArea
    {
        public BE2_OuterArea(Transform transform)
        {
            this.Transform = transform;
            _rectTransform = transform as RectTransform;
            spotOuterArea = transform.GetComponent<BE2_SpotOuterArea>();

            childBlocksArray = new I_BE2_Block[0];

            InitializeLayoutGroup();
        }

        public Transform Transform;
        public RectTransform _rectTransform;
        public I_BE2_Spot spotOuterArea;
        public int childBlocksCount;
        public I_BE2_Block[] childBlocksArray;

        protected virtual void InitializeLayoutGroup()
        {

        }

        public virtual Vector2 GetTopDropPosition(I_BE2_Block foundBlock)
        {
            return foundBlock.Transform.localPosition + new Vector3(0, (BE2_DragDropManager.Instance.GhostBlockTransform as RectTransform).sizeDelta.y - 10, 0);
        }

        public void UpdateChildBlocksList()
        {
            childBlocksArray = new I_BE2_Block[0];
            int childCount = Transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                I_BE2_Block childBlock = Transform.GetChild(i).GetComponent<I_BE2_Block>();
                if (childBlock != null)
                {
                    childBlocksArray = BE2_ArrayUtils.AddReturn(childBlocksArray, childBlock);
                }
            }
            childBlocksCount = childBlocksArray.Length;
        }

        public void UpdateLayout()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);
            UpdateChildBlocksList();
        }
    }
}
