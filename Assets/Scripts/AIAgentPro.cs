using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;

[Serializable]
public class GameStateData
{
    public int[,] grid;
    public List<PassengerInfo> passengers;
    public BusInfo currentBus;
    public List<WaitingAreaInfo> waitingArea;
    public int remainingPassengers;
    public int level;
}

[Serializable]
public class PassengerInfo
{
    public Vector2Int position;
    public int color;
}

[Serializable]
public class BusInfo
{
    public int color;
    public int capacity;
    public int currentPassengers;
}

[Serializable]
public class WaitingAreaInfo
{
    public int slotIndex;
    public int? passengerColor;
}

[Serializable]
public class AIMove
{
    public Vector2Int passengerPosition;
    public string action; // "bus" or "waiting"
    public string reasoning;
}

[Serializable]
public class GameAction
{
    public Passenger passenger;
    public Vector2Int position;
    public int colorIndex;
    public float priority;
    public string destination; // "bus" or "waiting"
}

public class AIAgentPro : MonoBehaviour
{
    private string OPENAI_API_KEY;
    private const string OPENAI_API_URL = "https://api.openai.com/v1/chat/completions";
    
    private GameManager gameManager;
    private bool isThinking = false;
    private GameLogger gameLogger;
    private int moveCount = 0;
    
    [Header("AI Settings")]
    [SerializeField] private bool useOpenAI = true;
    [SerializeField] private float moveDelay = 0.3f;
    [SerializeField] private bool debugMode = true;

    private void Start()
    {
        LoadOpenAIKey();
        gameManager = FindAnyObjectByType<GameManager>();
        gameLogger = new GameLogger();
        StartCoroutine(PlayGameAutomatically());
    }

