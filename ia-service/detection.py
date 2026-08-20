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
import os
import random
from PIL import Image, ImageDraw, ImageFont

MODEL_PATH = os.path.join(os.path.dirname(__file__), "models", "best.pt")

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
    return os.path.exists(MODEL_PATH)


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

    def analyser_image_fournie(self, img: Image.Image):
        """Analyse une photo réelle fournie (caméra du navigateur, PC ou téléphone).
        Simule une détection réaliste superposée sur la vraie photo."""
        img = img.convert("RGB").copy()
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
            resultat = None

        return img, resultat


# ---------------------- Mode réel (à activer une fois le modèle entraîné) ----------------------

class ModuleDetectionYOLO:
    """À utiliser une fois models/best.pt disponible.
    Nécessite : pip install opencv-python ultralytics
    """

    def __init__(self, chemin_modele=MODEL_PATH, source_camera=0, seuil_confiance=0.5):
        from ultralytics import YOLO
        import cv2
        self.cv2 = cv2
        self.model = YOLO(chemin_modele)
        self.camera = cv2.VideoCapture(source_camera)
        self.seuil_confiance = seuil_confiance

    def capturer_et_analyser(self):
        succes, frame = self.camera.read()
        if not succes:
            return None, None
        resultats = self.model.predict(frame, conf=self.seuil_confiance, verbose=False)[0]
        frame_annote = resultats.plot()
        img = Image.fromarray(self.cv2.cvtColor(frame_annote, self.cv2.COLOR_BGR2RGB))

        resultat = None
        for box in resultats.boxes:
            classe = self.model.names[int(box.cls[0])]
            if classe.lower() != "conforme":
                resultat = {"type_anomalie": "Qualité", "classe": classe,
                            "confiance": round(float(box.conf[0]), 2)}
                break
        return img, resultat

    def analyser_image_fournie(self, img: Image.Image):
        """Analyse réelle (YOLO) d'une photo fournie par la caméra du navigateur.
        Image redimensionnée pour accélérer l'inférence CPU (plan gratuit sans GPU)."""
        import numpy as np
        img_rgb = img.convert("RGB")
        # Redimensionner si l'image est grande (les téléphones envoient des photos HD)
        img_rgb.thumbnail((640, 640))
        frame = self.cv2.cvtColor(np.array(img_rgb), self.cv2.COLOR_RGB2BGR)
        resultats = self.model.predict(frame, conf=self.seuil_confiance, imgsz=416, verbose=False)[0]
        frame_annote = resultats.plot()
        img_annotee = Image.fromarray(self.cv2.cvtColor(frame_annote, self.cv2.COLOR_BGR2RGB))

        resultat = None
        for box in resultats.boxes:
            classe = self.model.names[int(box.cls[0])]
            if classe.lower() != "conforme":
                resultat = {"type_anomalie": "Qualité", "classe": classe,
                            "confiance": round(float(box.conf[0]), 2)}
                break
        return img_annotee, resultat

    def liberer(self):
        self.camera.release()


def get_module_detection():
    """Factory : retourne le module réel si le modèle existe, sinon la simulation."""
    if modele_reel_disponible():
        return ModuleDetectionYOLO()
    return ModuleDetectionSimulation()
