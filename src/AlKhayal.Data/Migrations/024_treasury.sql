CREATE TABLE TreasuryAccounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    NormalizedCode TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    Type TEXT NOT NULL CHECK(Type IN ('cash','bank')),
    Notes TEXT NOT NULL DEFAULT '',
    Version INTEGER NOT NULL DEFAULT 1 CHECK(Version > 0),
    IsDeleted INTEGER NOT NULL DEFAULT 0 CHECK(IsDeleted IN (0,1)),
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE TABLE TreasuryMovements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    Type TEXT NOT NULL CHECK(Type IN ('deposit','withdraw')),
    AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled > 0 AND AmountScaled % 100 = 0),
    BalanceAfterScaled INTEGER NOT NULL CHECK(typeof(BalanceAfterScaled)='integer' AND BalanceAfterScaled >= 0 AND BalanceAfterScaled % 100 = 0),
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    Notes TEXT NOT NULL,
    UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_TreasuryMovements_Account ON TreasuryMovements(AccountId,Id);
CREATE INDEX IX_TreasuryAccounts_Visible ON TreasuryAccounts(IsDeleted,Id);
CREATE TABLE TreasuryAudit (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    MovementId INTEGER REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
    Action TEXT NOT NULL,
    Detail TEXT NOT NULL,
    UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
CREATE TRIGGER TreasuryMovements_NoUpdate BEFORE UPDATE ON TreasuryMovements BEGIN SELECT RAISE(ABORT,'Treasury history is immutable'); END;
CREATE TRIGGER TreasuryMovements_NoDelete BEFORE DELETE ON TreasuryMovements BEGIN SELECT RAISE(ABORT,'Treasury history is immutable'); END;
CREATE TRIGGER TreasuryAudit_NoUpdate BEFORE UPDATE ON TreasuryAudit BEGIN SELECT RAISE(ABORT,'Treasury audit is immutable'); END;
CREATE TRIGGER TreasuryAudit_NoDelete BEFORE DELETE ON TreasuryAudit BEGIN SELECT RAISE(ABORT,'Treasury audit is immutable'); END;
