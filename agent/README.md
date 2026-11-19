# OSPREY Mission Agent (LLM + tool calling)

Command the OSPREY swarm in plain English:

```
mission> patrol a 40 m square north of here in Diamond formation at 10 m, then come back in a Line
  [ok ] get_swarm_state({}) -> State
  [ok ] set_formation({"formation": "Diamond"}) -> Formation set to Diamond.
  [ok ] set_altitude({"altitude": 10}) -> Cruise altitude set to 10.0 m.
  [ok ] set_waypoints({"waypoints": [...]}) -> Loaded 5 waypoints (4 path segments).
  [ok ] start_mission({}) -> Mission started: taking off.
  [ok ] wait_for_mission({"seconds": 60}) -> ...
```

## How it works

```
 You (natural language)
        │
        ▼
 osprey_agent.py ── Claude (tool calling: plans the mission, picks tools)
        │  HTTP / JSON (localhost:8765)
        ▼
 SwarmCommandAPI.cs (Unity) ──► MissionManager / FormationManager / WaypointManager
```

The agent observes the swarm (`get_swarm_state`), acts (`set_formation`, `set_spacing`,
`set_altitude`, `set_waypoints`, `start_mission`, `cancel_mission`) and monitors progress
(`wait_for_mission`) so it can run multi-phase plans. When a tool fails, the error
message is sent back to the model, and it corrects itself. Unity checks every command
(altitude 2–40 m, spacing 1.5–15 m, max 20 waypoints, ±500 m), so the LLM can't send
the swarm anywhere unsafe.

## Setup

**Unity**
1. In the `MainSimulation` scene, create an empty GameObject named `CommandAPI`.
2. Add the `SwarmCommandAPI` component. It finds MissionManager, FormationManager,
   SwarmManager, WaypointManager and the leader by itself (or assign them in the Inspector).
3. Press Play. The Console should show `[SwarmCommandAPI] Listening on http://localhost:8765/`.
   (Allow it if Windows Firewall asks.)

**Python**
```bash
cd agent
pip install -r requirements.txt
set ANTHROPIC_API_KEY=your-key        # Windows cmd (PowerShell: $env:ANTHROPIC_API_KEY="your-key")
python osprey_agent.py                # interactive
python osprey_agent.py -c "fly a 30 m triangle in Wedge formation"
python osprey_agent.py --mock         # try it without Unity (fake simulator)
```

Use a different model with `--model` or the `OSPREY_MODEL` environment variable.

## Test the API without the agent

```bash
curl http://localhost:8765/state
curl -X POST http://localhost:8765/command -d "{\"action\":\"set_formation\",\"formation\":\"Circle\"}"
```

## World frame

+X = east/right, +Z = north/forward, Y = altitude (metres). Waypoints are `(x, z)`.
The altitude comes from `set_altitude`.
