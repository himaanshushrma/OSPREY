#!/usr/bin/env python3
"""
OSPREY v3.0 - ROS 2 bridge.

Connects the Unity simulator (SwarmCommandAPI on http://localhost:8765) to ROS 2, so
standard ROS tools (RViz, rosbag, Nav2-style nodes) can watch and command the swarm.

Published (frame "map", ROS ENU: x = Unity x, y = Unity z, z = Unity y)
  /osprey/leader/pose      geometry_msgs/PoseStamped        10 Hz
  /osprey/swarm            geometry_msgs/PoseArray          10 Hz   leader + followers
  /osprey/map              nav_msgs/OccupancyGrid           1 Hz    the swarm's shared map
  /osprey/path             nav_msgs/Path                    1 Hz    current RRT* route
  /osprey/survivors        visualization_msgs/MarkerArray   2 Hz
  /osprey/battery          sensor_msgs/BatteryState         2 Hz    leader (percentage 0-1)
  /osprey/mission_state    std_msgs/String                  2 Hz    JSON summary
  TF  map -> osprey/leader

Subscribed
  /goal_pose               geometry_msgs/PoseStamped   RViz "2D Goal Pose": fly there (RRT* planned)
  /osprey/formation        std_msgs/String             e.g. "Wedge"
  /osprey/search_area      std_msgs/Float32MultiArray  [x_min, y_min, x_max, y_max, altitude, split(0/1)]
Services (std_srvs/Trigger)
  /osprey/takeoff  /osprey/land  /osprey/return_home  /osprey/cancel

Run (in WSL2 / Ubuntu with ROS 2 Humble or Jazzy):
  source /opt/ros/$ROS_DISTRO/setup.bash
  python3 ros2/osprey_bridge.py            # --unity http://localhost:8765
  rviz2 -d ros2/osprey.rviz
"""
import argparse
import json
import math
import urllib.error
import urllib.request

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, DurabilityPolicy, ReliabilityPolicy

from geometry_msgs.msg import Pose, PoseArray, PoseStamped, TransformStamped
from nav_msgs.msg import OccupancyGrid, Path
from sensor_msgs.msg import BatteryState
from std_msgs.msg import Float32MultiArray, String
from std_srvs.srv import Trigger
from tf2_ros import TransformBroadcaster
from visualization_msgs.msg import Marker, MarkerArray


def enu(v):
    """Unity {x, y(up), z} -> ROS (x east, y north, z up)."""
    return float(v["x"]), float(v["z"]), float(v["y"])


def yaw_quat(heading_deg):
    """Unity heading (deg, clockwise from +z/north) -> ROS quaternion (yaw CCW from +x/east)."""
    yaw = math.radians(90.0 - heading_deg)
    return 0.0, 0.0, math.sin(yaw / 2), math.cos(yaw / 2)


class Unity:
    def __init__(self, base):
        self.base = base.rstrip("/")

    def _req(self, path, body=None, timeout=2.0):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method="POST" if body is not None else "GET",
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:   # API answers 400 with a JSON reason
            return json.loads(e.read() or b"{}")

    def state(self):
        return self._req("/state")

    def map(self):
        return self._req("/map", timeout=4.0)

    def command(self, action, **kw):
        return self._req("/command", {"action": action, **kw}, timeout=10.0)


