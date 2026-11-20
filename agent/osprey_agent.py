"""
OSPREY Mission Agent
====================

An LLM agent that turns natural-language commands into swarm missions.

    "Patrol a 40 m square north of here in Diamond formation at 10 m altitude"

The agent (Claude, via tool calling) plans the mission and then calls OSPREY's
SwarmCommandAPI tools: it reads the swarm state, sets the formation, spacing,
altitude and waypoints, starts the mission, and can watch it until it ends
to run multi-step plans ("...then switch to Circle and come back").

Usage
-----
    pip install -r requirements.txt
    set ANTHROPIC_API_KEY=...        (Windows)   /   export ANTHROPIC_API_KEY=...

    python osprey_agent.py             # talk to Unity (Play mode, port 8765)
    python osprey_agent.py --mock      # no Unity: built-in fake simulator
    python osprey_agent.py -c "fly a triangle in Wedge formation"   # one-shot
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time

from osprey_client import FORMATIONS, MockSimulator, SimulatorError, UnityClient, discover

DEFAULT_MODEL = os.environ.get("OSPREY_MODEL", "claude-sonnet-5-5")
MAX_AGENT_STEPS = 25
MAX_WAIT_SECONDS = 60

SYSTEM_PROMPT = f"""You are the mission-planning agent for OSPREY, a Unity UAV swarm simulator.
You command one leader drone; follower drones hold a formation around it automatically.

World frame (metres): +X = east/right, +Z = north/forward, Y = altitude.
Waypoints are given as (x, z); altitude comes from the cruise altitude setting.

The world (if built with OSPREY > Build World):
- Base around (0, -8): helipad and home point. Keep missions starting and ending here.
- City north: x -90..120, z 65..185. Towers up to ~60 m; cruise >= 65 m to overfly, or route around.
- River east at x 142..158 with a bridge at z 105. Forest and low hills west (x < -40).
- Fields and huts south (z < -45) and across the river (x > 165). Trees top out around 10 m.
- Six survivors (rescue targets) are hidden in the forest and across the river.
The drones avoid obstacles by themselves (steer, brake, climb over low ones), but plan
sensible routes and altitudes anyway: 15-20 m clears trees and huts.
The leader carries a camera + object detector. get_swarm_state returns "detections"
(label, confidence, position, distance), "survivorsFound"/"survivorsTotal" and
"survivorPositions". 
v2.0 abilities:
- set_waypoints plans every leg with RRT* around buildings automatically, so you may put
  waypoints on the far side of the city; the reply says how many detour points were added.
- For searches use search_area: it sizes lawnmower lanes from the swarm width and camera
  footprint, plans the legs, tracks coverage, and returns home when done. Then use
  wait_for_mission until searchActive is false, and report coverage, survivors found
  (survivorPositions / survivorFinders) and where.
- The swarm shares a sensor map (mapExploredPercent) and a radio link (commsOnline /
  commsTotal). If drones are lost, mention it; the formation re-forms by itself.
v3.0 abilities:
- Battery: batteryPercent (leader), swarmBatteryMin, followerBatteries and
  batteryNeededToGetHome. Missions and searches are checked before they start and refused
  if the swarm cannot finish and get home with a 10% reserve; explain the refusal and offer
  a smaller area. During flight the swarm returns home by itself when the battery reaches
  what it needs to get home (batteryEvent). Landed drones on the pad charge.
- search_area with split=true: every drone searches its own strip at the same time
  (followers fly independently, altitudes staggered), so large areas finish much faster.
  Use it for areas wider than about 120 m, or when the user asks for speed.
- visionSource is "yolov8" when the real neural network is running, otherwise "simulated".

How to work:
1. Always call get_swarm_state first to learn where the leader is and what is running.
2. Turn the user's request into concrete numbers. Interpret directions and distances
   relative to the leader's current position unless the user gives absolute coordinates.
   Shapes: build them from waypoints (e.g. a square = 4 corners plus returning to the start
   if the user wants a closed loop; a circle/orbit = 8-12 points on the circle).
3. Configure before flying: set_formation / set_spacing / set_altitude as needed, then
   set_waypoints, then start_mission.
4. Waypoints cannot change while a mission is running; call cancel_mission first.
5. If the request has several phases ("then", "after that", "when you get there"), use
   wait_for_mission to watch progress, then do the next phase.
6. If a tool returns ok=false, read the message, fix the problem and retry once. If you
   still cannot do it, explain why.
7. Be safe: stay within ±500 m, altitude 2-40 m, spacing 1.5-15 m, at most 20 waypoints.
   If a request is ambiguous in a way that matters (e.g. no size given for an area), pick a
   sensible default and say what you chose.

