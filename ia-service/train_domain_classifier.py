from pathlib import Path

from PIL import Image
import torch
from torch import nn
from torch.utils.data import DataLoader, Dataset, WeightedRandomSampler
from torchvision import transforms
from torchvision.models import mobilenet_v3_small

try:
    from torchvision.models import MobileNet_V3_Small_Weights
except ImportError:  # pragma: no cover
    MobileNet_V3_Small_Weights = None

ROOT = Path(__file__).resolve().parent
DATA_DIR = ROOT / "domain-data"
INDUSTRIAL_DATA_DIR = ROOT / "data"
MODEL_DIR = ROOT / "models"
MODEL_PATH = MODEL_DIR / "domain_classifier.pt"
SPLITS = ["train", "valid", "test"]
CLASSES = ["hors_domaine", "industrial"]
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
IMG_SIZE = 224


def count_images(directory: Path) -> int:
    if not directory.exists():
        return 0
    return sum(
        1
        for path in directory.iterdir()
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )


def ensure_dataset_structure() -> None:
    print("[dataset] Vérification des chemins du dataset...", flush=True)
    for split in SPLITS:
        industrial_dir = INDUSTRIAL_DATA_DIR / split / "images"
        hors_dir = DATA_DIR / split / "hors_domaine"

        if not industrial_dir.exists():
            raise FileNotFoundError(f"Dossier industriel introuvable pour le split {split}: {industrial_dir}")
        if not hors_dir.exists():
            raise FileNotFoundError(f"Dossier hors_domaine introuvable pour le split {split}: {hors_dir}")

        industrial_count = count_images(industrial_dir)
        hors_count = count_images(hors_dir)

        if industrial_count == 0:
            raise FileNotFoundError(f"Aucune image industrielle trouvée dans {industrial_dir}")
        if hors_count == 0:
            raise FileNotFoundError(f"Aucune image hors_domaine trouvée dans {hors_dir}")

        print(f"[dataset] split={split:5s} industrial={industrial_count:4d} hors_domaine={hors_count:4d}", flush=True)

    for split in SPLITS:
        required_dirs = [DATA_DIR / split / "hors_domaine", INDUSTRIAL_DATA_DIR / split / "images"]
        for directory in required_dirs:
            if not directory.exists():
                raise FileNotFoundError(f"Dossier requis manquant: {directory}")

    print("[dataset] Vérification des classes terminée.", flush=True)


class DomainDataset(Dataset):
    def __init__(self, split: str, transform=None):
        self.transform = transform
        self.classes = CLASSES
        self.class_to_idx = {label: idx for idx, label in enumerate(self.classes)}
        self.samples = []

        industrial_dir = INDUSTRIAL_DATA_DIR / split / "images"
        hors_dir = DATA_DIR / split / "hors_domaine"

        if not industrial_dir.exists() or not hors_dir.exists():
            raise FileNotFoundError(f"Les chemins de dataset requis sont absents pour le split {split}.")

        for label, directory in [("industrial", industrial_dir), ("hors_domaine", hors_dir)]:
            if not directory.exists():
                raise FileNotFoundError(f"Le split {split} n'a pas de dossier {label} dans {directory}")

            for image_path in sorted(directory.iterdir()):
                if not image_path.is_file():
                    continue
                if image_path.suffix.lower() not in IMAGE_EXTENSIONS:
                    continue
                self.samples.append((image_path, self.class_to_idx[label]))

        if not self.samples:
            raise ValueError(f"Aucune image valide trouvée pour le split {split}.")

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        image_path, label = self.samples[idx]
        image = Image.open(image_path).convert("RGB")
        if self.transform is not None:
            image = self.transform(image)
        return image, label


def build_class_counts(dataset: Dataset):
    counts = {label: 0 for label in CLASSES}
    for _, label_index in dataset.samples:
        label_name = dataset.classes[label_index]
        counts[label_name] += 1
    return counts


def compute_class_weights(dataset: Dataset):
    counts = build_class_counts(dataset)
    total = sum(counts.values())
    if total == 0:
        raise ValueError("Le dataset est vide, impossible de calculer les poids de classe.")
    weights = []
    for label in CLASSES:
        count = counts[label]
        weights.append(max(1.0, total / (len(CLASSES) * count)) if count > 0 else 1.0)
    weights_tensor = torch.tensor(weights, dtype=torch.float32)
    print(f"[dataset] class_counts={counts}", flush=True)
    print(f"[dataset] class_weights={weights}", flush=True)
    return weights_tensor


