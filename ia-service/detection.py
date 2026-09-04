"""
Module de détection (couche Traitement & Logique).

Deux modes :
- MODE RÉEL   : si un modèle entraîné existe dans models/best.pt, utilise
                YOLOv8 (ultralytics) + une caméra réelle (OpenCV).
- MODE SIMULATION : sinon, génère une scène synthétique représentative
                d'un poste de câblage SEBN et simule des détections
                réalistes. Permet d'utiliser l'application immédiatement,
                en cohérence avec la stratégie hybride de données du
                Chapitre 2 du rapport.
"""
from pathlib import Path
import random
import threading
import logging
from typing import Any, Dict, Optional
from PIL import Image, ImageDraw, ImageFont

logger = logging.getLogger("uvicorn.error")
MODEL_PATH = Path(__file__).resolve().parent / "models" / "best.pt"
DOMAIN_MODEL_PATH = Path(__file__).resolve().parent / "models" / "domain_classifier.pt"
DOMAIN_MODEL_CACHE: Optional[Any] = None
DOMAIN_MODEL_CACHE_LOCK = threading.Lock()

CLASSES_DEFAUTS = {
    "Qualité": ["connecteur_manquant", "fil_mal_positionne", "defaut_couleur", "sertissage_defectueux"],
    "Production": ["cable_mal_clipse", "sous_ensemble_incomplet"],
    "5S": ["outil_hors_zone", "poste_desordre"],
}

COULEURS = {
    "Qualité": (220, 53, 69),      # rouge
    "Production": (255, 153, 0),   # orange
    "5S": (13, 110, 253),          # bleu
    "conforme": (25, 135, 84),     # vert
}


def modele_reel_disponible() -> bool:
    return MODEL_PATH.is_file()


def _charger_classifieur_domaine():
    global DOMAIN_MODEL_CACHE
    if DOMAIN_MODEL_PATH.exists() is False:
        return None
    if DOMAIN_MODEL_CACHE is not None:
        return DOMAIN_MODEL_CACHE

    with DOMAIN_MODEL_CACHE_LOCK:
        if DOMAIN_MODEL_CACHE is not None:
            return DOMAIN_MODEL_CACHE

        try:
            import torch
            from torchvision import transforms
            from torchvision.models import mobilenet_v3_small
            from torch import nn

            checkpoint = torch.load(DOMAIN_MODEL_PATH, map_location="cpu")
            model = mobilenet_v3_small(weights=None)
            model.classifier[3] = nn.Linear(model.classifier[3].in_features, 2)
            model.load_state_dict(checkpoint["model_state_dict"])
            model.eval()

            transform = transforms.Compose([
                transforms.Resize((224, 224)),
                transforms.ToTensor(),
                transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
            ])
            DOMAIN_MODEL_CACHE = {"model": model, "transform": transform, "classes": checkpoint.get("classes", ["hors_domaine", "industrial"])}
            return DOMAIN_MODEL_CACHE
        except Exception:
            logger.exception("[IA] Impossible de charger le classifieur de domaine industriel")
            return None


def verifier_image_industrielle(img: Image.Image) -> Dict[str, Any]:
    """Retourne un verdict binaire sur l'appartenance de l'image au domaine industriel.
    Le garde-fou est strict : si le classifieur est absent ou invalide, l'image est
    rejetée immédiatement pour éviter toute acceptation par défaut."""
    classifieur = _charger_classifieur_domaine()
    if classifieur is None:
        return {
            "valide": False,
            "label": "domain_classifier_missing",
            "confiance": 0.0,
            "message": "Classifieur de domaine absent ou invalide : refus strict de traiter l'image.",
            "skip": False,
        }

    try:
        import torch
        tensor = classifieur["transform"](img.convert("RGB")).unsqueeze(0)
        with torch.no_grad():
            logits = classifieur["model"](tensor)
            probs = torch.softmax(logits, dim=1)[0]
            idx = int(torch.argmax(probs).item())
            conf = float(probs[idx].item())
            label = classifieur["classes"][idx]
        industrial_index = classifieur["classes"].index("industrial") if "industrial" in classifieur["classes"] else 0
        industrial_probability = float(probs[industrial_index].item())
        is_valid = label == "industrial" and industrial_probability >= 0.45
        return {
            "valide": bool(is_valid),
            "label": label,
            "confiance": round(conf, 4),
            "industrial_probability": round(industrial_probability, 4) if industrial_index < len(probs) else 0.0,
            "message": "Image conforme au domaine industriel." if is_valid else "Image hors domaine : ce n'est pas un poste industriel valide.",
        }
    except Exception:
        logger.exception("[IA] validation du domaine industriel a échoué")
        return {"valide": False, "label": "unknown", "confiance": 0.0, "message": "Validation du domaine impossible : refus de traiter l'image."}


