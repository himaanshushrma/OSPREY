"""
Client for OSPREY's SwarmCommandAPI (Assets/Scripts/SwarmCommandAPI.cs).

Two implementations with the same interface:
  - UnityClient: talks to the running Unity simulator over HTTP (localhost).
  - MockSimulator: an in-process fake, so the agent can be tested without Unity.
"""

from __future__ import annotations

import json
import math
import time
import urllib.error
import urllib.request

FORMATIONS = [
    "V", "Line", "Diamond", "Wedge", "EchelonLeft",
    "EchelonRight", "Column", "Circle", "Grid", "Arrowhead",
]


class SimulatorError(Exception):
    pass


def discover(host: str = "localhost", first_port: int = 8765, count: int = 6, timeout: float = 1.5) -> str:
    """Find the running SwarmCommandAPI. Unity moves to the next port if 8765 is busy."""
    for port in range(first_port, first_port + count):
        url = f"http://{host}:{port}"
        try:
            with urllib.request.urlopen(url + "/state", timeout=timeout) as resp:
                if "state" in json.loads(resp.read().decode()):
                    return url
        except Exception:
            continue
    raise SimulatorError(
        f"Cannot find OSPREY on ports {first_port}-{first_port + count - 1}. "
        "Is Unity in Play mode with SwarmCommandAPI in the scene?"
    )