def build_weighted_sampler(dataset: Dataset):
    counts = build_class_counts(dataset)
    sample_weights = []
    for _, label_index in dataset.samples:
        label_name = dataset.classes[label_index]
        class_count = counts[label_name]
        weight = max(1.0, sum(counts.values()) / (len(CLASSES) * class_count)) if class_count > 0 else 1.0
        sample_weights.append(weight)
    sampler = WeightedRandomSampler(
        weights=sample_weights,
        num_samples=len(sample_weights),
        replacement=True,
    )
    return sampler


def validate_split_labels(split_name: str, dataset: Dataset) -> None:
    counts = build_class_counts(dataset)
    if counts.keys() != {label for label in CLASSES}:
        raise RuntimeError(f"Classes absentes dans le split {split_name}: {counts}")
    print(f"[dataset] {split_name}: {counts}", flush=True)


def build_train_transform():
    return transforms.Compose(
        [
            transforms.Resize((IMG_SIZE, IMG_SIZE)),
            transforms.RandomHorizontalFlip(p=0.5),
            transforms.RandomRotation(12),
            transforms.ColorJitter(brightness=0.15, contrast=0.15, saturation=0.15, hue=0.05),
            transforms.ToTensor(),
            transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
        ]
    )


def build_eval_transform():
    return transforms.Compose(
        [
            transforms.Resize((IMG_SIZE, IMG_SIZE)),
            transforms.ToTensor(),
            transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
        ]
    )


def compute_confusion_matrix(predictions, targets):
    matrix = {true_label: {pred_label: 0 for pred_label in CLASSES} for true_label in CLASSES}
    for target, pred in zip(targets.tolist(), predictions.tolist()):
        true_label = CLASSES[target]
        pred_label = CLASSES[pred]
        matrix[true_label][pred_label] += 1
    return matrix


def compute_class_metrics(confusion_matrix):
    metrics = {}
    for cls in CLASSES:
        tp = confusion_matrix[cls].get(cls, 0)
        fp = sum(confusion_matrix[other].get(cls, 0) for other in CLASSES if other != cls)
        fn = sum(confusion_matrix[cls].get(other, 0) for other in CLASSES if other != cls)
        recall = tp / (tp + fn) if (tp + fn) > 0 else 0.0
        precision = tp / (tp + fp) if (tp + fp) > 0 else 0.0
        metrics[cls] = {"recall": recall, "precision": precision}
    return metrics


def evaluate_model(model, loader, device, criterion=None):
    model.eval()
    total_loss = 0.0
    correct = 0
    total = 0
    all_predictions = []
    all_targets = []

    with torch.no_grad():
        for inputs, targets in loader:
            inputs = inputs.to(device)
            targets = targets.to(device)
            logits = model(inputs)
            if criterion is not None:
                total_loss += criterion(logits, targets).item() * inputs.size(0)
            predictions = logits.argmax(dim=1)
            correct += (predictions == targets).sum().item()
            total += targets.size(0)
            all_predictions.extend(predictions.cpu().tolist())
            all_targets.extend(targets.cpu().tolist())

    accuracy = correct / total if total > 0 else 0.0
    avg_loss = total_loss / total if total > 0 else 0.0
    confusion = compute_confusion_matrix(torch.tensor(all_predictions), torch.tensor(all_targets))
    metrics = compute_class_metrics(confusion)
    pred_counts = {label: sum(confusion[true].get(label, 0) for true in CLASSES) for label in CLASSES}
    majority_ratio = max(pred_counts.values()) / total if total > 0 else 0.0

    return {
        "loss": avg_loss,
        "accuracy": accuracy,
        "confusion": confusion,
        "metrics": metrics,
        "pred_counts": pred_counts,
        "majority_ratio": majority_ratio,
    }


def check_single_class_failure(test_metrics):
    if test_metrics["total"] if False else False:
        pass
    if test_metrics["majority_ratio"] > 0.92:
        raise RuntimeError(
            f"Le modèle prédit presque toujours une seule classe sur le test ({test_metrics['majority_ratio']:.2%}). "
            f"Training refusé comme non fiable : {test_metrics['pred_counts']}"
        )