class ModuleDetectionSimulation:
    """Génère une frame synthétique de poste de câblage + détection simulée."""

    def __init__(self, largeur=640, hauteur=400, probabilite_anomalie=0.35):
        self.largeur = largeur
        self.hauteur = hauteur
        self.probabilite_anomalie = probabilite_anomalie

    def _dessiner_poste(self, draw):
        # Fond atelier
        draw.rectangle([0, 0, self.largeur, self.hauteur], fill=(235, 236, 240))
        # Gabarit (board) de câblage
        draw.rectangle([60, 60, self.largeur - 60, self.hauteur - 60], fill=(60, 60, 65), outline=(30, 30, 33), width=3)
        # Connecteurs (petits rectangles clairs)
        random.seed()
        positions = [(110, 100), (250, 100), (390, 100), (110, 260), (250, 260), (390, 260)]
        for (x, y) in positions:
            draw.rectangle([x, y, x + 60, y + 40], fill=(220, 220, 210), outline=(90, 90, 90), width=2)
        # Câbles (lignes colorées)
        cable_colors = [(220, 50, 50), (50, 130, 220), (240, 200, 40), (60, 180, 90)]
        for i, (x, y) in enumerate(positions[:-1]):
            x2, y2 = positions[i + 1]
            draw.line([x + 30, y + 40, x2 + 30, y2], fill=cable_colors[i % len(cable_colors)], width=4)

    def capturer_et_analyser(self):
        """Retourne (image_PIL, resultat_dict|None)."""
        img = Image.new("RGB", (self.largeur, self.hauteur), (235, 236, 240))
        draw = ImageDraw.Draw(img)
        self._dessiner_poste(draw)
        return self._analyser_et_annoter(img, draw)

    def precharger_modele(self):
        return None

    def analyser_image_fournie(self, img: Image.Image):
        """Analyse une photo réelle fournie (caméra du navigateur, PC ou téléphone).
        Simule une détection réaliste superposée sur la vraie photo."""
        img = img.convert("RGB").copy()
        validation = verifier_image_industrielle(img)
        if not validation["valide"]:
            logger.warning("[IA] simulation image rejected by domain guard: %s (confidence=%.3f)", validation["label"], validation["confiance"])
            return img, {
                "status": "hors_domaine",
                "domain_valid": False,
                "message": validation["message"],
                "domain_confidence": validation["confiance"],
                "domain_label": validation["label"],
            }
        draw = ImageDraw.Draw(img)
        return self._analyser_et_annoter(img, draw, dessiner_fond=False)

    def _analyser_et_annoter(self, img, draw, dessiner_fond=True):
        largeur, hauteur = img.size

        anomalie_detectee = random.random() < self.probabilite_anomalie

        if anomalie_detectee:
            type_anomalie = random.choices(
                ["Qualité", "Production", "5S"], weights=[45, 30, 25], k=1
            )[0]
            classe = random.choice(CLASSES_DEFAUTS[type_anomalie])
            confiance = round(random.uniform(0.65, 0.97), 2)
            couleur = COULEURS[type_anomalie]

            # Boîte englobante simulée autour d'une zone aléatoire de l'image
            bx = random.randint(int(largeur * 0.15), int(largeur * 0.65))
            by = random.randint(int(hauteur * 0.15), int(hauteur * 0.55))
            bw, bh = int(largeur * 0.18), int(hauteur * 0.16)
            draw.rectangle([bx, by, bx + bw, by + bh], outline=couleur, width=4)
            label = f"{classe} ({int(confiance * 100)}%)"
            draw.rectangle([bx, by - 22, bx + len(label) * 7, by], fill=couleur)
            draw.text((bx + 3, by - 20), label, fill=(255, 255, 255))

            resultat = {
                "type_anomalie": type_anomalie,
                "classe": classe,
                "confiance": confiance,
            }
        else:
            # Cadre vert = conforme
            draw.rectangle([8, 8, largeur - 8, hauteur - 8], outline=COULEURS["conforme"], width=4)
            resultat = {
                "status": "conforme",
                "message": "Aucune non-conformité détectée sur ce poste industriel.",
            }

        return img, resultat


