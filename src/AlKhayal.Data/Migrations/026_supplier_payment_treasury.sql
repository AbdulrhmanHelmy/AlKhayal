CREATE TABLE SupplierPaymentTreasuryLinks (
    SupplierLedgerEntryId INTEGER PRIMARY KEY REFERENCES SupplierLedgerEntries(Id) ON DELETE RESTRICT,
    TreasuryMovementId INTEGER NOT NULL UNIQUE REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
    TreasuryAccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT
);
CREATE INDEX IX_SupplierPaymentTreasuryLinks_Account ON SupplierPaymentTreasuryLinks(TreasuryAccountId,SupplierLedgerEntryId);
CREATE TRIGGER SupplierPaymentTreasuryLinks_NoUpdate BEFORE UPDATE ON SupplierPaymentTreasuryLinks BEGIN SELECT RAISE(ABORT,'Supplier payment treasury link is immutable'); END;
CREATE TRIGGER SupplierPaymentTreasuryLinks_NoDelete BEFORE DELETE ON SupplierPaymentTreasuryLinks BEGIN SELECT RAISE(ABORT,'Supplier payment treasury link is immutable'); END;