    private void LoadOpenAIKey()
    {
        string keyFilePath = Path.Combine(Application.dataPath, "..", "openai_api_key.txt");
        
        try
        {
            if (File.Exists(keyFilePath))
            {
                OPENAI_API_KEY = File.ReadAllText(keyFilePath).Trim();
                
                if (string.IsNullOrEmpty(OPENAI_API_KEY))
                {
                    Debug.LogWarning("OpenAI API key file is empty. Please add your API key to openai_api_key.txt");
                    useOpenAI = false;
                }
                else
                {
                    Debug.Log("OpenAI API key loaded successfully from file.");
                }
            }
            else
            {
                Debug.LogWarning("OpenAI API key file not found. Please create openai_api_key.txt in the project root and add your API key.");
                useOpenAI = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading OpenAI API key: {e.Message}");
            useOpenAI = false;
        }
    }

    private IEnumerator PlayGameAutomatically()
    {
        yield return new WaitForSeconds(2f);
        
        while (true)
        {
            if (!isThinking && !IsGameTransitioning())
            {
                yield return MakeNextMove();
            }
            yield return new WaitForSeconds(moveDelay);
        }
    }

    private bool IsGameTransitioning()
    {
        // Check if any animations are playing
        var currentPath = GetCurrentPath();
        return currentPath != null && currentPath.Count > 0;
    }

    private IEnumerator MakeNextMove()
    {
        isThinking = true;
        
        GameStateData gameState = AnalyzeGameState();
        
        if (gameState == null || gameState.passengers.Count == 0)
        {
            isThinking = false;
            yield break;
        }
        
        // Use strategic algorithm or OpenAI
        GameAction bestAction = null;
        
        if (useOpenAI)
        {
            AIMove aiMove = null;
            yield return StartCoroutine(GetAIDecisionCoroutine(gameState, (result) => aiMove = result));
            
            if (aiMove != null)
            {
                bestAction = ConvertAIMoveToAction(aiMove, gameState);
            }
        }
        
        // Fallback to algorithmic approach if OpenAI fails or is disabled
        if (bestAction == null)
        {
            bestAction = GetBestStrategicMove(gameState);
        }
        
        if (bestAction != null)
        {
            ExecuteAction(bestAction);
            
            // Log the move
            var move = new AIMove
            {
                passengerPosition = bestAction.position,
                action = bestAction.destination,
                reasoning = $"Priority: {bestAction.priority:F2}"
            };
            gameLogger.LogMove(gameState, move);
            
            moveCount++;
            if (debugMode)
            {
                Debug.Log($"Move #{moveCount}: {bestAction.destination} passenger at ({bestAction.position.x},{bestAction.position.y}) - Priority: {bestAction.priority:F2}");
            }
        }
        
        isThinking = false;
    }

    private GameAction GetBestStrategicMove(GameStateData gameState)
    {
        List<GameAction> possibleActions = GetAllPossibleActions(gameState);
        
        if (possibleActions.Count == 0) return null;
        
        // Calculate priority for each action
        foreach (var action in possibleActions)
        {
            action.priority = CalculateActionPriority(action, gameState);
        }
        
        // Sort by priority (highest first)
        possibleActions.Sort((a, b) => b.priority.CompareTo(a.priority));
        
        return possibleActions[0];
    }

    private List<GameAction> GetAllPossibleActions(GameStateData gameState)
    {
        List<GameAction> actions = new List<GameAction>();
        var grid = GetGridField();
        
        foreach (var passenger in gameState.passengers)
        {
            Tile tile = grid[passenger.position.x, passenger.position.y];
            if (tile == null || tile.CurrentPassenger == null) continue;
            
            // Check if passenger can reach top row
            if (CanReachTop(passenger.position, gameState))
            {
                GameAction action = new GameAction
                {
                    passenger = tile.CurrentPassenger,
                    position = passenger.position,
                    colorIndex = passenger.color,
                    destination = passenger.color == gameState.currentBus.color ? "bus" : "waiting"
                };
                
                // Only add waiting action if there's space
                if (action.destination == "bus" || GetAvailableWaitingSlot() >= 0)
                {
                    actions.Add(action);
                }
            }
        }
        
        return actions;
    }

    private float CalculateActionPriority(GameAction action, GameStateData gameState)
    {
        float priority = 0f;
        
        // 1. Highest priority: Move matching passengers to current bus
        if (action.colorIndex == gameState.currentBus.color)
        {
            priority += 100f;
            
            // Bonus for filling the bus
            int spotsLeft = gameState.currentBus.capacity - gameState.currentBus.currentPassengers;
            priority += (3 - spotsLeft) * 10f;
        }
        
        // 2. Consider waiting area state
        int waitingOccupancy = gameState.waitingArea.Count(w => w.passengerColor.HasValue);
        int waitingSpace = 5 - waitingOccupancy;
        
        // 3. Check if this passenger blocks others
        int blockedPassengers = CountBlockedPassengers(action.position, gameState);
        priority -= blockedPassengers * 5f;
        
        // 4. Penalize using waiting area when it's getting full
        if (action.destination == "waiting")
        {
            priority -= (5 - waitingSpace) * 20f;
            
            // Check if future buses need this color
            if (WillColorBeNeededSoon(action.colorIndex, gameState))
            {
                priority += 15f;
            }
        }
        
        // 5. Distance to top (shorter is better)
        int distanceToTop = action.position.y;
        priority += (gameState.grid.GetLength(1) - distanceToTop) * 2f;
        
        // 6. Strategic positioning
        if (IsStrategicMove(action, gameState))
        {
            priority += 25f;
        }
        
        return priority;
    }

    private bool CanReachTop(Vector2Int start, GameStateData gameState)
    {
        // Simple BFS to check if passenger can reach top row
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        
        queue.Enqueue(start);
        visited.Add(start);
        
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            
            if (current.y == 0) return true;
            
            Vector2Int[] neighbors = {
                current + Vector2Int.up,
                current + Vector2Int.down,
                current + Vector2Int.left,
                current + Vector2Int.right
            };
            
            foreach (var next in neighbors)
            {
                if (IsValidMove(next, gameState) && !visited.Contains(next))
                {
                    queue.Enqueue(next);
                    visited.Add(next);
                }
            }
        }
        
        return false;
    }

    private bool IsValidMove(Vector2Int pos, GameStateData gameState)
    {
        if (pos.x < 0 || pos.x >= gameState.grid.GetLength(0) ||
            pos.y < 0 || pos.y >= gameState.grid.GetLength(1))
            return false;
            
        return gameState.grid[pos.x, pos.y] == 0; // Empty cell
    }

    private int CountBlockedPassengers(Vector2Int position, GameStateData gameState)
    {
        int blocked = 0;
        
        // Check passengers below this one
        foreach (var passenger in gameState.passengers)
        {
            if (passenger.position.y > position.y && 
                passenger.position.x == position.x)
            {
                // This passenger is directly below
                if (passenger.color == gameState.currentBus.color)
                {
                    blocked += 2; // Higher penalty for blocking matching passengers
                }
                else
                {
                    blocked += 1;
                }
            }
        }
        
        return blocked;
    }