# ---------------------- Mode réel (à activer une fois le modèle entraîné) ----------------------

class ModuleDetectionYOLO:
    """À utiliser une fois models/best.pt disponible.
    Nécessite : pip install opencv-python ultralytics
    """

    def __init__(self, chemin_modele=MODEL_PATH, source_camera=0, seuil_confiance=0.2):
        self.chemin_modele = str(chemin_modele)
        self.source_camera = source_camera
        self.seuil_confiance = seuil_confiance
        self.cv2 = None
        self.model = None
        self._model_lock = threading.Lock()
        self.camera = None

    def _charger_modele(self):
        if self.model is None:
            with self._model_lock:
                if self.model is None:
                    from ultralytics import YOLO
                    import cv2
                    self.cv2 = cv2
                    logger.info("[IA] loading model: %s", self.chemin_modele)
                    self.model = YOLO(self.chemin_modele)
                    logger.info("[IA] model loaded")
        return self.model

    def precharger_modele(self):
        """Initialisation paresseuse du modèle YOLO : on charge le modèle seulement
        au moment de la première vraie détection, sans exécuter un warm-up bloquant
        au démarrage du service."""
        self._charger_modele()
        logger.info("[IA] YOLO model initialized lazily; startup warm-up disabled")
        return None

    def _ouvrir_camera(self):
        if self.camera is None:
            import cv2
            self.cv2 = cv2
            self.camera = cv2.VideoCapture(self.source_camera)
        return self.camera

    def capturer_et_analyser(self):
        camera = self._ouvrir_camera()
        succes, frame = camera.read()
        if not succes:
            return None, None
        model = self._charger_modele()
        resultats = model.predict(frame, conf=self.seuil_confiance, verbose=False)[0]
        frame_annote = resultats.plot()
        img = Image.fromarray(self.cv2.cvtColor(frame_annote, self.cv2.COLOR_BGR2RGB))

        resultat = None
        for box in resultats.boxes:
            classe = model.names[int(box.cls[0])]
            if classe.lower() != "conforme":
                resultat = {"type_anomalie": "Qualité", "classe": classe,
                            "confiance": round(float(box.conf[0]), 2)}
                break
        return img, resultat

    def analyser_image_fournie(self, img: Image.Image):
        """Analyse réelle (YOLO) d'une photo fournie par la caméra du navigateur.
        Image redimensionnée pour accélérer l'inférence CPU (plan gratuit sans GPU)."""
        validation = verifier_image_industrielle(img)
        if not validation["valide"]:
            logger.warning("[IA] image rejected before YOLO: %s (confidence=%.3f)", validation["label"], validation["confiance"])
            return img.copy(), {
                "status": "hors_domaine",
                "domain_valid": False,
                "message": validation["message"],
                "domain_confidence": validation["confiance"],
                "domain_label": validation["label"],
            }

        import numpy as np
        model = self._charger_modele()
        img_rgb = img.convert("RGB")
        # Redimensionner si l'image est grande (les téléphones envoient des photos HD)
        img_rgb.thumbnail((384, 384))
        frame = self.cv2.cvtColor(np.array(img_rgb), self.cv2.COLOR_RGB2BGR)
        logger.info("[IA] inference started")
        resultats = model.predict(
            frame,
            conf=self.seuil_confiance,
            imgsz=320,
            batch=1,
            device="cpu",
            verbose=False,
        )[0]
        frame_annote = resultats.plot()
        img_annotee = Image.fromarray(self.cv2.cvtColor(frame_annote, self.cv2.COLOR_BGR2RGB))

        resultat = None
        for box in resultats.boxes:
            classe = model.names[int(box.cls[0])]
            if classe.lower() != "conforme":
                resultat = {"type_anomalie": "Qualité", "classe": classe,
                            "confiance": round(float(box.conf[0]), 2)}
                break
        if resultat is None:
            return img_annotee, {"status": "conforme", "domain_valid": True, "message": "Aucune anomalie détectée sur un poste industriel valide."}
        return img_annotee, resultat

    def liberer(self):
        if self.camera is not None:
            self.camera.release()


def get_module_detection():
    """Factory : retourne le module réel si le modèle existe, sinon la simulation."""
    if modele_reel_disponible():
        return ModuleDetectionYOLO()
    return ModuleDetectionSimulation()
