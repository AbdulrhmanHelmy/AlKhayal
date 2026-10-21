CREATE TABLE IF NOT EXISTS BusinessActivities (
    ActivityKey TEXT PRIMARY KEY,
    IsPrimary INTEGER NOT NULL DEFAULT 0 CHECK (IsPrimary IN (0, 1)),
    SelectedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_BusinessActivities_Primary
ON BusinessActivities(IsPrimary) WHERE IsPrimary = 1;

CREATE TABLE IF NOT EXISTS CapabilityConfiguration (
    CapabilityKey TEXT PRIMARY KEY,
    IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)),
    OverrideState TEXT NOT NULL CHECK (OverrideState IN ('recommended', 'administrator')),
    UpdatedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_CapabilityConfiguration_OverrideState
ON CapabilityConfiguration(OverrideState);
