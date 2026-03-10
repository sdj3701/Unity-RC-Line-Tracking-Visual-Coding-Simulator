using System;
using UnityEngine;

/// <summary>
/// Cell type used by generated maze data.
/// </summary>
public enum MiroCellType
{
    Wall = 0,
    MainPath = 1,
    BranchPath = 2
}

/// <summary>
/// Serializable maze payload used for rendering and persistence.
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

    // Flattened cells (1-based maze coordinates are mapped to 0-based array index).
    // Values use MiroCellType numeric values.
    public int[] cells = Array.Empty<int>();

    /// <summary>
    /// Returns the flattened cell index from one-based maze coordinates.
    /// </summary>
    public int GetCellIndex(int oneBasedY, int oneBasedX)
    {
        return (oneBasedY - 1) * mazeSize + (oneBasedX - 1);
    }

    /// <summary>
    /// Reads a cell type using one-based maze coordinates.
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
/// Unity implementation of the Roblox Lua DFS maze generation algorithm.
/// </summary>
public class MiroAlgorithm : MonoBehaviour
{
    [Header("Maze Shape")]
    [Min(5)] public int mazeSize = 15;
    [Min(0.1f)] public float cellStepX = 5f;
    [Min(0.1f)] public float cellStepZ = 5f;

    [Header("Seed")]
    [Tooltip("When enabled, the same seed value reproduces the same maze layout.")]
    public bool useFixedSeed = false;
    [Tooltip("Used only when 'useFixedSeed' is true.")]
    public int fixedSeed = 12345;

    [Header("Start Coordinates (Lua 1-based)")]
    public int startX1 = 2;
    public int startY1 = 1;
    public int startX2 = 2;
    public int startY2 = 2;

    [Header("Debug")]
    [SerializeField] bool logSummary = true;

    // Internal nonce used to guarantee seed variation even when multiple generations happen rapidly.
    int generationNonce;

    // Direction vectors are 1-based to mirror the original Lua table indexing.
    static readonly int[] Dx = { 0, -2, 2, 0, 0 };
    static readonly int[] Dy = { 0, 0, 0, -2, 2 };
    static readonly int[] Wx = { 0, -1, 1, 0, 0 };
    static readonly int[] Wy = { 0, 0, 0, -1, 1 };

    // 24 permutations of directions used exactly like the original Lua script.
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
    /// Generates maze data using the same DFS rules used in the original Lua script.
    /// </summary>
    public MiroMazeData GenerateMazeData()
    {
        return GenerateMazeData(false);
    }

    /// <summary>
    /// Generates maze data and optionally forces random seed usage even when fixed seed mode is enabled.
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

        // Lua-equivalent setup:
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
    /// Resolves the seed for this generation call.
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
    /// Converts an arbitrary requested size into a valid odd maze size used by the algorithm.
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
    /// Initializes maze text and visited arrays with empty/default values.
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
    /// Recursive DFS routine translated from Lua subDFS with equivalent path-marking behavior.
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
    /// Converts internal text markers into serializable cell data.
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
