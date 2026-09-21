# OSPREY — Swarm UAV Simulation

> Version 0.4.1 (Unity 6 LTS)

OSPREY is a command-and-control UAV swarm simulator built in Unity. The project focuses on autonomous formation flight, tactical camera systems, obstacle sensing, and AI-ready swarm architecture.

---

## Features

- Manual leader drone flight
- Autonomous follower drones
- V-formation system
- Dual camera modes
- Real-time HUD
- 3-ray obstacle sensing
- Physics-based quadcopter movement
- Modular AI architecture

---

## Controls

| Key | Action |
|------|--------|
| W A S D | Move leader |
| Q / E | Rotate |
| Space | Ascend |
| Left Shift | Descend |
| C | Chase Camera |
| V | Tactical Camera |
| M | Manual Mode |
| L | Leader Mode |

---

## Project Structure

Assets/

Scripts/

DroneController.cs

FollowerDrone.cs

SwarmManager.cs

FormationManager.cs

CameraManager.cs

ObstacleAvoidance.cs

HUDController.cs

Prefabs/

DronePrefab

Scenes/

MainSimulation

---

## Camera Modes

### Chase Camera
- Third-person cinematic view
- Follows the leader smoothly
- Shows the complete swarm

### Tactical Camera
- Top-down battlefield overview
- Automatically frames the swarm
- Designed for command-and-control operations

---

## Swarm Architecture

Leader Drone

↓

Swarm Manager

↓

Formation Manager

↓

Follower Drones

↓

Obstacle Sensors

Each follower calculates its formation offset while maintaining separation and obstacle awareness.

---

## Current Milestone (v0.4.1)

- [x] Drone physics
- [x] Leader controls
- [x] V formation
- [x] Swarm spawning
- [x] HUD
- [x] Chase camera
- [x] Tactical camera
- [x] 3-ray obstacle sensing

---

## Roadmap

### Version 0.5
- AI obstacle avoidance
- Formation recovery
- Dynamic spacing
- Collision-free navigation

### Version 1.0
- Waypoint missions
- GPS navigation
- Multi-formation switching
- Autonomous reconnaissance
- Mission recording & replay

---

## Built With

- Unity 6 LTS
- C#
- HDRP
- Rigidbody Physics

---

## Author

**Himanshu Sharma**

M.Tech CSE • JC Bose University of Science & Technology

Project: OSPREY Swarm Simulation
