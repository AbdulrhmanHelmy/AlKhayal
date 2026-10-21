CREATE TABLE IF NOT EXISTS ModuleConfiguration (
    ModuleKey TEXT PRIMARY KEY,
    IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)),
    OverrideState TEXT NOT NULL CHECK (OverrideState IN ('recommended', 'administrator')),
    UpdatedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_ModuleConfiguration_OverrideState
ON ModuleConfiguration(OverrideState);

CREATE TABLE IF NOT EXISTS FeatureAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FeatureKind TEXT NOT NULL CHECK (FeatureKind IN ('module', 'capability', 'recommendations')),
    FeatureKey TEXT NOT NULL,
    PreviousState INTEGER NULL CHECK (PreviousState IS NULL OR PreviousState IN (0, 1)),
    NewState INTEGER NULL CHECK (NewState IS NULL OR NewState IN (0, 1)),
    Source TEXT NOT NULL CHECK (Source IN ('recommended', 'administrator')),
    ChangedByUserId INTEGER NOT NULL,
    ChangedUtc TEXT NOT NULL,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_FeatureAuditLog_Feature
ON FeatureAuditLog(FeatureKind, FeatureKey, ChangedUtc);

