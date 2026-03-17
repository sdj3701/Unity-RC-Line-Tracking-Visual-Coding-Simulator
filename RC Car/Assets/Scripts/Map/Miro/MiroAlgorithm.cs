using System;
using UnityEngine;

/// <summary>
/// 생성된 미로 데이터에서 사용하는 셀 타입 정의.
/// </summary>
public enum MiroCellType
{
    Wall = 0,
    MainPath = 1,
    BranchPath = 2
}

/// <summary>
/// 렌더링/저장을 위해 직렬화 가능한 미로 데이터 구조.
/// </summary>
[Serializable]
public class MiroMazeData
{
    public int version = 1;
    public int mazeSize = 15;
    public int seed = -1;
    public string generatedAtUtc = "";
    public float cellStepX = 5f;
    public float cellStepZ = 5f;

    public int startX1 = 2;
    public int startY1 = 1;
    public int startX2 = 2;
    public int startY2 = 2;

    public int exitX = 14;
    public int exitY = 14;
    public int outerExitX = 14;
    public int outerExitY = 15;

    // 1-based 미로 좌표를 0-based 1차원 배열로 펼친 셀 데이터.
    // 값은 MiroCellType 열거형의 정수값을 사용한다.
    public int[] cells = Array.Empty<int>();

    /// <summary>
    /// 1-based 미로 좌표를 1차원 배열 인덱스로 변환한다.
    /// </summary>
    public int GetCellIndex(int oneBasedY, int oneBasedX)
    {
        return (oneBasedY - 1) * mazeSize + (oneBasedX - 1);
    }

    /// <summary>
    /// 1-based 미로 좌표 기준으로 셀 타입을 조회한다.
    /// </summary>
    public MiroCellType GetCellType(int oneBasedY, int oneBasedX)
    {
        if (cells == null || oneBasedY < 1 || oneBasedY > mazeSize || oneBasedX < 1 || oneBasedX > mazeSize)
        {
            return MiroCellType.Wall;
        }

        int index = GetCellIndex(oneBasedY, oneBasedX);
        if (index < 0 || index >= cells.Length)
        {
            return MiroCellType.Wall;
        }

        return (MiroCellType)cells[index];
    }
}

/// <summary>
/// Roblox Lua DFS 미로 생성 알고리즘을 Unity로 옮긴 구현체.
/// </summary>
public class MiroAlgorithm : MonoBehaviour
{
    [Header("Maze Shape")]
    [Min(5)] public int mazeSize = 15;
    [Min(0.1f)] public float cellStepX = 5f;
    [Min(0.1f)] public float cellStepZ = 5f;

    [Header("Seed")]
    [Tooltip("활성화하면 동일한 시드로 같은 미로를 재현한다.")]
    public bool useFixedSeed = false;
    [Tooltip("'useFixedSeed'가 켜졌을 때만 사용하는 시드 값.")]
    public int fixedSeed = 12345;

    [Header("Start Coordinates (Lua 1-based)")]
    public int startX1 = 2;
    public int startY1 = 1;
    public int startX2 = 2;
    public int startY2 = 2;

    [Header("Debug")]
    [SerializeField] bool logSummary = true;

    // 짧은 시간에 연속 생성해도 시드가 달라지도록 보조값으로 사용한다.
    int generationNonce;

    // 원본 Lua 테이블 인덱싱(1-based)을 그대로 맞추기 위한 방향 벡터.
    static readonly int[] Dx = { 0, -2, 2, 0, 0 };
    static readonly int[] Dy = { 0, 0, 0, -2, 2 };
    static readonly int[] Wx = { 0, -1, 1, 0, 0 };
    static readonly int[] Wy = { 0, 0, 0, -1, 1 };

    // 원본 Lua 스크립트와 동일한 4방향 순열 24개.
    static readonly int[,] DirectionPermutations = new int[,]
    {
        { 1, 2, 3, 4 }, { 1, 2, 4, 3 }, { 1, 3, 2, 4 }, { 1, 3, 4, 2 }, { 1, 4, 2, 3 }, { 1, 4, 3, 2 },
        { 2, 1, 3, 4 }, { 2, 1, 4, 3 }, { 2, 3, 1, 4 }, { 2, 3, 4, 1 }, { 2, 4, 1, 3 }, { 2, 4, 3, 1 },
        { 3, 2, 1, 4 }, { 3, 2, 4, 1 }, { 3, 1, 2, 4 }, { 3, 1, 4, 2 }, { 3, 4, 2, 1 }, { 3, 4, 1, 2 },
        { 4, 2, 3, 1 }, { 4, 2, 1, 3 }, { 4, 3, 2, 1 }, { 4, 3, 1, 2 }, { 4, 1, 2, 3 }, { 4, 1, 3, 2 }
    };

    bool isMazeEnd;
    System.Random random;

    /// <summary>
    /// 원본 Lua와 동일한 DFS 규칙으로 미로 데이터를 생성한다.
    /// </summary>
    public MiroMazeData GenerateMazeData()
    {
        return GenerateMazeData(false);
    }

