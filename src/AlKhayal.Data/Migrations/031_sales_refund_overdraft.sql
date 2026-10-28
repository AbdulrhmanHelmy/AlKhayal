-- Preserve ledger identities and historical post-balances. Migrator checks FKs before committing.
CREATE TABLE TreasuryMovements_031 (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    Type TEXT NOT NULL CHECK(Type IN ('deposit','withdraw')),
    AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled>0 AND AmountScaled%100=0),
    BalanceAfterScaled INTEGER NOT NULL CHECK(typeof(BalanceAfterScaled)='integer' AND BalanceAfterScaled%100=0),
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    Notes TEXT NOT NULL,
    UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
INSERT INTO TreasuryMovements_031(Id,AccountId,Type,AmountScaled,BalanceAfterScaled,OperationKey,RequestHash,Notes,UserId,CreatedUtc)
SELECT Id,AccountId,Type,AmountScaled,BalanceAfterScaled,OperationKey,RequestHash,Notes,UserId,CreatedUtc FROM TreasuryMovements;
DROP TABLE TreasuryMovements;
ALTER TABLE TreasuryMovements_031 RENAME TO TreasuryMovements;
CREATE INDEX IX_TreasuryMovements_Account ON TreasuryMovements(AccountId,Id);
CREATE TRIGGER TreasuryMovements_NoUpdate BEFORE UPDATE ON TreasuryMovements BEGIN SELECT RAISE(ABORT,'Treasury history is immutable'); END;
CREATE TRIGGER TreasuryMovements_NoDelete BEFORE DELETE ON TreasuryMovements BEGIN SELECT RAISE(ABORT,'Treasury history is immutable'); END;
CREATE TRIGGER TreasuryMovements_NegativeBalanceScope BEFORE INSERT ON TreasuryMovements
WHEN NEW.BalanceAfterScaled<0 AND NOT EXISTS(
    SELECT 1 FROM ReturnTreasuryAccounts r WHERE r.Purpose='sales' AND r.AccountId=NEW.AccountId
    AND (NEW.Type='deposit' OR (NEW.Type='withdraw' AND substr(NEW.OperationKey,1,19)='sale-return:refund:'))
)
BEGIN SELECT RAISE(ABORT,'Negative balance is restricted to sales refunds'); END;
