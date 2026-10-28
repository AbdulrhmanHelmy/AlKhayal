CREATE TABLE ShiftRevenueTreasury (
    Id INTEGER PRIMARY KEY CHECK(Id=1),
    AccountId INTEGER NOT NULL UNIQUE REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
CREATE TABLE SalesTreasuryReceipts (
    SalesInvoiceId INTEGER PRIMARY KEY REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
    TreasuryAccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    TreasuryMovementId INTEGER NOT NULL UNIQUE REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_SalesTreasuryReceipts_Account ON SalesTreasuryReceipts(TreasuryAccountId,SalesInvoiceId);
CREATE TRIGGER ShiftRevenueTreasury_NoUpdate BEFORE UPDATE ON ShiftRevenueTreasury BEGIN SELECT RAISE(ABORT,'Shift revenue account identity is immutable'); END;
CREATE TRIGGER ShiftRevenueTreasury_NoDelete BEFORE DELETE ON ShiftRevenueTreasury BEGIN SELECT RAISE(ABORT,'Shift revenue account identity is immutable'); END;
CREATE TRIGGER SalesTreasuryReceipts_NoUpdate BEFORE UPDATE ON SalesTreasuryReceipts BEGIN SELECT RAISE(ABORT,'Sales treasury source is immutable'); END;
CREATE TRIGGER SalesTreasuryReceipts_NoDelete BEFORE DELETE ON SalesTreasuryReceipts BEGIN SELECT RAISE(ABORT,'Sales treasury source is immutable'); END;
