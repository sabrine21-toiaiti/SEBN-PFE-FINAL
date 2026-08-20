# SEBN — Système de Détection Automatique des Non-Conformités
### Architecture Backend .NET + Microservice IA Python

Application complète, **testée de bout en bout**, prête à l'emploi.

---

## 🏗️ Architecture

```
┌─────────────────────┐        HTTP/JSON        ┌──────────────────────┐
│   ia-service/        │ ◄──────────────────────► │   SebnWeb/             │
│   Python (FastAPI)   │      GET /health          │   .NET 8 (Razor Pages) │
│   Détection IA (POO) │      GET /detect          │   Backend + Frontend   │
└──────────────────────┘                          │   Base JSON locale     │
                                                    └──────────────────────┘
```

- **ia-service** (Python, FastAPI) : microservice dédié à l'intelligence
  artificielle (POO). Génère une scène de poste de câblage simulée et une
  détection réaliste (ou le modèle YOLOv8 réel une fois entraîné).
- **SebnWeb** (.NET 8, Razor Pages) : backend complet (authentification,
  logique métier, persistance) + frontend (pages, design, graphiques).
  Consomme l'API du microservice IA via `HttpClient`.
- **Base de données** : fichier JSON local (`SebnWeb/Data/sebn_detection.json`),
  généré automatiquement au premier lancement avec **420 non-conformités
  simulées réalistes** (30 jours, 4 lignes, 12 opérateurs) — en cohérence
  avec la stratégie hybride de données du rapport (Chapitre 2).

## ✅ Testé et validé (avant livraison)

- Compilation `.NET` : ✅ 0 erreur, 0 warning
- Microservice Python : ✅ démarre, répond sur `/health` et `/detect`
- Connexion (4 rôles) : ✅ authentification + session testées
- Dashboard : ✅ affiche des statistiques réelles calculées (ex. 88,6 % conformité)
- Flux vidéo : ✅ appel réel au microservice IA, enregistrement en base confirmé
- Historique : ✅ filtres fonctionnels
- Contrôle d'accès par rôle : ✅ page Administration bloquée hors rôle PIT Admin (redirection testée)

## 🔧 Prérequis

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.10+

## 🚀 Installation (première fois)

### 1. Microservice IA (Python)
```bash
cd ia-service
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

### 2. Backend/Frontend (.NET)
```bash
cd SebnWeb
dotnet restore
```
> Le projet n'utilise **aucun package NuGet externe** (uniquement les
> bibliothèques intégrées à .NET) — il compile même sans connexion internet.

## ▶️ Lancer l'application

**Option simple** : double-cliquez sur `demarrer.bat` à la racine du projet
(lance les deux services automatiquement dans deux fenêtres).

**Option manuelle** (deux terminaux) :
```bash
# Terminal 1
cd ia-service
venv\Scripts\activate
uvicorn main:app --port 8000

