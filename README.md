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

# Controls

| Key | Action |
|------|--------|
| **W A S D** | Manual movement |
| **Q / E** | Yaw rotation |
| **Space** | Ascend |
| **Left Shift** | Descend |
| **P** | Start autonomous mission |
| **Esc** | Cancel mission |

---

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
| Obstacle Avoidance | 🔜 Planned |
| Occupancy Mapping | 🔜 Planned |
| Swarm AI | 🔜 Planned |

**Overall Progress:** **45%**

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
