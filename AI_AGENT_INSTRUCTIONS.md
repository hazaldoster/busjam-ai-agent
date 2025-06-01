# AI Agent for Bus Jam Game

## Overview

The AI agent implementation for the Bus Jam game:

**AIAgentPro.cs** - Enhanced AI agent with strategic fallback algorithm

## Features

### AIAgentPro 
- **Hybrid Approach**: Uses OpenAI API with algorithmic fallback
- **Strategic Planning**: Advanced priority-based decision making
- **Path Analysis**: BFS-based pathfinding to ensure moves are valid
- **Lookahead**: Considers future bus colors for optimal waiting area usage
- **Performance Optimization**: Detects blocked passengers and strategic moves
- **Comprehensive Logging**: Logs all moves to CSV files for analysis

### Key Strategies Implemented:
1. **Priority System**:
   - Matching passengers get highest priority (100+ points)
   - Penalizes blocking other passengers
   - Considers waiting area capacity
   - Evaluates strategic positioning

2. **Smart Waiting Area Management**:
   - Only uses waiting area when necessary
   - Checks if colors will be needed for future buses
   - Avoids filling up the waiting area

3. **Path Optimization**:
   - Ensures passengers can reach the top before moving
   - Identifies moves that unblock other passengers
   - Minimizes unnecessary moves

## Setup Instructions

### 1. Add AI Agent to Scene

1. Open your Unity scene with the game
2. Create an empty GameObject (Right-click in Hierarchy → Create Empty)
3. Name it "AI Agent"
4. Add the AIAgentPro component:
   - Select the AI Agent GameObject
   - In Inspector, click "Add Component"
   - Search for "AIAgentPro" and add it

### 2. Configure AI Settings

In the AIAgentPro component inspector:
- **Use OpenAI**: Toggle ON to use OpenAI API, OFF for pure algorithmic approach
- **Move Delay**: Set to 0.3-0.5 seconds for visible moves
- **Debug Mode**: Toggle ON to see detailed move logs in console

## Algorithm Details

### Decision Making Process

The AI uses a sophisticated decision-making process:

1. **Game State Analysis**:
   - Analyzes the current grid state using reflection to access GameManager data
   - Identifies all passengers, their positions, and colors
   - Checks current bus color, capacity, and passenger count
   - Monitors waiting area occupancy (5 slots maximum)

2. **Path Validation**:
   - Uses Breadth-First Search (BFS) to verify passengers can reach the top row
   - Only considers moves for passengers that have valid paths
   - Prevents invalid moves that would waste time

3. **Priority Calculation**:
   ```
   Priority Factors:
   - Bus Color Match: +100 points (highest priority)
   - Bus Fill Bonus: +(3-remaining_spots) * 10
   - Blocked Passengers: -5 points per blocked passenger
   - Waiting Area Penalty: -(5-available_space) * 20
   - Future Bus Relevance: +15 points if color needed soon
   - Distance to Top: +(grid_height - y_position) * 2
   - Strategic Move Bonus: +25 points if unblocks others
   ```

4. **Strategic Analysis**:
   - Identifies passengers that block others from reaching matching buses
   - Prioritizes moves that open paths for high-priority passengers
   - Considers future bus colors to optimize waiting area usage

### GPT-4 Backend Instructions

When OpenAI mode is enabled, the system sends detailed prompts to GPT-4:

#### System Prompt:
```
"You are an expert AI player for the Bus Jam puzzle game. You must analyze the game state and make optimal moves.

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
- Think ahead about which colors future buses will need"
```

#### Game State Prompt:
The AI sends a detailed game state including:
- Grid visualization with coordinates
- Current bus color and capacity
- Waiting area status
- List of moveable passengers with blocking status
- Upcoming bus colors for strategic planning

#### Expected Response Format:
```json
{
  "passengerPosition": {"x": x, "y": y},
  "action": "bus" or "waiting",
  "reasoning": "Strategic explanation"
}
```

## How It Works

1. **Game State Analysis**: 
   - Reads the current grid state using reflection
   - Identifies all passengers and their colors
   - Checks current bus color and capacity
   - Monitors waiting area status

2. **Decision Making**:
   - If OpenAI is enabled, sends structured game state to GPT-4
   - GPT-4 analyzes using strategic rules and returns optimal move
   - If OpenAI fails or is disabled, uses priority-based algorithm
   - Algorithm calculates priority scores for all possible moves

3. **Move Execution**:
   - Simulates clicking on the chosen passenger
   - Waits for animations to complete
   - Logs the move with reasoning

## Performance Expectations

The AI agent should be able to:
- Complete at least 5 out of 6 levels (as required)
- Make optimal moves to avoid filling the waiting area
- Prioritize matching passengers for current bus
- Plan ahead for future buses

## Logging

Game logs are saved to two locations:

1. **Unity Persistent Data Path**:
   - **Windows**: `%USERPROFILE%\AppData\LocalLow\[CompanyName]\[ProductName]\`
   - **macOS**: `~/Library/Application Support/[CompanyName]/[ProductName]/`
   - **Linux**: `~/.config/unity3d/[CompanyName]/[ProductName]/`

2. **Local Project Folder**:
   - `YourProjectFolder/GameLogs/`
   - Easier to access for analysis

Log format: `game_log_YYYYMMDD_HHMMSS.csv`

Columns:
- Timestamp, Level, Remaining Passengers
- Passenger Position, Action (bus/waiting)
- Reasoning, Bus Color, Waiting Area Status

## Troubleshooting

### AI Not Making Moves
1. Check if AI Agent GameObject is active in scene
2. Verify GameManager exists in scene
3. Check Unity Console for error messages

### Poor Performance
1. Increase "Move Delay" to give more thinking time
2. Enable "Debug Mode" to see decision reasoning
3. Check logs for patterns in failed moves
4. Try toggling between OpenAI and algorithmic modes

## Advanced Usage

### Modifying Strategy Weights
In `CalculateActionPriority()` method:
- Adjust priority values for different strategies
- Modify penalties for waiting area usage
- Change lookahead distance for future buses

### Adding Custom Logic
1. Modify `GetBestStrategicMove()` for new strategies
2. Update `CalculateActionPriority()` for new scoring
3. Enhance `IsStrategicMove()` for complex patterns

## Performance Tips

1. **For Faster Completion**: Set Move Delay to 0.1 seconds
2. **For Learning**: Enable Debug Mode and set Move Delay to 1 second
3. **For Analysis**: Review CSV logs after each session
4. **For Development**: Use algorithmic mode (toggle off OpenAI) for consistent testing

The AI agent combines advanced algorithmic decision-making with GPT-4's strategic analysis to achieve optimal performance! 