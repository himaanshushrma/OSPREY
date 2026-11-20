# OSPREY v3.0 – real YOLOv8 vision

This replaces the simulated survivor detector on the leader drone with a real neural network that runs on your NVIDIA GPU.

```
Unity leader camera ──JPEG 5 fps──► vision_server.py (YOLOv8, CUDA) ──boxes──► VisionClient
                                                                                ├─ draws a picture-in-picture feed with boxes (key Y)
                                                                                └─ "person" box → ray-cast to the ground → survivor
```

A COCO-trained YOLO model doesn't recognise OSPREY's survivors, which are simple low-poly shapes. So you train a model on **synthetic data from the simulator itself**, with labels made automatically from ground truth. This is the same idea as sim-to-real pipelines such as NVIDIA Isaac Replicator and Unity Perception.

## 1. Install (Windows, once)

```bat
cd D:\ospray
python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
python -m pip install -r vision\requirements.txt
python -c "import torch; print(torch.cuda.is_available(), torch.cuda.get_device_name(0))"
```

The last line should print `True` and your GPU's name.

## 2. Make the dataset (about 3 minutes)

1. Press **Play** in Unity, leave the drones landed, and press **F9**. You can also send `{"action":"capture_dataset","count":1500}` to the API.
2. A virtual camera flies around the world while survivors are moved to random spots and the sun changes. Each frame is saved with YOLO labels for 4 classes: `person`, `hut`, `building` and `tree`.
3. The output goes to `D:\ospray\Dataset\` (`images/`, `labels/`, `data.yaml`). Add `Dataset/` to `.gitignore`.

## 3. Train (about 10–20 minutes on a mid-range RTX)

```bat
python vision\train_yolo.py
```

Training saves `vision\models\osprey_yolov8n.pt` and an `.onnx` copy, and prints mAP50 for each class. Put these numbers in your résumé and slides; they are your real results.

## 4. Fly with it

```bat
python vision\vision_server.py --show
```

Then press **Play** in Unity. The camera feed at the top of the screen turns green (`● YOLOv8`). From then on, survivors the leader reports come from the network. The simulated detector takes over again automatically if the server stops.

Run `python vision\vision_server.py --model yolov8n.pt` for a quick COCO test. It runs, but it won't find the survivors; that's the point of step 3.
