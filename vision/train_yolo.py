"""
OSPREY v3.0 - train YOLOv8 on the synthetic drone-camera dataset.

1. In Unity (Play mode, drones landed), press F9 or send {"action":"capture_dataset","count":1500}.
   Images and labels are written to <project>/Dataset with a data.yaml.
2. Run:   python vision/train_yolo.py
          (or   python vision/train_yolo.py --data D:/ospray/Dataset/data.yaml --epochs 80)
3. The best weights are copied to vision/models/osprey_yolov8n.pt (and .onnx), which
   vision_server.py loads by default.

Uses your NVIDIA GPU (device 0) when CUDA is available, otherwise the CPU (much slower).
"""
import argparse
import shutil
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_DATA = HERE.parent / "Dataset" / "data.yaml"


def main():
    ap = argparse.ArgumentParser(description="Train YOLOv8 on the OSPREY dataset")
    ap.add_argument("--data", default=str(DEFAULT_DATA), help="path to data.yaml written by DatasetCapture")
    ap.add_argument("--model", default="yolov8n.pt", help="start weights: yolov8n.pt (fast) or yolov8s.pt (more accurate)")
    ap.add_argument("--epochs", type=int, default=60)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--batch", type=int, default=16, help="lower to 8 if your GPU runs out of memory")
    ap.add_argument("--device", default=None, help="0 for the first GPU, cpu for CPU (default: auto)")
    args = ap.parse_args()

    import torch
    from ultralytics import YOLO

    data = Path(args.data)
    if not data.exists():
        raise SystemExit(f"{data} not found. Capture a dataset in Unity first (F9 in Play mode).")

    device = args.device if args.device is not None else (0 if torch.cuda.is_available() else "cpu")
    print(f"CUDA available: {torch.cuda.is_available()}"
          + (f" ({torch.cuda.get_device_name(0)})" if torch.cuda.is_available() else " - training on CPU will be slow"))

    model = YOLO(args.model)
    results = model.train(
        data=str(data),
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=device,
        project=str(HERE / "runs"),
        name="osprey",
        exist_ok=True,
        # aerial images: any rotation is plausible, flips are fine; no mosaic in the last epochs
        degrees=15, fliplr=0.5, flipud=0.2, hsv_v=0.4, close_mosaic=10,
        patience=20,
    )

    best = Path(results.save_dir) / "weights" / "best.pt"
    out = HERE / "models"
    out.mkdir(exist_ok=True)
    shutil.copy(best, out / "osprey_yolov8n.pt")
    print(f"Saved {out / 'osprey_yolov8n.pt'}")

    # Validation metrics for the README / slides
    metrics = YOLO(str(out / "osprey_yolov8n.pt")).val(data=str(data), device=device, imgsz=args.imgsz)
    print(f"mAP50 {metrics.box.map50:.3f}   mAP50-95 {metrics.box.map:.3f}")
    names = metrics.names
    for i, ap50 in enumerate(metrics.box.ap50):
        print(f"  {names[i]:<9} AP50 {ap50:.3f}")

    # ONNX export (for Unity Inference Engine / other runtimes later)
    onnx = YOLO(str(out / "osprey_yolov8n.pt")).export(format="onnx", imgsz=args.imgsz, opset=15)
    print(f"Exported {onnx}")


if __name__ == "__main__":
    main()