    /// <summary>
    /// 미로 데이터를 생성한다.
    /// forceRandomSeed=true면 고정 시드 설정을 무시하고 랜덤 시드를 강제한다.
    /// </summary>
    public MiroMazeData GenerateMazeData(bool forceRandomSeed)
    {
        int normalizedSize = NormalizeMazeSize(mazeSize);
        int seed = ResolveSeed(forceRandomSeed);
        random = new System.Random(seed);
        isMazeEnd = false;

        char[,] text = new char[normalizedSize + 1, normalizedSize + 1];
        bool[,] visited = new bool[normalizedSize + 1, normalizedSize + 1];

        InitializeArrays(text, visited, normalizedSize);

        // Lua 초기값과 동일하게 시작점/출구 라인 셀을 먼저 개방한다.
        // MazeTextArray[Y1][X1] = "*"
        // MazeTextArray[Y2][X2] = "*"
        // MazeTextArray[MAZE_SIZE][MAZE_SIZE-1] = "*"
        text[startY1, startX1] = '*';
        text[startY2, startX2] = '*';
        text[normalizedSize, normalizedSize - 1] = '*';

        visited[startY1, startX1] = true;
        visited[startY2, startX2] = true;
        visited[normalizedSize, normalizedSize - 1] = true;

        SubDfs(startY2, startX2, startY1, startX1, normalizedSize, text, visited);

        MiroMazeData data = BuildMazeData(normalizedSize, seed, text);
        if (logSummary)
        {
            Debug.Log($"[MiroAlgorithm] Generated maze size={data.mazeSize}, seed={data.seed}, generatedAt={data.generatedAtUtc}");
        }

        return data;
    }

    /// <summary>
    /// 이번 생성 호출에서 사용할 시드를 결정한다.
    /// </summary>
    int ResolveSeed(bool forceRandomSeed)
    {
        if (!forceRandomSeed && useFixedSeed)
        {
            return fixedSeed;
        }

        generationNonce++;
        int tick = Environment.TickCount;
        long ticksUtc = DateTime.UtcNow.Ticks;
        return unchecked((tick * 397) ^ generationNonce ^ (int)ticksUtc);
    }

    /// <summary>
    /// 요청된 미로 크기를 알고리즘이 처리 가능한 홀수 크기로 보정한다.
    /// </summary>
    int NormalizeMazeSize(int requestedSize)
    {
        int normalized = Mathf.Max(5, requestedSize);
        if (normalized % 2 == 0)
        {
            normalized += 1;
        }

        return normalized;
    }

    /// <summary>
    /// 미로 텍스트 배열과 방문 배열을 기본값으로 초기화한다.
    /// </summary>
    void InitializeArrays(char[,] text, bool[,] visited, int size)
    {
        for (int y = 1; y <= size; y++)
        {
            for (int x = 1; x <= size; x++)
            {
                text[y, x] = '\0';
                visited[y, x] = false;
            }
        }
    }

    /// <summary>
    /// Lua subDFS를 동일한 동작으로 옮긴 재귀 DFS 함수.
    /// 경로 마킹("*", ".") 규칙도 원본과 동일하게 유지한다.
    /// </summary>
    void SubDfs(int bfY2, int bfX2, int bfY1, int bfX1, int size, char[,] text, bool[,] visited)
    {
        int permutationIndex = random.Next(24);

        for (int i = 0; i < 4; i++)
        {
            int dir = DirectionPermutations[permutationIndex, i];
            int nextY2 = bfY2 + Dy[dir];
            int nextX2 = bfX2 + Dx[dir];

            bool inRange =
                nextY2 > 1 &&
                nextY2 < size &&
                nextX2 > 1 &&
                nextX2 < size;

            if (!inRange || visited[nextY2, nextX2])
            {
                continue;
            }

            if (!isMazeEnd && (bfX2 > 2 || bfY2 > 2))
            {
                text[bfY1, bfX1] = '*';
                text[bfY2, bfX2] = '*';
            }

            int curX1 = bfX2 + Wx[dir];
            int curX2 = bfX2 + Dx[dir];
            int curY1 = bfY2 + Wy[dir];
            int curY2 = bfY2 + Dy[dir];

            if (curY2 == size - 1 && curX2 == size - 1)
            {
                text[curY1, curX1] = '*';
                text[curY2, curX2] = '*';
                isMazeEnd = true;
            }
            else
            {
                text[curY1, curX1] = '.';
                text[curY2, curX2] = '.';
            }

            visited[curY1, curX1] = true;
            visited[curY2, curX2] = true;

            SubDfs(curY2, curX2, curY1, curX1, size, text, visited);
        }

        if (!isMazeEnd)
        {
            text[bfY1, bfX1] = '.';
            text[bfY2, bfX2] = '.';
        }
    }

    /// <summary>
    /// 내부 텍스트 마커('*', '.')를 직렬화 가능한 셀 데이터로 변환한다.
    /// </summary>
    MiroMazeData BuildMazeData(int size, int seed, char[,] text)
    {
        MiroMazeData data = new MiroMazeData
        {
            version = 1,
            mazeSize = size,
            seed = seed,
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            cellStepX = cellStepX,
            cellStepZ = cellStepZ,
            startX1 = startX1,
            startY1 = startY1,
            startX2 = startX2,
            startY2 = startY2,
            exitX = size - 1,
            exitY = size - 1,
            outerExitX = size - 1,
            outerExitY = size,
            cells = new int[size * size]
        };

        for (int y = 1; y <= size; y++)
        {
            for (int x = 1; x <= size; x++)
            {
                char marker = text[y, x];
                MiroCellType cellType = MiroCellType.Wall;

                if (marker == '*')
                {
                    cellType = MiroCellType.MainPath;
                }
                else if (marker == '.')
                {
                    cellType = MiroCellType.BranchPath;
                }

                int index = (y - 1) * size + (x - 1);
                data.cells[index] = (int)cellType;
            }
        }

        return data;
    }
}
