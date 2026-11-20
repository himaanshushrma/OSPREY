# OSPREY — Autonomous Drone Swarm Simulator

> A Unity 6 autonomous UAV swarm simulator implementing **PID flight control**, **Pure Pursuit navigation**, **Bézier trajectory planning**, and **matrix-based swarm formations**.

![Unity](https://img.shields.io/badge/Unity-6-black)
![Language](https://img.shields.io/badge/C%23-.NET-purple)
![Version](https://img.shields.io/badge/Version-v1.0-blue)
![Status](https://img.shields.io/badge/Status-Active-success)

---

## Project Overview

**OSPREY (Operational Swarm Platform for Reconnaissance, Exploration & Yield)** is a robotics-oriented drone swarm simulator built in **Unity** using **C#**.

Unlike conventional Unity drone projects that rely on scripted movement, OSPREY separates **mission planning**, **trajectory generation**, **flight control**, and **swarm coordination** into independent modules. The project serves as a foundation for future autonomous UAV research including obstacle avoidance, mapping, distributed swarms, and search & rescue.

### Current Capabilities

- Autonomous takeoff and landing
- PID / PD heading controller
- Pure Pursuit path tracking
- Cubic Bézier trajectory generation
- Matrix-based formation engine
- Leader–Follower swarm architecture
- Runtime formation switching
- Mission finite state machine (FSM)
- **LLM mission agent**: natural-language commands → swarm missions via tool calling ([agent/](agent/README.md))

---

# System Architecture

```text
                    ┌─────────────────────┐
                    │   MissionManager    │
                    │   Mission FSM       │
                    └─────────┬───────────┘
                              │
                              ▼
                    ┌─────────────────────┐
                    │    PathPlanner      │
                    │  Bézier Generator   │
                    └─────────┬───────────┘
                              │
                              ▼
                    ┌─────────────────────┐
                    │  DroneController    │
                    │ PID + Pure Pursuit  │
                    └─────────┬───────────┘
                              │
                              ▼
                    ┌─────────────────────┐
                    │    SwarmManager     │
                    │ Leader + Followers  │
                    └─────────────────────┘
```

### Module Responsibilities

| Module | Responsibility |
|---------|----------------|
| `MissionManager` | Mission sequencing & FSM |
| `PathPlanner` | Cubic Bézier trajectory generation |
| `DroneController` | Manual & autonomous flight control |
| `SwarmManager` | Follower spawning & coordination |
| `FormationManager` | Formation geometry |
| `WaypointManager` | Mission waypoint storage |
| `SwarmCommandAPI` | Local HTTP API used by the LLM mission agent |

---

# Core Engineering Concepts

## 1. Matrix-Based Formation Engine

Each follower stores a **local offset** relative to the leader rather than a fixed world position.

### Mathematical Model

```text
Pi = Pleader + R × Oi
```

Where:

- **Pi** = follower position
- **Pleader** = leader position
- **R** = leader rotation matrix
- **Oi** = formation offset

### Why this approach?

The original implementation used hardcoded follower positions, which failed as the swarm grew. Matrix transformations allow formations to rotate naturally with the leader while supporting an arbitrary number of drones.

**Benefits**

- Unlimited follower count
- Dynamic formation switching
- Automatic rotation
- Low computational overhead
- Clean separation of geometry and control

---

## 2. PID / PD Flight Controller

The autonomous leader uses a **Proportional–Derivative** controller for heading correction.

### Control Equation

```text
u = Kp·e + Kd(de/dt)
```

Where:

- **e** = heading error
- **Kp** = proportional gain
- **Kd** = derivative damping

This produces smooth turns and eliminates abrupt heading changes.

---

## 3. Bézier Trajectory Planning

Instead of flying directly between waypoints, OSPREY generates smooth cubic Bézier trajectories.

```text
B(t) = (1-t)³P0
     + 3(1-t)²tP1
     + 3(1-t)t²P2
     + t³P3
```

The generated curve is sampled into intermediate navigation points for the flight controller.

**Advantages**

- Smooth trajectories
- Reduced cornering error
- Better path tracking
- Natural UAV movement

---

## 4. Pure Pursuit Navigation

The drone follows a **look-ahead target** rather than the waypoint itself.

```text
Closest Path Point
        │
        ▼
  Look Ahead Target
        │
        ▼
   Steering Command
```

This solved the major navigation bugs encountered during development:

- Waypoint orbiting
- WP1 freeze
- Oscillation near targets
- Abrupt stopping

---

## 5. Mission Finite State Machine

Mission logic is isolated from flight dynamics.

```text
Idle
 ↓
Takeoff
 ↓
Patrol
 ↓
Complete
```

Separating **decision making** from **vehicle control** makes the simulator easier to debug and extend.

---

# Features

## Autonomous Flight

- Automatic takeoff
- Cruise altitude hold
- Waypoint patrol
- Mission completion
- Manual override

## Flight Control

- Rigidbody physics
- Hover controller
- PD heading control
- Smooth steering
- Banked visual turns

## Swarm Coordination

- Leader–Follower architecture
- Runtime follower spawning
- Matrix formation engine
- Dynamic formations
- Scalable swarm size

## Trajectory Planning

- Cubic Bézier curves
- Pure Pursuit tracking
- Continuous waypoint transitions

---

# Implemented Formations

- V Formation
- Line
- Diamond
- Wedge
- Arrowhead
- Column
- Circle
- Grid
- Echelon Left
- Echelon Right

All formations are generated procedurally using leader-relative coordinates.

---

# v2.0: Mapping, Planning, Comms & Search

- **OccupancyGrid**: 100×100 cells (4 m) shared map. Prior scan at start + live mapping from every
  drone's sensors, delivered over the radio. Minimap overlay (M) shows explored area, obstacles,
  the planned route, search coverage, drones and survivors.
- **RRT\* planner** (`Planning/RRTStarPlanner.cs`): choose-parent + rewire, segment checks on the map
  with formation-width inflation, path shortcutting; `PathPlanner` + `BezierSmoother` round the corners.
  Every mission (API, P key, search) is planned around obstacles before take-off.
  Tested: base → north of the city at 15 m planned around the city in 0.9 s.
- **SwarmComms**: simulated radio (range, packet loss, 10 Hz). Followers fly on received leader
  state with dead reckoning; heartbeats detect lost drones and the formation re-forms (K = fail a drone).
- **SearchMission**: lawnmower lanes sized from swarm width + camera footprint, coverage tracking,
  every drone's camera detects survivors, then return home and land. API `search_area`, agent tool.
  Tested: 135×225 m forest, 100% coverage in 115 s, 3 survivors found, one drone failed mid-search.

# v3.0: Battery, Split Search, Real Vision & ROS 2

- **Battery-aware planning** (`DroneBattery`, `BatteryManager`): every drone drains power for hover +
  speed² + climb; landed drones charge on the pad. Missions and searches are checked before take-off
  (route + flight home + 10% reserve) and refused with a reason if the swarm can't make it. In flight,
  **smart return home** triggers when the swarm battery reaches what it needs to get home.
  Tested: battery forced to 22% mid-search → search stopped at 52% coverage, swarm flew home and
  landed with 12% (above the 10% reserve), then charged.
- **Split search** (`split_search`, or `search_area` with `"split": true`): the area is cut into one
  strip per drone; followers leave the formation and fly their own RRT*-planned lanes at staggered
  altitudes, maps and survivor reports merge over the radio, then everyone rejoins and goes home.
  Tested on the same 130×220 m forest: **100% coverage in 67 s vs 115 s** for the v2.0 formation
  search (−42%), 3 survivors found by 3 different followers.
- **Real YOLOv8 vision** (`DatasetCapture`, `VisionClient`, `vision/`): a synthetic-data generator
  renders drone-camera frames with randomised survivor positions, scale and sun angle and writes
  auto-labelled YOLO datasets (person / hut / building / tree), labels verified visually.
  `train_yolo.py` fine-tunes YOLOv8n on an NVIDIA GPU; `vision_server.py` serves it, and the leader
  streams its camera at 5 fps, draws the real boxes picture-in-picture (Y) and ray-casts every
  `person` box to the ground to report survivors. Falls back to the simulated detector when offline.
  See [vision/README.md](vision/README.md).
- **ROS 2 bridge** (`ros2/osprey_bridge.py`): publishes pose, swarm PoseArray, `nav_msgs/OccupancyGrid`
  (`GET /map`), RRT* `Path`, survivor markers and `BatteryState`; RViz *2D Goal Pose* sends the swarm
  to a point; take-off / land / return-home as services. RViz config included. See
  [ros2/README.md](ros2/README.md) (WSL2 setup; PX4 SITL mapping documented as future work).

# Controls

| Key | Action |
|------|--------|
| **W A S D** | Forward / back / strafe |
| **Q / E** | Yaw |
| **Space / Left Shift** | Climb / descend (release = hover, altitude hold) |
| **T / L** | Take off / land |
| **G** | Return to home (climbs to 20 m, flies home, lands) |
| **X** | Sport mode (faster manual flight) |
| **P / Esc** | Start / cancel autonomous mission |
| **1 - 0** | Formations |
| **C / V** | Chase / tactical camera |
| **O** | Object-detection overlay |
| **M** | Swarm map (minimap) |
| **K** | Simulate a drone failure |
| **F1** | Show controls in game |

# Obstacle Avoidance & Object Detection

- **CollisionAvoidance** (every drone): sphere-cast fan + 360° ring + close-contact check on the
  `Obstacle` layer. Repulsion field keeps distance, a **Vector Field Histogram** direction search
  picks the free direction closest to the goal (with a turn penalty against dithering), the drone
  brakes as gaps close and **climbs over** low obstacles (trees, huts, walls). Manual flight gets
  brake assist. Tested: a 15 m-altitude mission straight through downtown (towers to 58 m)
  completes by threading the street canyons.
- **ObjectDetector** (leader): simulated onboard camera (FOV, range, line of sight) + detector with
  YOLO-style labelled boxes and confidence (person, building, tree, hut, bridge, wall). Survivors
  (rescue targets) are logged when first seen and reported through the API to the mission agent.
- **Ground clearance**: every mode keeps a minimum height above terrain and rooftops.

# Project Structure

```text
OSPREY/
│
├── Assets/
│   ├── Scripts/
│   │   ├── DroneController.cs
│   │   ├── MissionManager.cs
│   │   ├── PathPlanner.cs
│   │   ├── SwarmManager.cs
│   │   ├── FormationManager.cs
│   │   ├── FollowerDrone.cs
│   │   └── WaypointManager.cs
│   │
│   ├── Prefabs/
│   ├── Materials/
│   └── Scenes/
│
├── README.md
└── docs/
    └── OSPREY_Architecture.md
```

---

# Development Journey

Several architectural improvements were introduced while solving real simulation problems.

| Problem | Engineering Solution |
|----------|----------------------|
| Followers failed above 4 drones | Matrix formation engine |
| Formation rotation broke | Rotation matrix transformation |
| WP1 mission freeze | Continuous Pure Pursuit tracking |
| Waypoint orbiting | Look-ahead steering target |
| Robotic turning | PD heading controller |
| Runtime formation bugs | Modular formation manager |

These changes shaped the final architecture rather than being isolated fixes.

---

# Current Progress

| System | Status |
|----------|--------|
| Flight Physics | ✅ Complete |
| PID Controller | ✅ Complete |
| Pure Pursuit | ✅ Complete |
| Bézier Planner | ✅ Complete |
| Formation Engine | ✅ Complete |
| Mission FSM | ✅ Complete |
| LLM Mission Agent | ✅ Complete |
| Obstacle Avoidance | ✅ Complete |
| Occupancy Mapping + RRT* | ✅ v2.0 |
| Swarm Comms + Search | ✅ v2.0 |
| Battery-aware Planning | ✅ v3.0 |
| Split (parallel) Search | ✅ v3.0 |
| YOLOv8 Vision Pipeline | ✅ v3.0 (train on your GPU) |
| ROS 2 Bridge | ✅ v3.0 |
| PX4 SITL backend | 🔜 Planned |

---

# Roadmap

### v1.1 — Environmental Autonomy

- RRT* global path planner
- Dynamic obstacle avoidance
- Local replanning
- Lidar sensor model

### v1.2 — Mapping

- Occupancy grid mapping
- Persistent environment map
- Shared world representation

### v1.3 — Distributed Swarm

- Drone-to-drone communication
- Shared mission state
- Formation consensus

### v2.0 — AI Swarm Missions

- Search & Rescue
- Autonomous area coverage
- Computer vision integration
- Target detection
- Tactical swarm behaviors

---

# Technology Stack

- **Engine:** Unity 6
- **Language:** C#
- **Physics:** Unity Rigidbody
- **Control:** PID / PD
- **Navigation:** Pure Pursuit
- **Trajectory:** Cubic Bézier Curves
- **Mathematics:** Linear Algebra & Rotation Matrices

---

# Author

**Himanshu Sharma**

*M.Tech Computer Science & Engineering (2026–2028)*

**J.C. Bose University of Science and Technology, YMCA**

**Focus:** Autonomous Systems • UAV Simulation • AI & Robotics

GitHub: **https://github.com/himaanshushrma**

---

## License

Released under the **MIT License**.
