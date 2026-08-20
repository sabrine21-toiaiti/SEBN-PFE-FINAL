CREATE TABLE IF NOT EXISTS Utilisateurs (
    IdUtilisateur INTEGER PRIMARY KEY AUTOINCREMENT,
    Login TEXT UNIQUE NOT NULL,
    MotDePasseHash TEXT NOT NULL,
    Role TEXT NOT NULL CHECK(Role IN ('OperateurProduction','SuperviseurQualite','SuperviseurPit','Administrateur')),
    NomAffichage TEXT NOT NULL,
    NbTentatives INTEGER NOT NULL DEFAULT 0,
    VerrouJusqua TEXT
);

CREATE TABLE IF NOT EXISTS Cameras (
    IdCamera TEXT PRIMARY KEY,
    StatutConnexion TEXT NOT NULL DEFAULT 'Active' CHECK(StatutConnexion IN ('Active','HorsLigne'))
);

CREATE TABLE IF NOT EXISTS Notifications (
    IdNotification INTEGER PRIMARY KEY AUTOINCREMENT,
    Message TEXT NOT NULL,
    IdPoste TEXT NOT NULL,
    DateCreation TEXT NOT NULL,
    Lue INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Postes (
    IdPoste TEXT PRIMARY KEY,
    LigneProduction TEXT NOT NULL,
    IdCamera TEXT,
    FOREIGN KEY (IdCamera) REFERENCES Cameras(IdCamera)
);

CREATE TABLE IF NOT EXISTS Operateurs (
    MatriculeOp TEXT PRIMARY KEY,
    NomOp TEXT NOT NULL,
    PrenomOp TEXT NOT NULL,
    Equipe TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Anomalies (
    IdAnomalie INTEGER PRIMARY KEY AUTOINCREMENT,
    DateHeure TEXT NOT NULL,
    TypeAnomalie TEXT NOT NULL CHECK(TypeAnomalie IN ('Qualité','Production','5S')),
    ClasseYolo TEXT NOT NULL,
    Confiance REAL NOT NULL,
    ImagePreuve TEXT,
    Statut INTEGER NOT NULL DEFAULT 0,
    IdPoste TEXT NOT NULL,
    MatriculeOp TEXT NOT NULL,
    FOREIGN KEY (IdPoste) REFERENCES Postes(IdPoste),
    FOREIGN KEY (MatriculeOp) REFERENCES Operateurs(MatriculeOp)
);