    private bool WillColorBeNeededSoon(int colorIndex, GameStateData gameState)
    {
        // Check next few buses in the level data
        var levelData = GetCurrentLevelData();
        if (levelData == null) return false;
        
        int currentBusIndex = GetCurrentBusIndex();
        
        // Look ahead at next 2 buses
        for (int i = currentBusIndex + 1; i < Math.Min(currentBusIndex + 3, levelData.busConfigs.Length); i++)
        {
            if (levelData.busConfigs[i].color == colorIndex)
                return true;
        }
        
        return false;
    }

    private bool IsStrategicMove(GameAction action, GameStateData gameState)
    {
        // Check if this move opens up paths for other important passengers
        var grid = gameState.grid.Clone() as int[,];
        grid[action.position.x, action.position.y] = 0; // Simulate move
        
        int improvements = 0;
        
        foreach (var passenger in gameState.passengers)
        {
            if (passenger.position.Equals(action.position)) continue;
            
            if (passenger.color == gameState.currentBus.color)
            {
                // Check if this passenger now has a clearer path
                if (!CanReachTop(passenger.position, gameState) && 
                    CanReachTopWithGrid(passenger.position, grid))
                {
                    improvements++;
                }
            }
        }
        
        return improvements > 0;
    }

    private bool CanReachTopWithGrid(Vector2Int start, int[,] grid)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        
        queue.Enqueue(start);
        visited.Add(start);
        
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            
            if (current.y == 0) return true;
            
            Vector2Int[] neighbors = {
                current + Vector2Int.up,
                current + Vector2Int.down,
                current + Vector2Int.left,
                current + Vector2Int.right
            };
            