Valid formations: {", ".join(FORMATIONS)}.
Finish with a short summary of what the swarm is doing (formation, altitude, route)."""

TOOLS = [
    {
        "name": "get_swarm_state",
        "description": "Get the current mission state, flight state (Landed/Flying/...), leader "
                       "position/heading/speed/height above ground, formation, spacing, cruise altitude, "
                       "waypoints, obstacle-avoidance status and camera detections incl. survivors found.",
        "input_schema": {"type": "object", "properties": {}},
    },
    {
        "name": "set_formation",
        "description": "Change the swarm formation. Can be called during a mission.",
        "input_schema": {
            "type": "object",
            "properties": {"formation": {"type": "string", "enum": FORMATIONS}},
            "required": ["formation"],
        },
    },
    {
        "name": "set_spacing",
        "description": "Set distance in metres between drones in the formation (1.5-15).",
        "input_schema": {
            "type": "object",
            "properties": {"spacing": {"type": "number", "minimum": 1.5, "maximum": 15}},
            "required": ["spacing"],
        },
    },
    {
        "name": "set_altitude",
        "description": "Set cruise altitude in metres (2-40). Applies at once if flying.",
        "input_schema": {
            "type": "object",
            "properties": {"altitude": {"type": "number", "minimum": 2, "maximum": 40}},
            "required": ["altitude"],
        },
    },
    {
        "name": "set_waypoints",
        "description": "Load the mission route as a list of (x, z) points in world metres. "
                       "By default the leader's current position is added as the start point. "
                       "Only allowed when no mission is running.",
        "input_schema": {
            "type": "object",
            "properties": {
                "waypoints": {
                    "type": "array",
                    "minItems": 1,
                    "maxItems": 20,
                    "items": {
                        "type": "object",
                        "properties": {"x": {"type": "number"}, "z": {"type": "number"}},
                        "required": ["x", "z"],
                    },
                },
                "from_current_position": {"type": "boolean", "default": True},
            },
            "required": ["waypoints"],
        },
    },
    {
        "name": "start_mission",
        "description": "Take off to cruise altitude and fly the loaded route with the swarm.",
        "input_schema": {"type": "object", "properties": {}},
    },
    {
        "name": "cancel_mission",
        "description": "Stop the current mission; the swarm hovers in place (manual mode).",
        "input_schema": {"type": "object", "properties": {}},
    },
    {
        "name": "takeoff",
        "description": "Take off from the ground to a low hover (only when landed).",
        "input_schema": {"type": "object", "properties": {}},
    },
    {
        "name": "land",
        "description": "Cancel any mission and land at the current position.",
        "input_schema": {"type": "object", "properties": {}},
    },
    {
        "name": "return_home",
        "description": "Cancel any mission, fly home at a safe height (>= 20 m) and land on the helipad.",
        "input_schema": {"type": "object", "properties": {}},
    },
    {
        "name": "search_area",
        "description": "Autonomous search of a rectangle (world metres): lawnmower lanes sized to the "
                       "swarm, RRT*-planned legs, coverage tracking, every drone's camera detects "
                       "survivors, then return home and land. Only when no mission is running.",
        "input_schema": {
            "type": "object",
            "properties": {
                "x0": {"type": "number"}, "z0": {"type": "number"},
                "x1": {"type": "number"}, "z1": {"type": "number"},
                "altitude": {"type": "number", "minimum": 8, "maximum": 40, "default": 25},
                "split": {"type": "boolean", "default": False,
                          "description": "v3.0 split search: each drone takes its own strip in parallel"},
            },
            "required": ["x0", "z0", "x1", "z1"],
        },
    },
    {
        "name": "wait_for_mission",
        "description": "Wait up to `seconds` (max 60) while the mission runs, returning early when "
                       "it completes or the leader reaches a new waypoint segment. Returns state.",
        "input_schema": {
            "type": "object",
            "properties": {"seconds": {"type": "number", "minimum": 1, "maximum": MAX_WAIT_SECONDS}},
            "required": ["seconds"],
        },
    },
]


class MissionAgent:
    def __init__(self, sim, model: str = DEFAULT_MODEL, verbose: bool = True):
        try:
            import anthropic
        except ImportError:
            sys.exit("Missing dependency: pip install -r requirements.txt")

        if not os.environ.get("ANTHROPIC_API_KEY"):
            sys.exit("Set the ANTHROPIC_API_KEY environment variable first.")

        self.llm = anthropic.Anthropic()
        self.sim = sim
        self.model = model
        self.verbose = verbose
        self.messages: list[dict] = []  # conversation memory across commands

    # ---------------- tool execution ----------------
    def run_tool(self, name: str, args: dict) -> dict:
        try:
            if name == "get_swarm_state":
                return self.sim.get_state()
            if name == "set_formation":
                return self.sim.command("set_formation", formation=args["formation"])
            if name == "set_spacing":
                return self.sim.command("set_spacing", spacing=args["spacing"])
            if name == "set_altitude":
                return self.sim.command("set_altitude", altitude=args["altitude"])
            if name == "set_waypoints":
                return self.sim.command(
                    "set_waypoints",
                    waypoints=[{"x": float(p["x"]), "z": float(p["z"])} for p in args["waypoints"]],
                    fromCurrentPosition=bool(args.get("from_current_position", True)),
                )
            if name == "start_mission":
                return self.sim.command("start_mission")
            if name == "cancel_mission":
                return self.sim.command("cancel_mission")
            if name in ("takeoff", "land", "return_home"):
                return self.sim.command(name)
            if name == "search_area":
                return self.sim.command("search_area", x0=float(args["x0"]), z0=float(args["z0"]),
                                        x1=float(args["x1"]), z1=float(args["z1"]),
                                        altitude=float(args.get("altitude", 25)),
                                        split=bool(args.get("split", False)))
            if name == "wait_for_mission":
                return self.wait_for_mission(float(args.get("seconds", 10)))
            return {"ok": False, "message": f"Unknown tool {name}"}
        except SimulatorError as e:
            return {"ok": False, "message": str(e)}

    def wait_for_mission(self, seconds: float) -> dict:
        seconds = max(1.0, min(seconds, MAX_WAIT_SECONDS))
        start = self.sim.get_state()
        s0 = start.get("state", {})
        deadline = time.monotonic() + seconds
        last = start
        while time.monotonic() < deadline:
            time.sleep(0.5)
            last = self.sim.get_state()
            s = last.get("state", {})
            if s.get("missionState") != s0.get("missionState") or \
               s.get("currentSegment") != s0.get("currentSegment"):
                break
        last["message"] = "Waited; returning current state."
        return last

    # ---------------- agent loop ----------------
    def ask(self, user_text: str) -> str:
        self.messages.append({"role": "user", "content": user_text})

        for _ in range(MAX_AGENT_STEPS):
            resp = self.llm.messages.create(
                model=self.model,
                max_tokens=2048,
                system=SYSTEM_PROMPT,
                tools=TOOLS,
                messages=self.messages,
            )
            self.messages.append({"role": "assistant", "content": resp.content})

            if resp.stop_reason != "tool_use":
                return "".join(b.text for b in resp.content if b.type == "text").strip()

            results = []
            for block in resp.content:
                if block.type != "tool_use":
                    continue
                out = self.run_tool(block.name, block.input)
                if self.verbose:
                    status = "ok " if out.get("ok", True) else "ERR"
                    print(f"  [{status}] {block.name}({json.dumps(block.input)}) -> {out.get('message', '')}")
                results.append({
                    "type": "tool_result",
                    "tool_use_id": block.id,
                    "content": json.dumps(out),
                    "is_error": not out.get("ok", True),
                })
            self.messages.append({"role": "user", "content": results})

        return "Stopped: too many steps for one command."


def main():
    ap = argparse.ArgumentParser(description="OSPREY natural-language mission agent")
    ap.add_argument("--mock", action="store_true", help="use the built-in fake simulator instead of Unity")
    ap.add_argument("--url", default=None, help="SwarmCommandAPI address (default: auto-detect ports 8765-8770)")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("-c", "--command", help="run one command and exit")
    ap.add_argument("-q", "--quiet", action="store_true", help="hide tool calls")
    args = ap.parse_args()

    if args.mock:
        sim = MockSimulator()
    else:
        try:
            url = args.url or discover()
            sim = UnityClient(url)
            sim.get_state()
            print(f"Connected to OSPREY at {url}")
        except SimulatorError as e:
            sys.exit(str(e))

    agent = MissionAgent(sim, model=args.model, verbose=not args.quiet)

    if args.command:
        print(agent.ask(args.command))
        return

    print("OSPREY Mission Agent" + (" (mock simulator)" if args.mock else "") + ". Type 'exit' to quit.")
    while True:
        try:
            text = input("\nmission> ").strip()
        except (EOFError, KeyboardInterrupt):
            break
        if text.lower() in ("exit", "quit"):
            break
        if text:
            print("\n" + agent.ask(text))


if __name__ == "__main__":
    main()
