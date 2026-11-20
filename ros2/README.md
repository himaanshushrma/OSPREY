# OSPREY v3.0 – ROS 2 bridge

`osprey_bridge.py` is a ROS 2 node that connects the Unity simulator to ROS. Through it you can:

- watch the swarm, its shared map, the RRT\* route and found survivors in RViz;
- send the swarm a goal with RViz's **2D Goal Pose** tool;
- call take-off, land and return-home as ROS services.

```
Unity (Windows)  ── HTTP :8765 ──►  osprey_bridge (WSL2 / Ubuntu)  ── DDS ──►  RViz, ros2 topic, rosbag …
```

## 1. One-time setup on Windows 11

1. **Install WSL2 with Ubuntu 22.04.** In PowerShell (as Administrator), run `wsl --install -d Ubuntu-22.04`, then reboot.
2. **Let WSL reach Unity on `localhost`.** Create `C:\Users\<you>\.wslconfig` containing:
   ```
   [wsl2]
   networkingMode=mirrored
   ```
   Then run `wsl --shutdown` in PowerShell.
3. **Install ROS 2 Humble.** In the Ubuntu terminal, follow https://docs.ros.org/en/humble/Installation/Ubuntu-Install-Debs.html (choose `ros-humble-desktop`). Then run:
   ```bash
   echo "source /opt/ros/humble/setup.bash" >> ~/.bashrc && source ~/.bashrc
   ```
   On Ubuntu 24.04, use ROS 2 Jazzy instead; the bridge works with both.

## 2. Run it

1. Press **Play** in Unity.
2. In Ubuntu, start the bridge. Windows drives are under `/mnt`:
   ```bash
   cd /mnt/d/ospray
   python3 ros2/osprey_bridge.py
   ```
3. In a second Ubuntu terminal, open RViz:
   ```bash
   cd /mnt/d/ospray && rviz2 -d ros2/osprey.rviz
   ```

## 3. Try it

```bash
ros2 topic list | grep osprey
ros2 topic echo /osprey/mission_state
ros2 topic hz /osprey/swarm                      # about 10 Hz
ros2 service call /osprey/takeoff std_srvs/srv/Trigger
# split search over the forest: [x_min, y_min, x_max, y_max, altitude, split]
ros2 topic pub --once /osprey/search_area std_msgs/msg/Float32MultiArray "{data: [-190, -180, -60, 40, 25, 1]}"
ros2 bag record /osprey/swarm /osprey/map /osprey/survivors   # record a mission
```

In RViz, click **2D Goal Pose**, then click a spot on the map. The swarm plans a route around obstacles with RRT\* and flies there.

**Coordinates:** ROS uses ENU axes, so ROS x is Unity x, ROS y is Unity z, and ROS z is Unity y (height).

## 4. Topics

| Topic | Type | Rate |
|---|---|---|
| `/osprey/leader/pose` | geometry_msgs/PoseStamped | 10 Hz |
| `/osprey/swarm` | geometry_msgs/PoseArray | 10 Hz |
| `/osprey/map` | nav_msgs/OccupancyGrid (latched) | 1 Hz |
| `/osprey/path` | nav_msgs/Path (latched) | 1 Hz |
| `/osprey/survivors` | visualization_msgs/MarkerArray | 2 Hz |
| `/osprey/battery` | sensor_msgs/BatteryState | 2 Hz |
| `/osprey/mission_state` | std_msgs/String (JSON) | 2 Hz |
| `/goal_pose` (in) | geometry_msgs/PoseStamped | – |
| `/osprey/formation` (in) | std_msgs/String | – |
| `/osprey/search_area` (in) | std_msgs/Float32MultiArray | – |
| `/osprey/{takeoff,land,return_home,cancel}` | std_srvs/Trigger | – |

## About PX4

This bridge works at the ROS 2 level and drives OSPREY's own flight model. It does not run PX4 firmware. To fly a PX4 SITL drone (in Gazebo) from the same commands, you would:

1. run PX4 SITL with the uXRCE-DDS agent (`MicroXRCEAgent udp4 -p 8888`);
2. add `px4_msgs` to a ROS workspace;
3. map `/goal_pose` to `TrajectorySetpoint` + `OffboardControlMode` on `/fmu/in/…`, and `/fmu/out/vehicle_local_position` back to `/osprey/leader/pose`. PX4 uses NED axes, so x↔y swap and z flips.

The topic layout above was designed so this is a drop-in second backend. It is listed as future work, not as something OSPREY does today.
