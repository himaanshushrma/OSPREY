"""
OSPREY v3.0 - YOLOv8 vision server.

Unity's VisionClient POSTs JPEG frames from the leader drone's camera to
    http://127.0.0.1:8766/detect
and gets back the detected boxes (normalised 0-1, image y downwards):
    {"boxes":[{"label":"person","conf":0.91,"x1":..,"y1":..,"x2":..,"y2":..}], "ms": 6.1, "model": "osprey_yolov8n"}

Run:   python vision/vision_server.py                 (your trained model)
       python vision/vision_server.py --model yolov8n.pt   (plain COCO model, for a quick test)
       python vision/vision_server.py --show          (also show the frames with boxes in a window)
"""
import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Lock

import cv2
import numpy as np

HERE = Path(__file__).resolve().parent
DEFAULT_MODEL = HERE / "models" / "osprey_yolov8n.pt"


def build_handler(model, device, conf, show, name):
    lock = Lock()   # one GPU inference at a time
    stats = {"frames": 0}

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *a):   # keep the console quiet
            pass

        def _send(self, code, obj):
            body = json.dumps(obj).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path == "/health":
                return self._send(200, {"ok": True, "model": name, "frames": stats["frames"]})
            self._send(404, {"error": "use POST /detect or GET /health"})

        def do_POST(self):
            if self.path != "/detect":
                return self._send(404, {"error": "use POST /detect"})
            n = int(self.headers.get("Content-Length", 0))
            data = np.frombuffer(self.rfile.read(n), np.uint8)
            img = cv2.imdecode(data, cv2.IMREAD_COLOR)
            if img is None:
                return self._send(400, {"error": "not a JPEG/PNG image"})
            h, w = img.shape[:2]

            t0 = time.perf_counter()
            with lock:
                r = model.predict(img, device=device, conf=conf, verbose=False)[0]
            ms = (time.perf_counter() - t0) * 1000

            boxes = []
            for (x1, y1, x2, y2), c, k in zip(r.boxes.xyxy.tolist(), r.boxes.conf.tolist(), r.boxes.cls.tolist()):
                boxes.append({"label": r.names[int(k)], "conf": round(c, 3),
                              "x1": x1 / w, "y1": y1 / h, "x2": x2 / w, "y2": y2 / h})
            stats["frames"] += 1
            if stats["frames"] % 50 == 0:
                print(f"{stats['frames']} frames, last {ms:.1f} ms, {len(boxes)} boxes")

            if show:
                cv2.imshow("OSPREY YOLOv8", r.plot())
                cv2.waitKey(1)
            self._send(200, {"boxes": boxes, "ms": round(ms, 1), "model": name})

    return Handler


def main():
    ap = argparse.ArgumentParser(description="OSPREY YOLOv8 vision server")
    ap.add_argument("--model", default=str(DEFAULT_MODEL))
    ap.add_argument("--port", type=int, default=8766)
    ap.add_argument("--conf", type=float, default=0.35)
    ap.add_argument("--device", default=None, help="0 = first GPU, cpu = CPU (default: auto)")
    ap.add_argument("--show", action="store_true", help="show annotated frames in an OpenCV window")
    args = ap.parse_args()

    import torch
    from ultralytics import YOLO

    model_path = Path(args.model)
    if not model_path.exists() and model_path.name != args.model:
        raise SystemExit(f"{model_path} not found. Train first: python vision/train_yolo.py "
                         f"(or test with --model yolov8n.pt)")
    device = args.device if args.device is not None else (0 if torch.cuda.is_available() else "cpu")
    model = YOLO(str(model_path) if model_path.exists() else args.model)
    model.predict(np.zeros((384, 640, 3), np.uint8), device=device, verbose=False)   # warm up the GPU

    gpu = torch.cuda.get_device_name(0) if torch.cuda.is_available() else "CPU"
    print(f"OSPREY vision server: {model_path.name} on {gpu}, classes {list(model.names.values())}")
    print(f"Listening on http://127.0.0.1:{args.port}/detect - press Play in Unity.")
    ThreadingHTTPServer(("127.0.0.1", args.port), build_handler(model, device, args.conf, args.show, model_path.stem)).serve_forever()


if __name__ == "__main__":
    main()