class UnityClient:
    def __init__(self, base_url: str = "http://localhost:8765", timeout: float = 8.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def _request(self, method: str, path: str, payload: dict | None = None) -> dict:
        data = json.dumps(payload).encode() if payload is not None else None
        req = urllib.request.Request(
            self.base_url + path,
            data=data,
            method=method,
            headers={"Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return json.loads(resp.read().decode())
        except urllib.error.HTTPError as e:
            # The API returns a JSON body (ok=false + message) on 4xx/5xx too
            try:
                return json.loads(e.read().decode())
            except Exception:
                raise SimulatorError(f"HTTP {e.code} from simulator") from e
        except urllib.error.URLError as e:
            raise SimulatorError(
                f"Cannot reach OSPREY at {self.base_url}. Is Unity in Play mode "
                f"with SwarmCommandAPI in the scene? ({e.reason})"
            ) from e

    def get_state(self) -> dict:
        return self._request("GET", "/state")

    def command(self, action: str, **params) -> dict:
        return self._request("POST", "/command", {"action": action, **params})


class MockSimulator:
    """Rough stand-in for Unity: the leader moves toward each waypoint at a fixed speed."""

    def __init__(self, speed: float = 7.0, followers: int = 4):
        self.speed = speed
        self.state = "Idle"
        self.formation = "V"
        self.spacing = 3.0
        self.cruise = 5.0
        self.pos = [0.0, 0.5, 0.0]
        self.heading = 0.0
        self.waypoints: list[list[float]] = []
        self.segment = 0
        self.followers = followers
        self._t = time.monotonic()

    # --- simulation --------------------------------------------------
    def _tick(self):
        now = time.monotonic()
        dt, self._t = now - self._t, now
        if self.state == "Takeoff":
            self.pos[1] = min(self.cruise, self.pos[1] + 3.0 * dt)
            if abs(self.pos[1] - self.cruise) < 0.25:
                self.state = "Patrol"
        elif self.state == "Patrol":
            budget = self.speed * dt
            while budget > 0 and self.state == "Patrol":
                tx, _, tz = self.waypoints[self.segment + 1]
                dx, dz = tx - self.pos[0], tz - self.pos[2]
                d = math.hypot(dx, dz)
                if d <= budget or d < 2.0:
                    self.pos[0], self.pos[2] = tx, tz
                    budget -= d
                    self.segment += 1
                    if self.segment >= len(self.waypoints) - 1:
                        self.state = "Complete"
                else:
                    self.pos[0] += dx / d * budget
                    self.pos[2] += dz / d * budget
                    self.heading = math.degrees(math.atan2(dx, dz)) % 360
                    budget = 0

    def _snapshot(self) -> dict:
        moving = self.state in ("Takeoff", "Patrol")
        return {
            "missionState": self.state,
            "leaderMode": "Auto" if moving else "Manual",
            "formation": self.formation,
            "spacing": self.spacing,
            "cruiseHeight": self.cruise,
            "leaderPosition": {"x": round(self.pos[0], 2), "y": round(self.pos[1], 2), "z": round(self.pos[2], 2)},
            "headingDeg": round(self.heading, 1),
            "speed": self.speed if self.state == "Patrol" else 0.0,
            "followerCount": self.followers,
            "currentSegment": self.segment,
            "segmentCount": max(0, len(self.waypoints) - 1),
            "waypoints": [{"x": x, "y": y, "z": z} for x, y, z in self.waypoints],
            "availableFormations": FORMATIONS,
        }

    def _resp(self, ok: bool, msg: str) -> dict:
        return {"ok": ok, "message": msg, "state": self._snapshot()}

    # --- API ---------------------------------------------------------
    def get_state(self) -> dict:
        self._tick()
        return self._resp(True, "State")

    def command(self, action: str, **p) -> dict:
        self._tick()
        active = self.state in ("Takeoff", "Patrol")

        if action == "set_formation":
            match = next((f for f in FORMATIONS if f.lower() == str(p.get("formation", "")).lower()), None)
            if not match:
                return self._resp(False, f"Unknown formation. Valid: {', '.join(FORMATIONS)}")
            self.formation = match
            return self._resp(True, f"Formation set to {match}.")

        if action == "set_spacing":
            s = float(p.get("spacing", -1))
            if not 1.5 <= s <= 15:
                return self._resp(False, "Spacing must be between 1.5 and 15 m.")
            self.spacing = s
            return self._resp(True, f"Formation spacing set to {s:.1f} m.")

        if action == "set_altitude":
            a = float(p.get("altitude", -1))
            if not 2 <= a <= 40:
                return self._resp(False, "Altitude must be between 2 and 40 m.")
            self.cruise = a
            return self._resp(True, f"Cruise altitude set to {a:.1f} m.")

        if action == "set_waypoints":
            if active:
                return self._resp(False, "A mission is running. Call cancel_mission before changing waypoints.")
            pts = p.get("waypoints") or []
            if not pts:
                return self._resp(False, "Provide at least one waypoint.")
            wps = []
            if p.get("fromCurrentPosition", True):
                wps.append([self.pos[0], self.cruise, self.pos[2]])
            wps += [[float(w["x"]), self.cruise, float(w["z"])] for w in pts]
            if len(wps) < 2:
                return self._resp(False, "A mission needs at least 2 points.")
            self.waypoints, self.segment = wps, 0
            return self._resp(True, f"Loaded {len(wps)} waypoints ({len(wps) - 1} path segments).")

        if action == "start_mission":
            if active:
                return self._resp(False, f"Mission already running ({self.state}).")
            if len(self.waypoints) < 2:
                return self._resp(False, "Need at least 2 waypoints. Call set_waypoints first.")
            # Like Unity, the path starts at waypoint 0
            self.segment, self.state = 0, "Takeoff"
            return self._resp(True, "Mission started: taking off.")

        if action == "cancel_mission":
            self.state = "Idle"
            return self._resp(True, "Mission cancelled. Swarm switched to manual hover.")

        if action == "search_area":
            if active:
                return self._resp(False, "A mission is running. Cancel it first.")
            x0, x1 = sorted([float(p["x0"]), float(p["x1"])])
            z0, z1 = sorted([float(p["z0"]), float(p["z1"])])
            lanes = max(1, int((x1 - x0) // 60) + 1)
            wps = [[self.pos[0], self.cruise, self.pos[2]]]
            for k in range(lanes):
                x = x0 + (x1 - x0) * (k + 0.5) / lanes
                a, b = (z0, z1) if k % 2 == 0 else (z1, z0)
                wps += [[x, self.cruise, a], [x, self.cruise, b]]
            self.waypoints, self.segment, self.state = wps, 0, "Takeoff"
            kind = "Split search" if p.get("split") else "Search"
            return self._resp(True, f"{kind} started: {lanes} lanes over {x1-x0:.0f} x {z1-z0:.0f} m.")

        if action in ("takeoff", "land", "return_home"):
            if action != "takeoff":
                self.state = "Idle"
            if action == "return_home":
                self.pos = [0.0, 0.3, -8.0]
            if action == "land":
                self.pos[1] = 0.3
            return self._resp(True, {"takeoff": "Taking off.", "land": "Landing at the current position.",
                                     "return_home": "Returning home, then landing."}[action])

        return self._resp(False, f"Unknown action '{action}'.")
