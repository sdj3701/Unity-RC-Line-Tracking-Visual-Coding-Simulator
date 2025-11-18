using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MG_BlocksEngine2.Block;
using MG_BlocksEngine2.UI;
using MG_BlocksEngine2.Environment;

namespace MG_BlocksEngine2.DragDrop
{
    public class BE2_DragBlock : MonoBehaviour, I_BE2_Drag
    {
        RectTransform _rectTransform;
        // v2.11 - references to drag drop manager and execution manager refactored in drag scripts
        BE2_DragDropManager _dragDropManager => BE2_DragDropManager.Instance;
        Transform _transform;
        public Transform Transform => _transform ? _transform : transform;

        // v2.13 ----> move raypoint to layout and create new variable to indicate the position of the ghost block when placed over, or a method  
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

        // v2.13 - BE2_DragBlock.OnDragStart implements the group drag feature with also the possibility to drag a single block by holding the auxiliary key  
        public void OnDragStart()
        {
            BE2_OuterArea outerArea = Block.Layout.OuterArea;

            if (!BE2_DragDropManager.disableGroupDrag)
            {
                if (Block.ParentBlock == null)
                    return;

                for (int i = transform.parent.childCount - 1; i > transform.GetSiblingIndex(); i--)
                {
                    Transform child = transform.parent.GetChild(i);

                    if (child.GetComponent<I_BE2_Block>() == null)
                        continue;

                    child.SetParent(outerArea.Transform);
                    child.SetAsFirstSibling();
                }
            }
            else
            {
                if (transform.parent.GetComponent<I_BE2_ProgrammingEnv>() != null)
                {
                    if (outerArea.childBlocksCount > 0)
                    {
                        I_BE2_Block nextBlock = outerArea.childBlocksArray[0].Transform.GetComponent<I_BE2_Block>();
                        BE2_OuterArea nextBlockOuterArea = nextBlock.Layout.OuterArea;

                        nextBlock.Transform.SetParent(transform.parent);

                        for (int i = outerArea.childBlocksCount - 1; i >= 0; i--)
                        {
                            Transform child = outerArea.childBlocksArray[i].Transform;

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

            if (foundSpot != null)
            {
                if (foundSpot is BE2_SpotOuterArea)
                {
                    if (foundSpot.Block.ParentSection != null)
                    {
                        ghostBlockTransform.SetParent(foundSpot.Block.Transform.parent);
                        ghostBlockTransform.localScale = Vector3.one;
                        ghostBlockTransform.gameObject.SetActive(true);
                        ghostBlockTransform.SetSiblingIndex(foundSpot.Block.Transform.GetSiblingIndex() + 1);

                        foundSpot.Block.ParentSection.UpdateLayout();
                    }
                    else
                    {
                        if (foundSpot.Block.Transform.parent.GetComponent<I_BE2_ProgrammingEnv>() == null)
                        {
                            ghostBlockTransform.SetParent(foundSpot.Block.Transform.parent);
                            ghostBlockTransform.SetSiblingIndex(foundSpot.Block.Transform.GetSiblingIndex() + 1);
                        }
                        else
                        {
                            ghostBlockTransform.SetParent(foundSpot.Transform);
                            ghostBlockTransform.SetAsFirstSibling();
                        }

                        ghostBlockTransform.localScale = Vector3.one;
                        ghostBlockTransform.gameObject.SetActive(true);

                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(foundSpot.Transform.parent as RectTransform);
                    }
                }
                else if (foundSpot is BE2_SpotBlockBody && foundSpot.Block != Block)
                {
                    ghostBlockTransform.SetParent(foundSpot.Transform);
                    ghostBlockTransform.localScale = Vector3.one;
                    ghostBlockTransform.gameObject.SetActive(true);
                    ghostBlockTransform.SetSiblingIndex(0);
                }
                else
                {
                    ghostBlockTransform.gameObject.SetActive(false);
                }
            }
            else if (foundBlock != null)
            {
                ghostBlockTransform.SetParent(foundBlock.Transform.parent);
                ghostBlockTransform.localScale = foundBlock.Transform.localScale;
                ghostBlockTransform.localPosition = Block.Layout.OuterArea.GetTopDropPosition(foundBlock);
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

        // v2.13 - refactored to include the possiblity to drag in the outer area
        public void OnPointerUp()
        {
            if (_dragDropManager.ConnectionPoint.spot != null)
            {
                if (_dragDropManager.ConnectionPoint.spot is BE2_SpotBlockBody)
                {
                    DropTo(_dragDropManager.ConnectionPoint.spot, 0);
                }
                else if (_dragDropManager.ConnectionPoint.spot is BE2_SpotOuterArea)
                {
                    DropTo(_dragDropManager.GhostBlockTransform.parent, _dragDropManager.GhostBlockTransform.GetSiblingIndex());
                }
                else
                {
                    DropTo(_dragDropManager.ConnectionPoint.spot.Block.Transform.parent, _dragDropManager.ConnectionPoint.spot.Block.Transform.GetSiblingIndex() + 1);
                }

                // v2.13 - releases the OuterArea.childBlocks when block is release in another block 
                Transform outerAreaTransform = Block.Layout.OuterArea.Transform;
                int siblingIndex = transform.GetSiblingIndex();

                for (int i = outerAreaTransform.childCount - 1; i >= 0; i--)
                {
                    Transform child = outerAreaTransform.GetChild(i);

                    if (child.GetComponent<I_BE2_Block>() == null)
                        continue;

                    child.SetParent(transform.parent);
                    child.SetSiblingIndex(siblingIndex + 1);
                }
            }
            else if (_dragDropManager.ConnectionPoint.block != null)
            {
                Block.Transform.SetParent(_dragDropManager.ConnectionPoint.block.Transform.parent);
                _dragDropManager.ConnectionPoint.block.Transform.SetParent(Block.Layout.OuterArea.Transform);

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
                        Transform.SetParent(programmingEnv.Transform);
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

        // v2.11 - added DropTo method to the BE2_DragBlock and BE2_DragOperation classes
        void DropTo(Transform spot, int siblinIndex)
        {
            Transform.SetParent(spot);
            Transform.SetSiblingIndex(siblinIndex);
        }
        void DropTo(I_BE2_Spot spot, int siblinIndex)
        {
            DropTo(spot.Transform, siblinIndex);
        }
        public void DropTo(I_BE2_Block parentBlock, int sectionIndex, int siblinIndex)
        {
            if (parentBlock.Layout.SectionsArray.Length > sectionIndex && parentBlock.Layout.SectionsArray[sectionIndex].Body != null) // make sure the body exists
            {
                DropTo(parentBlock.Layout.SectionsArray[sectionIndex].Body.Spot, siblinIndex);

                parentBlock.Instruction.InstructionBase.BlocksStack.PopulateStack();
            }
        }
    }
}
