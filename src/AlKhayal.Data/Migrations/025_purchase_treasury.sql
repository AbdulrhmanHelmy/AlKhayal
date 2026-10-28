ALTER TABLE PurchaseInvoices ADD COLUMN TreasuryAccountId INTEGER NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT;
ALTER TABLE PurchaseInvoices ADD COLUMN TreasuryMovementId INTEGER NULL REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT;
CREATE UNIQUE INDEX UX_PurchaseInvoices_TreasuryMovement ON PurchaseInvoices(TreasuryMovementId) WHERE TreasuryMovementId IS NOT NULL;
CREATE INDEX IX_PurchaseInvoices_TreasuryAccount ON PurchaseInvoices(TreasuryAccountId,Id) WHERE TreasuryAccountId IS NOT NULL;
