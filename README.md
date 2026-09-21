# OSPREY

### Operational Swarm Platform for Reconnaissance, Exploration & Yield

A Unity 6 LTS–based UAV swarm simulation platform for autonomous formation flight, tactical surveillance, command-and-control operations, and AI-ready drone research.

---

## About OSPREY

**OSPREY** is a modular drone swarm simulator that recreates how multiple unmanned aerial vehicles coordinate, navigate, and operate as a single intelligent system.

The project combines realistic quadcopter physics, autonomous follower drones, tactical camera systems, live telemetry, and obstacle sensing inside a Unity HDRP environment.

Its architecture is designed so future AI modules can be integrated without changing the simulation core.

---

## What OSPREY stands for

| Letter | Meaning |
|---------|---------|
| **O** | **Operational** — realistic UAV mission execution and testing |
| **S** | **Swarm** — coordinated multi-drone autonomous flight |
| **P** | **Platform** — modular simulation framework |
| **R** | **Reconnaissance** — surveillance and observation missions |
| **E** | **Exploration** — navigation through unknown environments |
| **Y** | **Yield** — successful mission completion through coordinated behavior |

---

# Features

- Autonomous leader–follower swarm architecture
- Physics-based quadcopter flight
- V-formation flight
- Manual leader control
- Chase camera
- Tactical overhead camera
- Live HUD (Altitude, Speed, Battery & Heading)
- 3-ray obstacle sensing system
- HDRP simulation environment
- Modular AI-ready codebase

---

# Controls

| Key | Action |
|------|--------|
| **W A S D** | Move leader drone |
| **Q / E** | Rotate (Yaw) |
| **Space** | Ascend |
| **Left Shift** | Descend |
| **C** | Chase Camera |
| **V** | Tactical Camera |
| **M** | Manual Flight Mode |
| **L** | Leader Control Mode |

---

# System Architecture

```text
                  OSPREY

            Main Simulation Scene
                     │
     ┌───────────────┼───────────────┐
     │               │               │
 Leader Drone   Swarm Manager   Formation Manager
     │               │               │
     └───────────────┼───────────────┘
                     │
             Follower Drones
                     │
          Obstacle Avoidance Sensors
                     │
               Unity HDRP Physics
```

---

# Project Structure

```text
Assets/
│
├── Prefabs/
│   └── DronePrefab
│
├── Scripts/
│   ├── DroneController.cs
│   ├── FollowerDrone.cs
│   ├── SwarmManager.cs
│   ├── FormationManager.cs
│   ├── CameraManager.cs
│   ├── ObstacleAvoidance.cs
│   └── HUDController.cs
│
├── Materials/
├── Scenes/
│   └── MainSimulation
│
└── Resources/
```

---

# Current Version — v0.4.1

### Completed

- [x] Drone physics
- [x] Leader controls
- [x] Swarm spawning
- [x] V formation
- [x] Chase camera
- [x] Tactical camera
- [x] HUD telemetry
- [x] 3-ray obstacle sensing

---

# Roadmap

## Version 0.5

- AI obstacle avoidance
- Formation recovery
- Dynamic spacing
- Collision-free navigation

## Version 0.8

- Waypoint missions
- Autonomous patrol routes
- Multi-formation switching
- Mission recording

## Version 1.0

- Fully autonomous swarm navigation
- Search & reconnaissance missions
- Computer vision integration
- Real-time command interface

---

# Technologies

- **Engine:** Unity 6 LTS
- **Language:** C#
- **Render Pipeline:** HDRP
- **Physics:** Rigidbody
- **Architecture:** Component-based modular system

---

# Author

**Himanshu Sharma**

M.Tech — Computer Science & Engineering

JC Bose University of Science & Technology

**Project:** OSPREY — Operational Swarm Platform for Reconnaissance, Exploration & Yield
