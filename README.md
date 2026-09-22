# OSPREY - Autonomous Drone Swarm Simulator

> A Unity-based autonomous UAV swarm simulator implementing PID flight control, Pure Pursuit navigation, Bézier trajectory planning, and matrix-based swarm formations.

![Unity](https://img.shields.io/badge/Unity-6-black)
![Language](https://img.shields.io/badge/C%23-.NET-purple)
![Status](https://img.shields.io/badge/Status-Active-success)
![Version](https://img.shields.io/badge/Version-v1.0-blue)

---

# Overview

OSPREY is an autonomous drone swarm simulation project developed in Unity using C#.

The project focuses on robotics, autonomous navigation, and swarm coordination principles rather than simple scripted movement.

The simulator implements:

- Autonomous takeoff and landing
- PID flight control
- Pure Pursuit path tracking
- Bézier trajectory generation
- Matrix-based swarm formations
- Finite State Mission System
- Multi-drone coordination

The objective is to build a foundation for future autonomous systems such as:

- Search & Rescue
- Obstacle Avoidance
- Occupancy Grid Mapping
- Drone-to-Drone Communication
- AI Tactical Swarms

---

# Key Engineering Concepts

## 1. Matrix-Based Formation Engine

Instead of storing world positions for each drone, followers store local offsets relative to the leader.

### Mathematical Model

P(i) = P(leader) + R × O(i)

Where:

- P(i) = follower position
- P(leader) = leader position
- R = leader rotation matrix
- O(i) = formation offset

### Benefits

- Unlimited followers
- Automatic rotation
- Dynamic formation switching
- Low computational cost
- Realistic swarm behavior

---

## 2. PID Flight Controller

The drone heading is controlled using a Proportional-Derivative (PD) controller.

### Equation

u = Kp × e + Kd × de/dt

Where:

- e = heading error
- Kp = proportional gain
- Kd = derivative gain

### Benefits

- Smooth turns
- Reduced oscillation
- Stable steering
- Realistic UAV movement

---

## 3. Bézier Trajectory Planning

Straight-line waypoint navigation produces robotic motion.

OSPREY generates smooth cubic Bézier curves between waypoints.

### Equation

B(t) = (1-t)^3P0
     + 3(1-t)^2tP1
     + 3(1-t)t^2P2
     + t^3P3

Where:

- P0 = start waypoint
- P3 = destination waypoint
- P1,P2 = control points

### Benefits

- Smooth trajectories
- Reduced sharp turns
- Better path tracking

---

## 4. Pure Pursuit Navigation

The drone follows a look-ahead point on the generated trajectory rather than chasing waypoints directly.

### Equation

Target = ClosestPoint + LookAheadDistance

### Benefits

- Eliminates waypoint orbiting
- Prevents stopping at waypoints
- Produces continuous movement

---

## 5. Finite State Machine (FSM)

Mission logic is separated from flight control.

### Mission States

Idle
↓
Takeoff
↓
Patrol
↓
Complete

### Benefits

- Modular architecture
- Easier debugging
- Scalable mission logic

---

# Features

## Autonomous Flight

- Automatic takeoff
- Cruise altitude hold
- Waypoint navigation
- Autonomous mission execution
- Mission completion detection

## Flight Control

- PID steering
- Hover controller
- Physics-based movement
- Banked turns
- Smooth acceleration

## Swarm Coordination

- Leader-Follower architecture
- Dynamic formations
- Matrix transformations
- Runtime formation switching

## Path Planning

- Cubic Bézier paths
- Pure Pursuit tracking
- Curved waypoint navigation

---

# Implemented Formations

- Line
- V Formation
- Diamond
- Circle
- Wedge
- Arrow
- Column
- Cross
- Plus
- Custom Runtime Formations

---

# Software Architecture

```text
MissionManager
│
├── PathPlanner
│
├── DroneController
│
├── SwarmManager
│
└── WaypointManager
```

## Module Responsibilities

| Module | Responsibility |
|----------|----------------|
| MissionManager | Mission FSM |
| PathPlanner | Bézier trajectory generation |
| DroneController | PID + Pure Pursuit control |
| SwarmManager | Formation management |
| WaypointManager | Mission waypoints |

---

# Project Structure

```text
OSPREY/
│
├── Assets/
│   │
│   ├── Scripts/
│   │   ├── DroneController.cs
│   │   ├── MissionManager.cs
│   │   ├── PathPlanner.cs
│   │   ├── SwarmManager.cs
│   │   ├── FormationManager.cs
│   │   └── WaypointManager.cs
│   │
│   ├── Prefabs/
│   ├── Materials/
│   └── Scenes/
│
├── README.md
│
└── docs/
    └── OSPREY_Architecture.md
```

---

# Controls

| Key | Action |
|------|--------|
| W | Forward |
| S | Backward |
| A | Left |
| D | Right |
| Q | Rotate Left |
| E | Rotate Right |
| P | Start Mission |
| ESC | Cancel Mission |

---

# Technologies Used

- Unity 6
- C#
- Rigidbody Physics
- Linear Algebra
- PID Control
- Bézier Curves
- Pure Pursuit Navigation
- Finite State Machines

---

# Bugs Solved

| Problem | Solution |
|----------|----------|
| Followers breaking after 5 drones | Matrix formation system |
| Formation rotation issues | Rotation matrix transformation |
| Waypoint orbiting | Pure Pursuit controller |
| WP1 freeze | Continuous velocity tracking |
| Robotic turning | PID steering |
| Formation switching issues | Runtime formation manager |

---

# Current Progress

| Module | Completion |
|----------|------------|
| Flight Physics | 100% |
| PID Controller | 100% |
| Pure Pursuit | 100% |
| Bézier Planner | 100% |
| Formation Engine | 95% |
| Mission System | 95% |
| Obstacle Avoidance | 0% |
| Mapping | 0% |
| Swarm AI | 10% |

Overall Project Completion: **45%**

---

# Future Roadmap

## v1.1

- RRT* Global Path Planner
- Obstacle Avoidance
- Dynamic Replanning

## v1.2

- Lidar Sensor Model
- Occupancy Grid Mapping
- Environment Awareness

## v1.3

- Drone-to-Drone Communication
- Shared World Map
- Formation Consensus

## v2.0

- Tactical Swarm AI
- Search & Rescue Mission
- Autonomous Area Coverage
- Thermal Target Detection

---

# Why OSPREY?

Most Unity drone projects simply move objects between predefined points.

OSPREY focuses on actual robotics and autonomous systems concepts:

- Matrix-based swarm coordination
- PID flight control
- Pure Pursuit navigation
- Bézier trajectory generation
- Modular mission architecture

The project is designed as a foundation for advanced UAV autonomy and swarm intelligence research.

---

# Author

**Himanshu Sharma**

B.Tech Computer Science & Engineering

Autonomous Systems | UAV Simulation | AI & Robotics

GitHub:
https://github.com/himaanshushrma

---

# License

This project is released under the MIT License.