# Terminal 2
cd SebnWeb
dotnet run
```

Puis ouvrez : **http://localhost:5000**

## 🔑 Comptes de démonstration

| Rôle | Login | Mot de passe |
|---|---|---|
| Superviseur Production | `superviseur` | `sebn2026` |
| Auditeur Qualité | `qualite` | `sebn2026` |
| Administrateur PIT | `admin` | `sebn2026` |
| Direction Générale | `direction` | `sebn2026` |

## 📁 Structure du projet

```
SEBN-DOTNET/
├── demarrer.bat                    # Lance les 2 services d'un coup
├── ia-service/                     # Microservice IA (Python)
│   ├── main.py                      # API FastAPI (/health, /detect)
│   ├── detection.py                 # Simulation + hook YOLO réel
│   ├── models/best.pt               # (à ajouter après entraînement)
│   └── requirements.txt
└── SebnWeb/                        # Backend + Frontend (.NET 8)
    ├── Program.cs                   # Point d'entrée, configuration DI
    ├── Models/
    │   ├── Utilisateur.cs            # Classe abstraite + 4 rôles (héritage)
    │   ├── Entites.cs                # Anomalie, Operateur, Poste, Camera
    │   └── Enums.cs
    ├── Data/
    │   ├── AppDataStore.cs           # Backend — couche d'accès aux données
    │   ├── SeedGenerator.cs          # Génère les 420 anomalies simulées
    │   └── sebn_detection.json       # Base de données (générée au 1er lancement)
    ├── Services/
    │   └── DetectionApiClient.cs     # Client HTTP vers le microservice Python
    ├── Pages/
    │   ├── Index.cshtml(.cs)          # Connexion
    │   ├── Dashboard.cshtml(.cs)      # KPI + graphiques (Chart.js)
    │   ├── FluxVideo.cshtml(.cs)      # Surveillance temps réel
    │   ├── Historique.cshtml(.cs)     # Filtres + clôture d'anomalies
    │   ├── Administration.cshtml(.cs) # Réservé au rôle PIT Admin
    │   └── _Layout.cshtml             # Sidebar, design SEBN
    └── wwwroot/
        ├── css/site.css               # Identité visuelle SEBN (couleurs du logo)
        └── images/logo_sebn.png
```

## 🌍 Déploiement permanent gratuit (Render.com — accessible depuis n'importe où)

Aucune carte bancaire requise. L'application sera accessible 24/7 via une URL
publique, depuis n'importe quel PC ou téléphone connecté à internet.

### Étape 1 — Pousser le code sur GitHub
```bash
git init
git add .
git commit -m "SEBN - application complete"
git branch -M main
git remote add origin https://github.com/TON-USERNAME/sebn-app.git
git push -u origin main
```

### Étape 2 — Déployer avec le Blueprint (fichier render.yaml inclus)
1. Créer un compte sur [render.com](https://render.com) (Sign up avec GitHub, sans carte)
2. Dashboard → **New → Blueprint**
3. Sélectionner votre dépôt GitHub `sebn-app`
4. Render détecte automatiquement `render.yaml` et propose de créer les **2 services**
   (`sebn-ia-service` en Python, `sebn-web-app` en Docker/.NET)
5. Cliquer **Apply** → le déploiement démarre (~5-10 minutes la première fois)

### Étape 3 — Vérifier la liaison entre les 2 services
Dashboard Render → `sebn-web-app` → **Environment** → vérifier que
`DetectionApi__BaseUrl` pointe bien vers l'URL réelle de `sebn-ia-service`
générée par Render (visible dans son propre dashboard, en général
`https://sebn-ia-service.onrender.com`). Corriger si besoin puis **Manual Deploy**.

### Étape 4 — Tester
Ouvrir l'URL de `sebn-web-app` (ex. `https://sebn-web-app.onrender.com`)
depuis n'importe quel appareil.

⚠️ **Le plan gratuit "s'endort"** après ~15 minutes d'inactivité — le premier
chargement après une pause prend 30-60 secondes (le temps que le service se
réveille). C'est normal, pas un bug. Pour la soutenance, ouvrez l'application
5 minutes avant de commencer pour la "réveiller".

## 🔄 Passer en mode réel (une fois le modèle YOLO entraîné)

1. Entraîner le modèle (Roboflow + Google Colab)
2. Placer `best.pt` dans `ia-service/models/best.pt`
3. Dans `ia-service/requirements.txt`, décommenter `opencv-python` et `ultralytics`
4. `pip install -r requirements.txt`
5. Relancer — le microservice bascule automatiquement en mode réel (caméra + YOLO)

## 📝 Notes pour le rapport (Chapitre 5 — Réalisation)

- Le **diagramme de classes** (Chapitre 4) est implémenté directement en C#
  dans `Models/Utilisateur.cs` (héritage : `Utilisateur` abstrait → 4 classes filles).
- L'architecture **microservices** (Python IA ↔ backend .NET via API REST)
  illustre concrètement l'architecture 3 couches du Chapitre 3.
- Pensez à faire des captures d'écran de chaque page connectée pour le rapport.
