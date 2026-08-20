"""
Microservice IA (POO Python) - expose la détection via une API REST
consommée par le backend .NET.

Lancer : uvicorn main:app --port 8000 --reload
"""
from fastapi import FastAPI, UploadFile, File
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Optional
from detection import get_module_detection, modele_reel_disponible
import base64
import io
from PIL import Image

app = FastAPI(title="SEBN - Microservice de Détection IA")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

detecteur = get_module_detection()

# "Réchauffe" le modèle YOLO au démarrage du service (le premier appel PyTorch
# est toujours plus lent) - évite que le tout premier utilisateur subisse ce délai.
if modele_reel_disponible():
    try:
        _img_test = Image.new("RGB", (416, 416), (128, 128, 128))
        detecteur.analyser_image_fournie(_img_test)
        print("Modèle YOLO réchauffé avec succès.")
    except Exception as _e:
        print(f"Avertissement : échec du réchauffement du modèle ({_e})")


class AnomalieDetectee(BaseModel):
    type_anomalie: str
    classe: str
    confiance: float


class ResultatDetection(BaseModel):
    image_base64: str
    anomalie: Optional[AnomalieDetectee] = None


@app.get("/health")
def health():
    return {"status": "ok", "mode": "reel" if modele_reel_disponible() else "simulation"}


@app.get("/detect", response_model=ResultatDetection)
def detect():
    img, resultat = detecteur.capturer_et_analyser()

    buffer = io.BytesIO()
    img.save(buffer, format="PNG")
    image_b64 = base64.b64encode(buffer.getvalue()).decode()

    anomalie = None
    if resultat:
        anomalie = AnomalieDetectee(
            type_anomalie=resultat["type_anomalie"],
            classe=resultat["classe"],
            confiance=resultat["confiance"],
        )

    return ResultatDetection(image_base64=image_b64, anomalie=anomalie)


@app.post("/detect-image", response_model=ResultatDetection)
async def detect_image(file: UploadFile = File(...)):
    """Analyse une photo envoyée par le navigateur (caméra PC ou téléphone)."""
    contenu = await file.read()
    img = Image.open(io.BytesIO(contenu))

    img_annotee, resultat = detecteur.analyser_image_fournie(img)

    buffer = io.BytesIO()
    img_annotee.save(buffer, format="JPEG", quality=85)
    image_b64 = base64.b64encode(buffer.getvalue()).decode()

    anomalie = None
    if resultat:
        anomalie = AnomalieDetectee(
            type_anomalie=resultat["type_anomalie"],
            classe=resultat["classe"],
            confiance=resultat["confiance"],
        )

    return ResultatDetection(image_base64=image_b64, anomalie=anomalie)