def run_six_image_test(model, device, eval_transform):
    test_paths = [
        DATA_DIR / "valid" / "industrial" / "normal10_synth01_png.rf.048f3de70bfbbb27cc790948ac9fb4e8.jpg",
        DATA_DIR / "valid" / "industrial" / "normal13_synth03_png.rf.d06e794394425437831a617e14b26d55.jpg",
        DATA_DIR / "valid" / "industrial" / "normal15_synth03_png.rf.94ce0cfc893f76d8665e6a14605ed541.jpg",
        DATA_DIR / "valid" / "hors_domaine" / "hors_domaine_valid_000.jpg",
        DATA_DIR / "valid" / "hors_domaine" / "hors_domaine_valid_001.png",
        DATA_DIR / "valid" / "hors_domaine" / "hors_domaine_valid_002.jpg",
    ]
    print("[test6] filename | predicted_class | confidence | expected_class | PASS/FAIL", flush=True)
    passes = 0
    for path in test_paths:
        if not path.exists():
            print(f"[test6] {path.name} | MISSING | 0.0000 | unknown | FAIL", flush=True)
            continue
        expected = "industrial" if "industrial" in str(path) else "hors_domaine"
        img = Image.open(path).convert("RGB")
        tensor = eval_transform(img).unsqueeze(0).to(device)
        with torch.no_grad():
            logits = model(tensor)
            probs = torch.softmax(logits, dim=1)[0]
            pred_idx = torch.argmax(probs).item()
            pred_label = CLASSES[pred_idx]
            confidence = float(probs[pred_idx].item())
        status = "PASS" if pred_label == expected else "FAIL"
        if status == "PASS":
            passes += 1
        print(f"[test6] {path.name} | {pred_label} | {confidence:.4f} | {expected} | {status}", flush=True)
    return passes