            foreach (var next in neighbors)
            {
                if (next.x >= 0 && next.x < grid.GetLength(0) &&
                    next.y >= 0 && next.y < grid.GetLength(1) &&
                    grid[next.x, next.y] == 0 && !visited.Contains(next))
                {
                    queue.Enqueue(next);
                    visited.Add(next);
                }
            }
        }
        
        return false;
    }

    private GameAction ConvertAIMoveToAction(AIMove move, GameStateData gameState)
    {
        var grid = GetGridField();
        if (grid == null) return null;
        
        Tile tile = grid[move.passengerPosition.x, move.passengerPosition.y];
        if (tile == null || tile.CurrentPassenger == null) return null;
        
        var passenger = gameState.passengers.FirstOrDefault(p => p.position.Equals(move.passengerPosition));
        if (passenger == null) return null;
        
        return new GameAction
        {
            passenger = tile.CurrentPassenger,
            position = move.passengerPosition,
            colorIndex = passenger.color,
            destination = move.action,
            priority = 100f
        };
    }

    private void ExecuteAction(GameAction action)
    {
        if (action?.passenger != null)
        {
            gameManager.OnPassengerClicked(action.passenger);
        }
    }

    // Improved OpenAI integration with better prompts
    private string GetSystemPrompt()
    {
        return @"You are an expert AI player for the Bus Jam puzzle game. You must analyze the game state and make optimal moves.

CRITICAL RULES:
1. Passengers can only move through EMPTY adjacent tiles (up, down, left, right)
2. Passengers must reach the TOP ROW (y=0) to board a bus
3. If passenger color MATCHES current bus color → they board immediately
4. If colors DON'T match → passenger goes to waiting area (5 slots MAX)
5. Bus departs when FULL (3 passengers)
6. Game FAILS if waiting area is FULL and you need to add another passenger

WINNING STRATEGY:
- ALWAYS prioritize passengers matching the current bus color
- NEVER block matching passengers with non-matching ones
- Clear paths for high-priority passengers
- Use waiting area SPARINGLY - it's a limited resource
- Think ahead about which colors future buses will need

Your response must be a JSON object:
{
  ""passengerPosition"": {""x"": x, ""y"": y},
  ""action"": ""bus"" or ""waiting"",
  ""reasoning"": ""Strategic explanation""
}";
    }

    private string GeneratePrompt(GameStateData gameState)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"LEVEL {gameState.level + 1} - Move #{moveCount + 1}");
        sb.AppendLine($"Grid: {gameState.grid.GetLength(0)}x{gameState.grid.GetLength(1)}");
        sb.AppendLine($"Remaining: {gameState.remainingPassengers} passengers");
        
        // Show grid with coordinates
        sb.AppendLine("\nGRID (0=empty, -1=disabled, colors=1,2,3...):");
        sb.AppendLine("  X→ 0 1 2 3 4 5 6 7 8");
        sb.AppendLine("Y↓   ---------------");
        
        for (int y = 0; y < gameState.grid.GetLength(1); y++)
        {
            sb.Append($"{y} |  ");
            for (int x = 0; x < gameState.grid.GetLength(0); x++)
            {
                sb.Append($"{gameState.grid[x, y],2}");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine($"\nCURRENT BUS: Color {gameState.currentBus.color} [{gameState.currentBus.currentPassengers}/3]");
        
        // Waiting area status
        int waitingUsed = gameState.waitingArea.Count(w => w.passengerColor.HasValue);
        sb.AppendLine($"\nWAITING AREA [{waitingUsed}/5 used]:");
        for (int i = 0; i < gameState.waitingArea.Count; i++)
        {
            var slot = gameState.waitingArea[i];
            sb.Append($"  [{i}]: ");
            sb.AppendLine(slot.passengerColor.HasValue ? $"Color {slot.passengerColor}" : "Empty");
        }
        
        // List moveable passengers
        sb.AppendLine("\nMOVEABLE PASSENGERS:");
        foreach (var passenger in gameState.passengers)
        {
            bool canReach = CanReachTop(passenger.position, gameState);
            sb.Append($"  ({passenger.position.x},{passenger.position.y}): Color {passenger.color}");
            if (passenger.color == gameState.currentBus.color)
            {
                sb.Append(" ← MATCHES BUS!");
            }
            if (!canReach)
            {
                sb.Append(" [BLOCKED]");
            }
            sb.AppendLine();
        }
        
        // Future buses preview
        var levelData = GetCurrentLevelData();
        if (levelData != null)
        {
            int busIndex = GetCurrentBusIndex();
            sb.AppendLine("\nUPCOMING BUSES:");
            for (int i = busIndex + 1; i < Math.Min(busIndex + 3, levelData.busConfigs.Length); i++)
            {
                sb.AppendLine($"  Next+{i - busIndex}: Color {levelData.busConfigs[i].color}");
            }
        }
        
        sb.AppendLine("\nChoose the BEST move to win this level!");
        
        return sb.ToString();
    }

    // OpenAI API communication (same as before but with improved error handling)
    private IEnumerator GetAIDecisionCoroutine(GameStateData gameState, System.Action<AIMove> callback)
    {
        string prompt = GeneratePrompt(gameState);
        
        using (UnityWebRequest www = new UnityWebRequest(OPENAI_API_URL, "POST"))
        {
            var requestBody = new OpenAIRequest
            {
                model = "gpt-4o-mini", 
                messages = new OpenAIMessage[]
                {
                    new OpenAIMessage { role = "system", content = GetSystemPrompt() },
                    new OpenAIMessage { role = "user", content = prompt }
                },
                temperature = 0.1f, // Lower temperature for more deterministic moves
                max_tokens = 300
            };
            
            string json = JsonUtility.ToJson(requestBody);
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
            
            www.uploadHandler = new UploadHandlerRaw(jsonToSend);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {OPENAI_API_KEY}");
            
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    if (debugMode)
                    {
                        Debug.Log($"OpenAI Response: {www.downloadHandler.text}");
                    }
                    
                    OpenAIResponse result = JsonUtility.FromJson<OpenAIResponse>(www.downloadHandler.text);
                    if (result.choices != null && result.choices.Length > 0)
                    {
                        string aiResponse = result.choices[0].message.content;
                        callback(ParseAIResponse(aiResponse));
                    }
                    else
                    {
                        Debug.LogError("No choices in OpenAI response");
                        callback(null);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse OpenAI response: {e.Message}");
                    callback(null);
                }
            }
            else
            {
                Debug.LogError($"OpenAI API Error: {www.error} - {www.downloadHandler.text}");
                callback(null);
            }
        }
    }

    private AIMove ParseAIResponse(string response)
    {
        try
        {
            int startIndex = response.IndexOf('{');
            int endIndex = response.LastIndexOf('}');
            
            if (startIndex >= 0 && endIndex >= 0)
            {
                string jsonStr = response.Substring(startIndex, endIndex - startIndex + 1);
                
                AIMove move = new AIMove();
                
                // Parse position
                var xMatch = System.Text.RegularExpressions.Regex.Match(jsonStr, @"""x""\s*:\s*(\d+)");
                var yMatch = System.Text.RegularExpressions.Regex.Match(jsonStr, @"""y""\s*:\s*(\d+)");
                
                if (xMatch.Success && yMatch.Success)
                {
                    int x = int.Parse(xMatch.Groups[1].Value);
                    int y = int.Parse(yMatch.Groups[1].Value);
                    move.passengerPosition = new Vector2Int(x, y);
                }
                
                // Parse action
                var actionMatch = System.Text.RegularExpressions.Regex.Match(jsonStr, @"""action""\s*:\s*""([^""]+)""");
                if (actionMatch.Success)
                {
                    move.action = actionMatch.Groups[1].Value;
                }
                
                // Parse reasoning
                var reasoningMatch = System.Text.RegularExpressions.Regex.Match(jsonStr, @"""reasoning""\s*:\s*""([^""]+)""");
                if (reasoningMatch.Success)
                {
                    move.reasoning = reasoningMatch.Groups[1].Value;
                }
                
                return move;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse AI response: {e.Message}\nResponse: {response}");
        }
        
        return null;
    }

    // Helper methods to access private GameManager fields
    private GameStateData AnalyzeGameState()
    {
        if (gameManager == null) return null;
        
        var gameState = new GameStateData
        {
            passengers = new List<PassengerInfo>(),
            waitingArea = new List<WaitingAreaInfo>(),
            level = GetCurrentLevel()
        };
        
        var grid = GetGridField();
        if (grid == null) return null;
        
        int gridX = grid.GetLength(0);
        int gridY = grid.GetLength(1);
        gameState.grid = new int[gridX, gridY];
        
        for (int x = 0; x < gridX; x++)
        {
            for (int y = 0; y < gridY; y++)
            {
                Tile tile = grid[x, y];
                if (tile == null) continue;
                
                if (tile.IsDisabled)
                {
                    gameState.grid[x, y] = -1;
                }
                else if (tile.CurrentPassenger != null)
                {
                    var passenger = tile.CurrentPassenger;
                    int colorIndex = GetColorIndex(passenger.Color);
                    gameState.grid[x, y] = colorIndex;
                    gameState.passengers.Add(new PassengerInfo
                    {
                        position = new Vector2Int(x, y),
                        color = colorIndex
                    });
                }
                else
                {
                    gameState.grid[x, y] = 0;
                }
            }
        }
        
        var currentBus = GetCurrentBus();
        if (currentBus != null)
        {
            gameState.currentBus = new BusInfo
            {
                color = GetColorIndex(currentBus.Color),
                capacity = 3,
                currentPassengers = GetBusPassengerCount(currentBus)
            };
        }
        
        var waitingSlots = GetWaitingSlots();
        if (waitingSlots != null)
        {
            for (int i = 0; i < waitingSlots.Length; i++)
            {
                gameState.waitingArea.Add(new WaitingAreaInfo
                {
                    slotIndex = i,
                    passengerColor = waitingSlots[i].Passenger != null ? 
                        GetColorIndex(waitingSlots[i].Passenger.Color) : null
                });
            }
        }
        
        gameState.remainingPassengers = GetRemainingPassengers();
        
        return gameState;
    }

    // Reflection helpers
    private Tile[,] GetGridField()
    {
        var field = typeof(GameManager).GetField("grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(gameManager) as Tile[,];
    }

    private Bus GetCurrentBus()
    {
        var field = typeof(GameManager).GetField("currentBus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(gameManager) as Bus;
    }

    private WaitingSlot[] GetWaitingSlots()
    {
        var field = typeof(GameManager).GetField("waitingSlots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(gameManager) as WaitingSlot[];
    }

    private List<Vector2Int> GetCurrentPath()
    {
        var field = typeof(GameManager).GetField("currentPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(gameManager) as List<Vector2Int>;
    }

    private int GetRemainingPassengers()
    {
        var field = typeof(GameManager).GetField("remainingPassengers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field != null ? (int)field.GetValue(gameManager) : 0;
    }

    private int GetCurrentLevel()
    {
        var field = typeof(GameManager).GetField("currentLevelIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field != null ? (int)field.GetValue(gameManager) : 0;
    }

    private int GetCurrentBusIndex()
    {
        var field = typeof(GameManager).GetField("currentBusIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field != null ? (int)field.GetValue(gameManager) : 0;
    }

    private LevelData GetCurrentLevelData()
    {
        var field = typeof(GameManager).GetField("currentLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(gameManager) as LevelData;
    }

    private int GetBusPassengerCount(Bus bus)
    {
        var field = typeof(Bus).GetField("passengers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var passengers = field?.GetValue(bus) as List<Passenger>;
        return passengers?.Count ?? 0;
    }

    private int GetAvailableWaitingSlot()
    {
        var waitingSlots = GetWaitingSlots();
        if (waitingSlots == null) return -1;
        
        for (int i = 0; i < waitingSlots.Length; i++)
        {
            if (waitingSlots[i].Passenger == null)
                return i;
        }
        return -1;
    }

    private int GetColorIndex(Color color)
    {
        var field = typeof(GameManager).GetField("possibleColors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var colors = field?.GetValue(gameManager) as Color[];
        
        if (colors != null)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                if (ColorsAreEqual(colors[i], color))
                    return i;
            }
        }
        
        return -1;
    }

    private bool ColorsAreEqual(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f && 
               Mathf.Abs(a.g - b.g) < 0.01f && 
               Mathf.Abs(a.b - b.b) < 0.01f;
    }
}

[Serializable]
public class GameLogEntry
{
    public DateTime timestamp;
    public int level;
    public int remainingPassengers;
    public Vector2Int passengerPosition;
    public string action;
    public string reasoning;
    public int busColor;
    public int waitingAreaOccupancy;
}

[Serializable]
public class OpenAIRequest
{
    public string model;
    public OpenAIMessage[] messages;
    public float temperature;
    public int max_tokens;
}

[Serializable]
public class OpenAIMessage
{
    public string role;
    public string content;
}

[Serializable]
public class OpenAIResponse
{
    public OpenAIChoice[] choices;
}

[Serializable]
public class OpenAIChoice
{
    public OpenAIMessage message;
}

// Game Logger class for logging moves
public class GameLogger
{
    private List<GameLogEntry> logs = new List<GameLogEntry>();
    private string logFilePath;
    private string localLogFilePath;
    private string logFileName;

    public GameLogger()
    {
        // Create filename with timestamp
        logFileName = $"game_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        
        // Save to Unity's persistent data path
        logFilePath = System.IO.Path.Combine(Application.persistentDataPath, logFileName);
        
        // Also save to a local GameLogs folder in the project root
        string projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);
        string localLogDir = System.IO.Path.Combine(projectPath, "GameLogs");
        
        // Create GameLogs directory if it doesn't exist
        if (!System.IO.Directory.Exists(localLogDir))
        {
            System.IO.Directory.CreateDirectory(localLogDir);
        }
        
        localLogFilePath = System.IO.Path.Combine(localLogDir, logFileName);
        
        WriteHeader();
        
        Debug.Log($"AI Agent logs will be saved to:");
        Debug.Log($"1. Persistent: {logFilePath}");
        Debug.Log($"2. Local: {localLogFilePath}");
    }

    private void WriteHeader()
    {
        string header = "Timestamp,Level,RemainingPassengers,PassengerPosition,Action,Reasoning,BusColor,WaitingAreaStatus";
        
        // Write to both locations
        System.IO.File.WriteAllText(logFilePath, header + "\n");
        System.IO.File.WriteAllText(localLogFilePath, header + "\n");
    }

    public void LogMove(GameStateData gameState, AIMove move)
    {
        var entry = new GameLogEntry
        {
            timestamp = DateTime.Now,
            level = gameState.level,
            remainingPassengers = gameState.remainingPassengers,
            passengerPosition = move.passengerPosition,
            action = move.action,
            reasoning = move.reasoning,
            busColor = gameState.currentBus.color,
            waitingAreaOccupancy = gameState.waitingArea.Count(w => w.passengerColor.HasValue)
        };

        logs.Add(entry);
        WriteLogEntry(entry);
    }

    private void WriteLogEntry(GameLogEntry entry)
    {
        string line = $"{entry.timestamp:yyyy-MM-dd HH:mm:ss},{entry.level},{entry.remainingPassengers}," +
                     $"({entry.passengerPosition.x};{entry.passengerPosition.y}),{entry.action}," +
                     $"\"{entry.reasoning}\",{entry.busColor},{entry.waitingAreaOccupancy}/5";
        
        // Append to both log files
        try
        {
            System.IO.File.AppendAllText(logFilePath, line + "\n");
            System.IO.File.AppendAllText(localLogFilePath, line + "\n");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write log entry: {e.Message}");
        }
    }
} 