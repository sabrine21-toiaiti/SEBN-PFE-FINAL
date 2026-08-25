"""
Microservice IA (POO Python) - expose la détection via une API REST
consommée par le backend .NET.

Lancer : uvicorn main:app --port 8000 --reload
"""
from fastapi import FastAPI, UploadFile, File
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from typing import Optional
from detection import get_module_detection, modele_reel_disponible
import base64
import io
import logging
import time
from PIL import Image, UnidentifiedImageError

logger = logging.getLogger(__name__)
MAX_IMAGE_BYTES = 10 * 1024 * 1024

app = FastAPI(title="SEBN - Microservice de Détection IA")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Le modèle et la caméra sont initialisés à la première détection, jamais au
# chargement du module, afin que /health reste disponible immédiatement.
detecteur = get_module_detection()


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
    started = time.perf_counter()
    logger.info("[IA] request received: /detect-image filename=%s content_type=%s", file.filename, file.content_type)
    try:
        contenu = await file.read()
        logger.info("[IA] image received: %d bytes", len(contenu))
        if not contenu or len(contenu) > MAX_IMAGE_BYTES:
            return JSONResponse(
                status_code=413,
                content={"success": False, "error": "L'image est vide ou dépasse 10 Mo.", "code": "IMAGE_TOO_LARGE"},
            )

        img = Image.open(io.BytesIO(contenu))
        logger.info("[IA] image decoded: format=%s size=%s", img.format, img.size)

        img_annotee, resultat = detecteur.analyser_image_fournie(img)
        logger.info("[IA] inference completed")

        buffer = io.BytesIO()
        img_annotee.save(buffer, format="JPEG", quality=85)
        image_b64 = base64.b64encode(buffer.getvalue()).decode()
        logger.info("[IA] result encoded: %d bytes in %.3f s", len(buffer.getvalue()), time.perf_counter() - started)

        anomalie = None
        if resultat:
            anomalie = AnomalieDetectee(
                type_anomalie=resultat["type_anomalie"],
                classe=resultat["classe"],
                confiance=resultat["confiance"],
            )

        return ResultatDetection(image_base64=image_b64, anomalie=anomalie)
    except UnidentifiedImageError:
        logger.warning("IA image detection rejected an invalid image")
        return JSONResponse(
            status_code=400,
            content={"success": False, "error": "Le fichier envoyé n'est pas une image valide.", "code": "INVALID_IMAGE"},
        )
    except Exception:
        logger.exception("IA image detection failed during image processing or inference")
        return JSONResponse(
            status_code=500,
            content={"success": False, "error": "Erreur interne pendant l'analyse IA.", "code": "INFERENCE_ERROR"},
        )