def train() -> None:
    ensure_dataset_structure()

    train_transform = build_train_transform()
    eval_transform = build_eval_transform()
    train_ds = DomainDataset("train", train_transform)
    valid_ds = DomainDataset("valid", eval_transform)
    test_ds = DomainDataset("test", eval_transform)

    if train_ds.classes != CLASSES:
        raise RuntimeError(f"Les classes d'entraînement sont incorrectes : {train_ds.classes}")
    if valid_ds.classes != CLASSES:
        raise RuntimeError(f"Les classes de validation sont incorrectes : {valid_ds.classes}")
    if test_ds.classes != CLASSES:
        raise RuntimeError(f"Les classes de test sont incorrectes : {test_ds.classes}")

    validate_split_labels("train", train_ds)
    validate_split_labels("valid", valid_ds)
    validate_split_labels("test", test_ds)

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    print(f"[train] device={device}", flush=True)

    if MobileNet_V3_Small_Weights is not None:
        weight_url = MobileNet_V3_Small_Weights.DEFAULT.url
        weight_filename = weight_url.rsplit("/", 1)[-1] if weight_url else None
        weight_cache = Path(torch.hub.get_dir()) / "checkpoints" / str(weight_filename) if weight_filename else None
        if weight_cache is not None and weight_cache.exists():
            print("[model] local ImageNet weights available -> using them.", flush=True)
            model = mobilenet_v3_small(weights=MobileNet_V3_Small_Weights.DEFAULT)
        else:
            print("[model] no local ImageNet weights found -> training from scratch with weights=None.", flush=True)
            model = mobilenet_v3_small(weights=None)
    else:
        print("[model] MobileNet_V3_Small_Weights unavailable -> training from scratch with weights=None.", flush=True)
        model = mobilenet_v3_small(weights=None)

    if hasattr(model, "classifier") and len(model.classifier) > 0:
        model.classifier[3] = nn.Linear(model.classifier[3].in_features, len(CLASSES))
    elif hasattr(model, "fc"):
        model.fc = nn.Linear(model.fc.in_features, len(CLASSES))
    else:
        raise RuntimeError("Architecture de modèle non prise en charge pour cette classification binaire.")

    model.to(device)

    class_weights = compute_class_weights(train_ds).to(device)
    criterion = nn.CrossEntropyLoss(weight=class_weights)
    optimizer = torch.optim.AdamW(model.parameters(), lr=1e-3, weight_decay=1e-4)
    scheduler = torch.optim.lr_scheduler.ReduceLROnPlateau(optimizer, mode="min", factor=0.5, patience=2)

    train_sampler = build_weighted_sampler(train_ds)
    train_loader = DataLoader(train_ds, batch_size=16, sampler=train_sampler, num_workers=0)
    valid_loader = DataLoader(valid_ds, batch_size=16, shuffle=False, num_workers=0)
    test_loader = DataLoader(test_ds, batch_size=16, shuffle=False, num_workers=0)

    best_state = None
    best_score = -1.0
    best_epoch = 0
    patience = 5
    stale_epochs = 0
    epochs = 20

    for epoch in range(epochs):
        model.train()
        running_loss = 0.0
        for inputs, targets in train_loader:
            inputs = inputs.to(device)
            targets = targets.to(device)
            optimizer.zero_grad()
            logits = model(inputs)
            loss = criterion(logits, targets)
            loss.backward()
            optimizer.step()
            running_loss += loss.item() * inputs.size(0)

        train_loss = running_loss / len(train_ds)
        valid_metrics = evaluate_model(model, valid_loader, device, criterion)
        val_loss = valid_metrics["loss"]
        val_accuracy = valid_metrics["accuracy"]
        industrial_recall = valid_metrics["metrics"]["industrial"]["recall"]
        hors_recall = valid_metrics["metrics"]["hors_domaine"]["recall"]
        score = val_accuracy + 0.5 * (industrial_recall + hors_recall)

        if score > best_score:
            best_score = score
            best_state = {k: v.detach().cpu().clone() for k, v in model.state_dict().items()}
            best_epoch = epoch + 1
            stale_epochs = 0
        else:
            stale_epochs += 1

        print(
            f"[train] epoch={epoch + 1}/{epochs} train_loss={train_loss:.4f} val_loss={val_loss:.4f} "
            f"val_accuracy={val_accuracy:.4f} industrial_recall={industrial_recall:.4f} hors_domaine_recall={hors_recall:.4f}",
            flush=True,
        )

        scheduler.step(val_loss)
        if stale_epochs >= patience:
            print(f"[train] Early stopping at epoch {epoch + 1} (best_epoch={best_epoch})", flush=True)
            break

    if best_state is None:
        raise RuntimeError("Aucun modèle valide n'a été sauvegardé pendant l'entraînement.")

    model.load_state_dict(best_state)
    model.eval()

    test_metrics = evaluate_model(model, test_loader, device, criterion)
    total = len(test_ds)
    test_accuracy = test_metrics["accuracy"]
    industrial_recall = test_metrics["metrics"]["industrial"]["recall"]
    hors_recall = test_metrics["metrics"]["hors_domaine"]["recall"]
    industrial_precision = test_metrics["metrics"]["industrial"]["precision"]
    hors_precision = test_metrics["metrics"]["hors_domaine"]["precision"]

    print("[test] Confusion Matrix:", flush=True)
    for true_label in CLASSES:
        print(f"[test] {true_label} -> {test_metrics['confusion'][true_label]}", flush=True)
    print(
        f"[test] accuracy={test_accuracy:.4f} industrial_recall={industrial_recall:.4f} "
        f"hors_domaine_recall={hors_recall:.4f} industrial_precision={industrial_precision:.4f} "
        f"hors_domaine_precision={hors_precision:.4f}",
        flush=True,
    )

    if total > 0 and test_metrics["majority_ratio"] > 0.92:
        raise RuntimeError(
            f"Le modèle a prédit presque toujours une seule classe sur TEST ({test_metrics['majority_ratio']:.2%}); "
            f"training refusé comme non fiable. {test_metrics['pred_counts']}"
        )

    six_passes = run_six_image_test(model, device, eval_transform)
    six_ok = six_passes == 6

    print(f"TRAINING_STATUS={'SUCCESS' if six_ok and test_accuracy >= 0.70 else 'FAIL'}", flush=True)
    print(f"TEST_ACCURACY={test_accuracy:.4f}", flush=True)
    print(f"INDUSTRIAL_RECALL={industrial_recall:.4f}", flush=True)
    print(f"HORS_DOMAINE_RECALL={hors_recall:.4f}", flush=True)
    print(f"SIX_IMAGE_TEST={'PASS' if six_ok else 'FAIL'}", flush=True)
    print(f"MODEL_PATH={MODEL_PATH}", flush=True)

    if not six_ok:
        raise RuntimeError("Le test sur les six images échoue : le modèle n'est pas fiable pour le garde-fou de domaine.")

    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    torch.save(
        {
            "model_state_dict": model.state_dict(),
            "classes": CLASSES,
            "image_size": IMG_SIZE,
        },
        MODEL_PATH,
    )

    return model


if __name__ == "__main__":
    train()