class OspreyBridge(Node):
    def __init__(self, unity_url):
        super().__init__("osprey_bridge")
        self.unity = Unity(unity_url)
        self.online = False

        latched = QoSProfile(depth=1, durability=DurabilityPolicy.TRANSIENT_LOCAL, reliability=ReliabilityPolicy.RELIABLE)
        self.pub_pose = self.create_publisher(PoseStamped, "/osprey/leader/pose", 10)
        self.pub_swarm = self.create_publisher(PoseArray, "/osprey/swarm", 10)
        self.pub_map = self.create_publisher(OccupancyGrid, "/osprey/map", latched)
        self.pub_path = self.create_publisher(Path, "/osprey/path", latched)
        self.pub_surv = self.create_publisher(MarkerArray, "/osprey/survivors", 10)
        self.pub_batt = self.create_publisher(BatteryState, "/osprey/battery", 10)
        self.pub_state = self.create_publisher(String, "/osprey/mission_state", 10)
        self.tf = TransformBroadcaster(self)

        self.create_subscription(PoseStamped, "/goal_pose", self.on_goal, 10)
        self.create_subscription(String, "/osprey/formation", self.on_formation, 10)
        self.create_subscription(Float32MultiArray, "/osprey/search_area", self.on_search, 10)
        for name in ("takeoff", "land", "return_home", "cancel"):
            self.create_service(Trigger, f"/osprey/{name}", self.make_trigger(name))

        self.last = None
        self.tick = 0
        self.create_timer(0.1, self.fast)     # 10 Hz
        self.create_timer(1.0, self.slow)     # 1 Hz
        self.get_logger().info(f"OSPREY bridge -> {unity_url}")

    # ---------- helpers ----------
    def stamp(self):
        return self.get_clock().now().to_msg()

    def pose(self, p, heading=None):
        m = Pose()
        m.position.x, m.position.y, m.position.z = enu(p)
        if heading is not None:
            m.orientation.x, m.orientation.y, m.orientation.z, m.orientation.w = yaw_quat(heading)
        else:
            m.orientation.w = 1.0
        return m

    def call(self, action, **kw):
        try:
            r = self.unity.command(action, **kw)
        except Exception as e:
            return False, f"Unity not reachable: {e}"
        return bool(r.get("ok")), r.get("message", "")

    # ---------- publishing ----------
    def fast(self):
        try:
            r = self.unity.state()
        except Exception as e:
            if self.online:
                self.get_logger().warn(f"Lost Unity: {e}")
            self.online = False
            return
        if not self.online:
            self.get_logger().info("Connected to the OSPREY simulator.")
        self.online = True
        s = r.get("state") or {}
        self.last = s
        if "leaderPosition" not in s:
            return
        now = self.stamp()

        ps = PoseStamped()
        ps.header.frame_id = "map"
        ps.header.stamp = now
        ps.pose = self.pose(s["leaderPosition"], s.get("headingDeg", 0.0))
        self.pub_pose.publish(ps)

        t = TransformStamped()
        t.header = ps.header
        t.child_frame_id = "osprey/leader"
        t.transform.translation.x, t.transform.translation.y, t.transform.translation.z = \
            ps.pose.position.x, ps.pose.position.y, ps.pose.position.z
        t.transform.rotation = ps.pose.orientation
        self.tf.sendTransform(t)

        pa = PoseArray()
        pa.header = ps.header
        pa.poses = [ps.pose] + [self.pose(p) for p in s.get("followerPositions") or []]
        self.pub_swarm.publish(pa)

        self.tick += 1
        if self.tick % 5:
            return
        # 2 Hz
        b = BatteryState()
        b.header = ps.header
        b.percentage = float(s.get("batteryPercent", 0.0)) / 100.0
        b.power_supply_status = BatteryState.POWER_SUPPLY_STATUS_CHARGING if s.get("charging") \
            else BatteryState.POWER_SUPPLY_STATUS_DISCHARGING
        b.present = True
        b.location = "leader"
        self.pub_batt.publish(b)

        ma = MarkerArray()
        for i, p in enumerate(s.get("survivorPositions") or []):
            m = Marker()
            m.header = ps.header
            m.ns, m.id, m.type, m.action = "survivors", i, Marker.CYLINDER, Marker.ADD
            m.pose = self.pose(p)
            m.scale.x = m.scale.y = 3.0
            m.scale.z = 2.0
            m.color.r, m.color.g, m.color.b, m.color.a = 1.0, 0.2, 0.2, 0.9
            ma.markers.append(m)
        self.pub_surv.publish(ma)

        keys = ("missionState", "flightState", "formation", "survivorsFound", "survivorsTotal", "searchActive",
                "searchSplit", "searchCoverage", "swarmBatteryMin", "commsOnline", "commsTotal", "visionSource")
        self.pub_state.publish(String(data=json.dumps({k: s.get(k) for k in keys})))

    def slow(self):
        if not self.online:
            return
        try:
            m = self.unity.map()
        except Exception:
            return
        if "data" not in m:
            return
        now = self.stamp()
        g = OccupancyGrid()
        g.header.frame_id = "map"
        g.header.stamp = now
        g.info.map_load_time = now
        g.info.resolution = float(m["resolution"])
        g.info.width, g.info.height = int(m["width"]), int(m["height"])
        # Unity row-major from (originX, originZ): x -> columns, z -> rows == ROS x/y layout
        g.info.origin.position.x = float(m["originX"])
        g.info.origin.position.y = float(m["originZ"])
        g.info.origin.orientation.w = 1.0
        g.data = [int(v) for v in m["data"]]
        self.pub_map.publish(g)

        path = Path()
        path.header = g.header
        for p in m.get("plannedRoute") or []:
            ps = PoseStamped()
            ps.header = g.header
            ps.pose = self.pose(p)
            path.poses.append(ps)
        self.pub_path.publish(path)

    # ---------- commands ----------
    def on_goal(self, msg: PoseStamped):
        x, y = msg.pose.position.x, msg.pose.position.y
        ok, text = self.call("set_waypoints", waypoints=[{"x": x, "z": y}], fromCurrentPosition=True, plan=True)
        if ok:
            ok, text2 = self.call("start_mission")
            text = f"{text} | {text2}"
        (self.get_logger().info if ok else self.get_logger().warn)(f"Goal ({x:.0f}, {y:.0f}): {text}")

    def on_formation(self, msg: String):
        ok, text = self.call("set_formation", formation=msg.data)
        (self.get_logger().info if ok else self.get_logger().warn)(text)

    def on_search(self, msg: Float32MultiArray):
        d = list(msg.data)
        if len(d) < 4:
            self.get_logger().warn("search_area needs [x_min, y_min, x_max, y_max, altitude?, split?]")
            return
        alt = d[4] if len(d) > 4 else 25.0
        split = len(d) > 5 and d[5] > 0.5
        ok, text = self.call("search_area", x0=d[0], z0=d[1], x1=d[2], z1=d[3], altitude=alt, split=split)
        (self.get_logger().info if ok else self.get_logger().warn)(text)

    def make_trigger(self, name):
        action = {"cancel": "cancel_mission"}.get(name, name)

        def handler(_req, res):
            res.success, res.message = self.call(action)
            return res
        return handler


def main():
    ap = argparse.ArgumentParser(description="OSPREY ROS 2 bridge")
    ap.add_argument("--unity", default="http://localhost:8765", help="SwarmCommandAPI address")
    args, ros_args = ap.parse_known_args()
    rclpy.init(args=ros_args)
    node = OspreyBridge(args.unity)
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.try_shutdown()


if __name__ == "__main__":
    main()
