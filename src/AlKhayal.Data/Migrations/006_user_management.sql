ALTER TABLE Users ADD COLUMN LastLoginUtc TEXT;

UPDATE Users
SET Role = CASE WHEN lower(Role) = 'administrator' THEN 'administrator' ELSE 'cashier' END;

CREATE TABLE IF NOT EXISTS UserScreenPermissions (
    UserId INTEGER NOT NULL,
    ScreenId TEXT NOT NULL,
    GrantedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL,
    PRIMARY KEY (UserId, ScreenId),
    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_UserScreenPermissions_ScreenId ON UserScreenPermissions(ScreenId);

CREATE TABLE IF NOT EXISTS UserAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ActingUserId INTEGER NOT NULL,
    TargetUserId INTEGER NOT NULL,
    Action TEXT NOT NULL,
    ChangeDetails TEXT,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY(ActingUserId) REFERENCES Users(Id),
    FOREIGN KEY(TargetUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_UserAuditLog_Target_Created ON UserAuditLog(TargetUserId, CreatedUtc);
