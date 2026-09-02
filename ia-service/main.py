"""
Microservice IA (POO Python) - expose la détection via une API REST
consommée par le backend .NET.

Lancer : uvicorn main:app --port 8000 --reload
"""
import asyncio
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

logger = logging.getLogger("uvicorn.error")
MAX_IMAGE_BYTES = 10 * 1024 * 1024
INFERENCE_TIMEOUT_SECONDS = 90
inference_lock = asyncio.Semaphore(1)

app = FastAPI(title="SEBN - Microservice de Détection IA")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

detecteur = get_module_detection()


class AnomalieDetectee(BaseModel):
    type_anomalie: str
    classe: str
    confiance: float


class ResultatDetection(BaseModel):
    image_base64: str
    anomalie: Optional[AnomalieDetectee] = None
    status: Optional[str] = None
    domain_valid: Optional[bool] = None
    message: Optional[str] = None


@app.on_event("startup")
async def precharger_modele():
    """Ne pas exécuter le warm-up YOLO au démarrage : celui-ci est bloquant et
    coûteux en CPU. Le modèle est initialisé à la demande lors de la vraie
    première détection."""
    logger.info("[IA] startup complete; YOLO loaded lazily on first real inference")
    return None


@app.get("/health")
def health():
    return {"status": "ok", "mode": "reel" if modele_reel_disponible() else "simulation"}


@app.get("/detect", response_model=ResultatDetection)
def detect():
    img, resultat = detecteur.capturer_et_analyser()

    if img is None:
        return JSONResponse(
            status_code=503,
            content={
                "success": False,
                "error": "La caméra industrielle est indisponible ou n'a fourni aucune image.",
                "code": "CAMERA_UNAVAILABLE",
            },
        )

    buffer = io.BytesIO()
    img.save(buffer, format="PNG")
    image_b64 = base64.b64encode(buffer.getvalue()).decode()

    anomalie = None
    status = None
    domain_valid = None
    message = None
    if resultat:
        if resultat.get("status") == "hors_domaine":
            status = "hors_domaine"
            domain_valid = False
            message = resultat.get("message")
        elif resultat.get("status") == "conforme":
            status = "conforme"
            domain_valid = True
            message = resultat.get("message")
        else:
            anomalie = AnomalieDetectee(
                type_anomalie=resultat["type_anomalie"],
                classe=resultat["classe"],
                confiance=resultat["confiance"],
            )
            status = "anomalie"
            domain_valid = True

    return ResultatDetection(
        image_base64=image_b64,
        anomalie=anomalie,
        status=status,
        domain_valid=domain_valid,
        message=message,
    )


@app.post("/detect-image", response_model=ResultatDetection)
async def detect_image(file: UploadFile = File(...)):
    """Analyse une photo envoyée par le navigateur (caméra PC ou téléphone)."""
    started = time.perf_counter()
    logger.info("[IA] Request received: /detect-image filename=%s content_type=%s", file.filename, file.content_type)
    try:
        contenu = await file.read()
        logger.info("[IA] Image size: %.1f KB", len(contenu) / 1024)
        if not contenu or len(contenu) > MAX_IMAGE_BYTES:
            return JSONResponse(
                status_code=413,
                content={"success": False, "error": "L'image est vide ou dépasse 10 Mo.", "code": "IMAGE_TOO_LARGE"},
            )

        preprocessing_started = time.perf_counter()
        img = Image.open(io.BytesIO(contenu))
        img.load()
        logger.info("[IA] Image dimensions: %s x %s", img.width, img.height)
        logger.info("[IA] Preprocessing: %.3f s", time.perf_counter() - preprocessing_started)

        inference_started = time.perf_counter()
        async def executer_inference():
            async with inference_lock:
                return await asyncio.to_thread(detecteur.analyser_image_fournie, img)

        try:
            img_annotee, resultat = await asyncio.wait_for(
                executer_inference(), timeout=INFERENCE_TIMEOUT_SECONDS
            )
        except asyncio.TimeoutError:
            logger.error("[IA] inference timeout after %s seconds", INFERENCE_TIMEOUT_SECONDS)
            return JSONResponse(
                status_code=504,
                content={
                    "success": False,
                    "error": "L'analyse IA a dépassé le délai autorisé.",
                    "code": "INFERENCE_TIMEOUT",
                },
            )
        logger.info("[IA] YOLO inference: %.3f s", time.perf_counter() - inference_started)
        logger.info("[IA] inference completed")

        postprocessing_started = time.perf_counter()
        buffer = io.BytesIO()
        img_annotee.save(buffer, format="JPEG", quality=85)
        logger.info("[IA] Postprocessing: %.3f s", time.perf_counter() - postprocessing_started)
        encoding_started = time.perf_counter()
        image_b64 = base64.b64encode(buffer.getvalue()).decode()
        logger.info("[IA] Base64 encoding: %.3f s", time.perf_counter() - encoding_started)
        logger.info("[IA] Total: %.3f s", time.perf_counter() - started)

        anomalie = None
        status = None
        domain_valid = None
        message = None
        if resultat:
            if resultat.get("status") == "hors_domaine":
                status = "hors_domaine"
                domain_valid = False
                message = resultat.get("message")
            elif resultat.get("status") == "conforme":
                status = "conforme"
                domain_valid = True
                message = resultat.get("message")
            else:
                anomalie = AnomalieDetectee(
                    type_anomalie=resultat["type_anomalie"],
                    classe=resultat["classe"],
                    confiance=resultat["confiance"],
                )
                status = "anomalie"
                domain_valid = True

        return ResultatDetection(
            image_base64=image_b64,
            anomalie=anomalie,
            status=status,
            domain_valid=domain_valid,
            message=message,
        )
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
