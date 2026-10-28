CREATE TABLE ReturnTreasuryAccounts (
 Purpose TEXT PRIMARY KEY CHECK(Purpose IN ('sales','purchase')),
 AccountId INTEGER NOT NULL UNIQUE REFERENCES TreasuryAccounts(Id),
 CreatedUtc TEXT NOT NULL
);
CREATE TABLE ReturnTreasuryLinks (
 Id INTEGER PRIMARY KEY,
 SalesReturnId INTEGER REFERENCES SalesReturns(Id),
 PurchaseReturnId INTEGER REFERENCES PurchaseReturns(Id),
 Role TEXT NOT NULL CHECK(Role IN ('funding_out','funding_in','refund','purchase_refund')),
 MovementId INTEGER NOT NULL UNIQUE REFERENCES TreasuryMovements(Id),
 CHECK((SalesReturnId IS NOT NULL AND PurchaseReturnId IS NULL) OR (SalesReturnId IS NULL AND PurchaseReturnId IS NOT NULL)),
 UNIQUE(SalesReturnId,Role), UNIQUE(PurchaseReturnId,Role)
);
CREATE TRIGGER ReturnTreasuryAccounts_NoUpdate BEFORE UPDATE ON ReturnTreasuryAccounts BEGIN SELECT RAISE(ABORT,'Return account identity is immutable'); END;
CREATE TRIGGER ReturnTreasuryAccounts_NoDelete BEFORE DELETE ON ReturnTreasuryAccounts BEGIN SELECT RAISE(ABORT,'Return account identity is immutable'); END;
CREATE TRIGGER ReturnTreasuryLinks_NoUpdate BEFORE UPDATE ON ReturnTreasuryLinks BEGIN SELECT RAISE(ABORT,'Return treasury source is immutable'); END;
CREATE TRIGGER ReturnTreasuryLinks_NoDelete BEFORE DELETE ON ReturnTreasuryLinks BEGIN SELECT RAISE(ABORT,'Return treasury source is immutable'); END;
