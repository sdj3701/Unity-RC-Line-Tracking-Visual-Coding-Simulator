using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.Core;
using MG_BlocksEngine2.UI;
using MG_BlocksEngine2.Environment;

namespace MG_BlocksEngine2.DragDrop
{
    public class BE2_DragTrigger : MonoBehaviour, I_BE2_Drag
    {
        // v2.11 - references to drag drop manager and execution manager refactored in drag scripts
        BE2_DragDropManager _dragDropManager => BE2_DragDropManager.Instance;
        BE2_ExecutionManager _executionManager => BE2_ExecutionManager.Instance;
        RectTransform _rectTransform;
        // v2.12 - removed unused blocks stack variable from the DragTrigger class

        Transform _transform;
        public Transform Transform => _transform ? _transform : transform;
        public Vector2 RayPoint => _rectTransform.position;
        public I_BE2_Block Block { get; set; }

        void Awake()
        {
            _transform = transform;
            _rectTransform = GetComponent<RectTransform>();
            Block = GetComponent<I_BE2_Block>();
        }

        public void OnPointerDown()
        {

        }

        public void OnRightPointerDownOrHold()
        {
            BE2_UI_ContextMenuManager.instance.OpenContextMenu(0, Block);
        }

        public void OnDragStart()
        {
            I_BE2_BlockSectionBody body = Block.Layout.SectionsArray[0].Body;

            if (BE2_DragDropManager.disableGroupDrag)
            {
                if (transform.parent.GetComponent<I_BE2_ProgrammingEnv>() != null)
                {
                    if (body.ChildBlocksCount > 0)
                    {
                        I_BE2_Block nextBlock = body.ChildBlocksArray[0].Transform.GetComponent<I_BE2_Block>();
                        BE2_OuterArea nextBlockOuterArea = nextBlock.Layout.OuterArea;

                        nextBlock.Transform.SetParent(transform.parent);

                        for (int i = body.ChildBlocksCount - 1; i >= 0; i--)
                        {
                            Transform child = body.ChildBlocksArray[i].Transform;

                            if (child.GetComponent<I_BE2_Block>() == null)
                                continue;

                            child.SetParent(nextBlockOuterArea.Transform);
                            child.SetAsFirstSibling();
                        }
                    }
                }
            }
        }

        public void OnDrag()
        {
            DetectSpot();
        }

        void DetectSpot()
        {
            if (Transform.parent != _dragDropManager.DraggedObjectsTransform)
                Transform.SetParent(_dragDropManager.DraggedObjectsTransform, true);

            BE2_Raycaster.ConnectionPoint connectionPoint = new BE2_Raycaster.ConnectionPoint();
            connectionPoint.spot = (_dragDropManager.Raycaster as BE2_Raycaster).FindClosestConnectableSpot(this, _dragDropManager.detectionDistance);
            if (connectionPoint.spot == null)
                connectionPoint.block = (_dragDropManager.Raycaster as BE2_Raycaster).FindClosestConnectableBlock(this, _dragDropManager.detectionDistance);

            _dragDropManager.ConnectionPoint = connectionPoint;
            I_BE2_Spot foundSpot = connectionPoint.spot;
            I_BE2_Block foundBlock = connectionPoint.block;

            Transform ghostBlockTransform = _dragDropManager.GhostBlockTransform;

            if (foundBlock != null)
            {
                ghostBlockTransform.SetParent(foundBlock.Transform.parent);
                ghostBlockTransform.localScale = foundBlock.Transform.localScale;
                ghostBlockTransform.localPosition = foundBlock.Transform.localPosition + new Vector3(0, (ghostBlockTransform as RectTransform).sizeDelta.y - 10, 0);//gameObject.SetActive(true);
                ghostBlockTransform.gameObject.SetActive(true);

            }
            else
            {
                ghostBlockTransform.gameObject.SetActive(false);
            }

            // v2.6 - adjustments on position and angle of blocks for supporting all canvas render modes
            ghostBlockTransform.localPosition = new Vector3(ghostBlockTransform.localPosition.x, ghostBlockTransform.localPosition.y, 0);
            ghostBlockTransform.localEulerAngles = Vector3.zero;
        }

        public void OnPointerUp()
        {
            if (_dragDropManager.ConnectionPoint.block != null)
            {
                Block.Transform.SetParent(_dragDropManager.ConnectionPoint.block.Transform.parent);
                _dragDropManager.ConnectionPoint.block.Transform.SetParent(Block.Layout.SectionsArray[0].Body.RectTransform);

                Transform outerAreaTransform = _dragDropManager.ConnectionPoint.block.Layout.OuterArea.Transform;
                int siblingIndex = _dragDropManager.ConnectionPoint.block.Transform.GetSiblingIndex();

                for (int i = outerAreaTransform.childCount - 1; i >= 0; i--)
                {
                    Transform child = outerAreaTransform.GetChild(i);

                    if (child.GetComponent<I_BE2_Block>() == null)
                        continue;

                    child.SetParent(_dragDropManager.ConnectionPoint.block.Transform.parent);
                    child.SetSiblingIndex(siblingIndex + 1);
                }

                I_BE2_ProgrammingEnv programmingEnv = Transform.GetComponentInParent<I_BE2_ProgrammingEnv>();

                if (programmingEnv != null)
                {
                    _executionManager.AddToBlocksStackArray(Block.Instruction.InstructionBase.BlocksStack, programmingEnv.TargetObject);
                }
            }
            else
            {
                I_BE2_Spot spot = _dragDropManager.Raycaster.GetSpotAtPosition(RayPoint);

                // v2.12 - dropping blocks in the ProgrammingEnv now can be done if part of the block is outside
                // of the Env but the pointer is inside 
                if (spot == null)
                    spot = _dragDropManager.Raycaster.GetSpotAtPosition(Core.BE2_InputManager.Instance.CanvasPointerPosition);

                if (spot != null)
                {
                    I_BE2_ProgrammingEnv programmingEnv = spot.Transform.GetComponentInParent<I_BE2_ProgrammingEnv>();
                    if (programmingEnv == null && spot.Transform.GetChild(0) != null)
                        programmingEnv = spot.Transform.GetChild(0).GetComponentInParent<I_BE2_ProgrammingEnv>();

                    if (programmingEnv != null)
                    {
                        Transform.SetParent(programmingEnv.Transform);
                        _executionManager.AddToBlocksStackArray(Block.Instruction.InstructionBase.BlocksStack, programmingEnv.TargetObject);
                    }
                    else
                        Destroy(Transform.gameObject);
                }
                else
                {
                    Destroy(Transform.gameObject);
                }
            }

            // v2.6 - adjustments on position and angle of blocks for supporting all canvas render modes
            Transform.localPosition = new Vector3(Transform.localPosition.x, Transform.localPosition.y, 0);
            Transform.localEulerAngles = Vector3.zero;

            // v2.9 - bugfix: TargetObject of blocks being null
            Block.Instruction.InstructionBase.UpdateTargetObject();
        }
    }
}
