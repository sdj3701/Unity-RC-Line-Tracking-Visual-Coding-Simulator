using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BlockView : MonoBehaviour
{
    public BlockDefinition Definition;
    public TMP_InputField[] InputFields;
    public BlockView NextBlock;
    public BlockView PreviousBlock; // [수정] 이전 블록 참조 추가
    private BlockNode node;

    // [수정] node 초기화를 Start()에서 Awake()로 변경
    void Awake()
    {
        // Awake에서 node를 초기화하여 Instantiate 직후에도 접근 가능하게 함
        node = new BlockNode();
        node.Type = Definition.BlockType;
        
        // InputNames를 사용하는 로직도 Awake로 옮겨야 안전합니다.
        // 기존 Start() 로직을 그대로 가져옵니다.
        foreach (var name in Definition.InputNames)
            node.Inputs.Add(new BlockInput { Name = name, Type = "number", Value = "0" });
    }

    void Start()
    {
        // Start에서는 InputField 리스너만 설정합니다.
        for (int i = 0; i < InputFields.Length; i++)
        {
            int index = i;
            InputFields[i].onValueChanged.AddListener((val) =>
            {
                // Awake에서 node가 초기화되었으므로 안전합니다.
                node.Inputs[index].Value = val;
            });
        }
    }

    public BlockNode GetNode() 
    {
        UpdateNextNodeChain(); // 노드를 요청할 때마다 체인 구조를 갱신
        return node;
    }
    
    // [수정] BlockNode의 NextBlocks를 현재 UI 연결 상태에 맞게 갱신
    public void UpdateNextNodeChain()
    {
        node.NextBlocks.Clear();
        if (NextBlock != null)
        {
            // NextBlock의 GetNode()를 호출하여 재귀적으로 전체 체인 갱신
            node.NextBlocks.Add(NextBlock.GetNode()); 
        }
    }

    // [수정] 이 블록과 그 아래에 연결된 모든 블록의 위치를 재조정 (X축 고정)
    public void UpdatePositionOfChain(float xPosition)
    {
        BlockView current = this;
        
        // 이 블록의 X 위치를 고정
        RectTransform currentRect = current.GetComponent<RectTransform>();
        currentRect.anchoredPosition = new Vector2(xPosition, currentRect.anchoredPosition.y);
        
        // 다음 블록들 위치 조정
        while (current.NextBlock != null)
        {
            RectTransform nextRect = current.NextBlock.GetComponent<RectTransform>();
            RectTransform currentR = current.GetComponent<RectTransform>();
            
            // 다음 블록의 Y 위치 = 현재 블록의 Y 위치 - 현재 블록의 높이 - 간격
            float newY = currentR.anchoredPosition.y - currentR.sizeDelta.y - 10f; // 10f는 간격
            
            // X 위치는 xPosition으로 고정
            nextRect.anchoredPosition = new Vector2(xPosition, newY); 
            
            current = current.NextBlock;
        }
    }
}