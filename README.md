# OSPREY — Autonomous Drone Swarm Simulator

**Operational Swarm Platform for Reconnaissance, Exploration & Yield**

An 8-drone search-and-rescue swarm in Unity 6 that plans its own routes, maps the world as it flies, survives losing drones, manages its batteries, sees with a YOLOv8 model trained on its own synthetic data, and takes orders in plain English from an LLM agent or from ROS 2.

![Unity](https://img.shields.io/badge/Unity-6-black)
![C#](https://img.shields.io/badge/C%23-.NET-purple)
![Python](https://img.shields.io/badge/Python-3.10+-blue)
![YOLOv8](https://img.shields.io/badge/YOLOv8-Ultralytics-00FFFF)
![ROS 2](https://img.shields.io/badge/ROS_2-Humble%20%7C%20Jazzy-22314E)
![Version](https://img.shields.io/badge/Version-v3.0-brightgreen)
![License](https://img.shields.io/badge/License-MIT-yellow)

> "Search the forest west of the river and tell me where the survivors are."
> One sentence to the mission agent, and the swarm plans the lanes, flies the search, finds survivors, and comes home before its batteries run out.

---

## Results (measured in the simulator)

| Test | Result |
|---|---|
| RRT\* route from base to beyond the city at 15 m (towers up to 58 m) | planned in **0.9 s**, 347 m, no last-second dodges |
| Mission straight through downtown with obstacle avoidance | **38 s**, completed (was stalling before VFH) |
| Formation search, 135 × 225 m forest (v2.0) | **100%** coverage in **115 s**, 3 survivors |
| Split search, same forest (130 × 220 m search area, v3.0) | **100%** coverage in **67 s** (**−42%**), 3 survivors found by 3 different drones |
| Drone failure mid-search | detected in **1.0 s**, formation re-formed, mission finished |
| Battery forced to 22% mid-search | turned home by itself, landed with **12%** (reserve 10%) |
| Whole-map search at 58% battery | **refused** before take-off, with the reason |
| Synthetic dataset, 80 frames | **196** survivor boxes, auto-labelled, checked by drawing them back onto the images |

YOLOv8 accuracy (mAP) is reported by `vision/train_yolo.py` after training on your own GPU.

---

## What's inside

| Area | What OSPREY does |
|---|---|
| **Flight** | Rigidbody physics, PD heading control, Pure Pursuit path tracking, take-off / land / return home, altitude hold, ground clearance |
| **Swarm** | Leader–follower formations from `P = P_leader + R × O` (10 formations), simulated radio with packet loss and heartbeats, automatic re-forming when a drone is lost |
| **Planning** | RRT\* global planner (choose-parent, rewire, shortcutting) on a shared occupancy grid, Bézier corner smoothing, every mission planned before take-off |
| **Avoidance** | Sphere-cast fan + 360° ring, Vector Field Histogram direction search with a turn penalty, braking, climbing over low obstacles |
| **Mapping** | 100 × 100 shared occupancy grid (4 m cells), filled by every drone's sensors over the radio; minimap overlay |
| **Missions** | Mission FSM, lawnmower search sized to the swarm and camera footprint, **split search** (one strip per drone), coverage tracking |
| **Energy** | Battery model (hover + speed² + climb), pre-flight energy checks, smart return home, charging on the pad |
| **Vision** | Simulated detector, plus a **real YOLOv8** pipeline: synthetic data generator → training → GPU inference server → survivors ray-cast to the ground |
| **AI agent** | Claude-powered mission agent with 12 tools: plain English in, swarm missions out, with self-checking |
| **Integration** | Local HTTP command API, ROS 2 bridge (topics, services, RViz) |

---

## Architecture

```text
   Plain English                       RViz / ROS 2 tools
        │                                      │
        ▼                                      ▼
 ┌──────────────┐                     ┌─────────────────┐
 │ LLM mission  │                     │  osprey_bridge  │   ros2/
 │ agent        │   agent/            │  (rclpy)        │
 └──────┬───────┘                     └────────┬────────┘
        │        HTTP  :8765  /state /command /map
        └──────────────────┬───────────────────┘
                           ▼
 ┌────────────────────────────────────────────────────────────┐
 │ Unity 6 simulator                                          │
 │                                                            │
 │  SwarmCommandAPI ─► MissionManager (FSM) ─► SearchMission  │
 │                         │                      │ split     │
 │                         ▼                      ▼           │
 │  MissionRoute ─► RRTStarPlanner ◄── OccupancyGrid ◄─┐      │
 │       │          + BezierSmoother                   │      │
 │       ▼                                     sensor hits    │
 │  DroneController (leader) ──► SwarmComms ──► FollowerDrone │
 │   PD + Pure Pursuit           radio, heartbeats  ×7        │
 │   CollisionAvoidance (VFH)                                 │
 │   DroneBattery + BatteryManager                            │
 │   ObjectDetector + VisionClient ──── JPEG 5 fps ──┐        │
 └───────────────────────────────────────────────────┼────────┘
                                                     ▼
                                     vision_server.py (YOLOv8, CUDA)   vision/
```

### Key scripts

| Script | Role |
|---|---|
| `DroneController` | Manual and autonomous flight, flight states, status overlay; adds the v2/v3 systems to the leader |
| `FollowerDrone` | Flies its formation slot from radio messages, or its own route in split search |
| `FormationManager` / `SwarmManager` | Formation geometry, follower spawning |
| `MissionManager` | Mission FSM (Idle → Takeoff → Patrol → Complete) |
| `MissionRoute` | Plans every leg with RRT\* before take-off |
| `Planning/RRTStarPlanner`, `Planning/BezierSmoother` | Global planning and smoothing |
| `OccupancyGrid` | Shared map, minimap, ROS-format export |
| `CollisionAvoidance` | VFH steering, braking, climb-over, map reporting |
| `SwarmComms` | Simulated radio, heartbeats, failure handling |
| `SearchMission` | Formation and split searches, coverage tracking |
| `DroneBattery` / `BatteryManager` | Energy model, mission checks, smart return home |
| `ObjectDetector` | Simulated camera detector, survivor registry |
| `DatasetCapture` | Synthetic, auto-labelled YOLO dataset generator |
| `VisionClient` | Streams the leader camera to YOLOv8, picture-in-picture boxes |
| `SwarmCommandAPI` | HTTP API used by the agent and the ROS 2 bridge |
| `Editor/OspreyWorldBuilder` | Builds the 3D world (OSPREY ▸ Build World) |

---

## Getting started

### 1. Run the simulator

1. Open the project in **Unity 6** (6000.3, HDRP).
2. Open `Assets/MainSimulation.unity`. If the world is missing, run **OSPREY ▸ Build World**.
3. Press **Play**. The Console shows `[SwarmCommandAPI] Listening on http://localhost:8765/`.
4. Press **T** to take off, **P** to start a mission, **F1** for all controls.

### 2. Talk to it (LLM mission agent)

```bash
cd agent
python -m pip install -r requirements.txt
export ANTHROPIC_API_KEY=...        # Windows cmd: set ANTHROPIC_API_KEY=...
python osprey_agent.py
```

Try: *"Take off, fly a square 60 m wide at 20 m, then land"* or *"Split-search the forest west of the river and report survivors"*. Details in [agent/README.md](agent/README.md).

### 3. Real YOLOv8 vision (NVIDIA GPU)

```bash
python -m pip install -r vision/requirements.txt
# In Unity, Play mode with drones landed: press F9 to capture a dataset
python vision/train_yolo.py
python vision/vision_server.py --show
```

The camera feed at the top of the Game view turns green (`● YOLOv8`). Full guide in [vision/README.md](vision/README.md).

### 4. ROS 2 (WSL2 + Humble/Jazzy)

```bash
python3 ros2/osprey_bridge.py
rviz2 -d ros2/osprey.rviz
```

Click **2D Goal Pose** in RViz and the swarm flies there. Setup and topic list in [ros2/README.md](ros2/README.md).

---

## Command API

`GET /state` returns the full swarm state. `GET /map` returns the occupancy grid in ROS `nav_msgs/OccupancyGrid` layout. Everything else is a `POST /command` with a JSON body:

| Action | Example body |
|---|---|
| `set_formation` | `{"action":"set_formation","formation":"Wedge"}` |
| `set_spacing` / `set_altitude` | `{"action":"set_altitude","altitude":20}` |
| `set_waypoints` | `{"action":"set_waypoints","waypoints":[{"x":0,"z":120}],"plan":true}` |
| `start_mission` / `cancel_mission` | `{"action":"start_mission"}` |
| `takeoff` / `land` / `return_home` | `{"action":"return_home"}` |
| `search_area` | `{"action":"search_area","x0":-190,"z0":-180,"x1":-60,"z1":40,"altitude":25}` |
| `split_search` | same fields as `search_area` |
| `fail_drone` | `{"action":"fail_drone"}` (simulate a failure) |
| `capture_dataset` | `{"action":"capture_dataset","count":1500}` |
| `set_battery` | `{"action":"set_battery","percent":35}` (testing) |

---

## Controls

| Key | Action |
|---|---|
| **W A S D** | Forward / back / strafe |
| **Q / E** | Yaw |
| **Space / Left Shift** | Climb / descend (release to hover) |
| **T / L / G** | Take off / land / return home |
| **X** | Sport mode |
| **P / Esc** | Start / cancel autonomous mission |
| **1 – 0** | Formations |
| **C / V** | Chase / tactical camera |
| **O / M / Y** | Detections overlay / swarm map / YOLOv8 camera feed |
| **K** | Simulate a drone failure |
| **F9** | Capture a YOLO training dataset |
| **F1** | Show controls in game |

Formations: V, Line, Diamond, Wedge, Arrowhead, Column, Circle, Grid, Echelon Left, Echelon Right.

---

## Core ideas

**Formations.** Each follower keeps a local offset `Oᵢ` and flies to `Pᵢ = P_leader + R × Oᵢ`, so any formation rotates with the leader and scales to any number of drones.

**Control.** Heading uses PD control, `u = Kp·e + Kd·de/dt`. Paths are cubic Bézier curves, `B(t) = (1−t)³P₀ + 3(1−t)²tP₁ + 3(1−t)t²P₂ + t³P₃`, tracked with Pure Pursuit (closest point + look-ahead target), which removed waypoint orbiting and freezing.

**Planning.** RRT\* grows a tree through free space on the shared map, inflated by the formation's width. Choose-parent and rewire keep each branch on its cheapest path; shortcutting and Bézier corners make it flyable.

**Avoidance.** A Vector Field Histogram scores 24 directions by free distance and closeness to the goal, with a penalty for turning, so the drone commits to a side instead of dithering.

**Energy.** Power = hover + k·v² + climb. Before take-off the swarm checks *route + flight home + 10% reserve*; in flight it turns home the moment the charge left equals what it needs to get there.

**Sim-to-real vision.** A COCO model can't see OSPREY's survivors, so the simulator renders its own training set: random camera poses, survivor positions, scale and sun, with labels projected from ground-truth bounds.

---

## Version history

| Version | Highlights |
|---|---|
| **v0** | Single hand-flown drone prototype |
| **v1.0** | Swarm core: formation engine, PD control, Pure Pursuit, Bézier paths, mission FSM |
| **v1.1** | LLM mission agent + HTTP command API |
| **v1.2** | 3D world: city, river and bridge, forest, countryside, rescue targets |
| **v1.3** | Full flight controls: take off, land, hover, return home, sport mode |
| **v1.4** | VFH obstacle avoidance, simulated onboard object detection |
| **v2.0** | RRT\* planning, shared occupancy mapping, swarm comms, autonomous search |
| **v3.0** | Battery-aware planning, split search, YOLOv8 vision pipeline, ROS 2 bridge |

See [Releases](https://github.com/himaanshushrma/OSPREY/releases) for details.

## Roadmap (v4.0)

- Train and benchmark the YOLOv8 model, publish mAP per class
- PX4 SITL backend: fly the same ROS 2 missions on PX4 firmware in Gazebo
- On-device inference inside Unity (ONNX / Inference Engine), no server
- Harder worlds: moving survivors, wind, night and fog

---

## Project structure

```text
OSPREY/
├── Assets/
│   ├── MainSimulation.unity        # the scene (3D world + swarm)
│   ├── Scripts/                    # all simulator code (see Key scripts)
│   │   └── Planning/               # RRT*, nodes, Bézier smoothing
│   ├── Editor/OspreyWorldBuilder.cs
│   ├── Materials/OspreyWorld/
│   └── Prefabs/
├── agent/                          # LLM mission agent (Python, Claude API)
├── vision/                         # YOLOv8 training + inference server
├── ros2/                           # ROS 2 bridge + RViz config
└── README.md
```

## Tech stack

Unity 6 (HDRP) · C# · Python · Anthropic Claude API · Ultralytics YOLOv8 · OpenCV · PyTorch (CUDA) · ROS 2 (rclpy, RViz)

---

## Author

**Himanshu Sharma** · M.Tech Computer Science & Engineering (2026–2028), J.C. Bose University of Science & Technology, YMCA

Autonomous systems · UAV swarms · AI agents · computer vision

[GitHub](https://github.com/himaanshushrma) · [LinkedIn](https://www.linkedin.com/in/himaanshushrma) · [Portfolio](https://himaanshushrma.github.io)

## License

Released under the **MIT License**.
