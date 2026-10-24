ALTER TABLE Suppliers ADD COLUMN SupplierCredit NUMERIC NOT NULL DEFAULT 0 CHECK(SupplierCredit>=0);
ALTER TABLE PurchaseInvoiceLines ADD COLUMN InventoryDocumentLineId INTEGER NULL REFERENCES InventoryDocumentLines(Id) ON DELETE RESTRICT;
CREATE UNIQUE INDEX UX_PurchaseInvoiceLines_InventoryDocumentLine ON PurchaseInvoiceLines(InventoryDocumentLineId) WHERE InventoryDocumentLineId IS NOT NULL;
ALTER TABLE SupplierPayments ADD COLUMN OperationKey TEXT NULL;
ALTER TABLE SupplierPayments ADD COLUMN RequestHash TEXT NULL;
CREATE UNIQUE INDEX UX_SupplierPayments_OperationKey ON SupplierPayments(OperationKey) WHERE OperationKey IS NOT NULL;
ALTER TABLE SupplierLedgerEntries ADD COLUMN SupplierInvoiceId INTEGER NULL REFERENCES SupplierInvoices(Id) ON DELETE RESTRICT;
DROP TRIGGER TR_SupplierLedgerEntries_NoUpdate;
UPDATE SupplierLedgerEntries SET SupplierInvoiceId=(SELECT InvoiceId FROM SupplierPayments p WHERE p.Id=SupplierLedgerEntries.LegacyPaymentId)
WHERE EntryType='legacy_payment' AND LegacyPaymentId IS NOT NULL;
CREATE TRIGGER TR_SupplierLedgerEntries_NoUpdate BEFORE UPDATE ON SupplierLedgerEntries BEGIN SELECT RAISE(ABORT,'supplier_ledger_immutable'); END;

-- New ledger operations become the authoritative balance. These columns remain
-- compatibility projections for existing screens and allow a supplier credit balance.
UPDATE Suppliers SET SupplierCredit=0 WHERE SupplierCredit IS NULL;

CREATE TABLE PurchaseReturnAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseReturnId INTEGER NOT NULL REFERENCES PurchaseReturns(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id),
 EventType TEXT NOT NULL CHECK(EventType IN ('posted')),
 EventUtc TEXT NOT NULL,
 Details TEXT NOT NULL DEFAULT ''
);
CREATE TRIGGER TR_PurchaseReturnAudit_NoUpdate BEFORE UPDATE ON PurchaseReturnAudit BEGIN SELECT RAISE(ABORT,'purchase_return_audit_immutable'); END;
CREATE TRIGGER TR_PurchaseReturnAudit_NoDelete BEFORE DELETE ON PurchaseReturnAudit BEGIN SELECT RAISE(ABORT,'purchase_return_audit_immutable'); END;
CREATE TRIGGER TR_PurchaseReturns_PostedImmutable BEFORE UPDATE ON PurchaseReturns WHEN OLD.Status='posted' BEGIN SELECT RAISE(ABORT,'purchase_return_immutable'); END;
CREATE TRIGGER TR_PurchaseReturnLines_PostedInsert BEFORE INSERT ON PurchaseReturnLines WHEN (SELECT Status FROM PurchaseReturns WHERE Id=NEW.PurchaseReturnId)<>'draft' BEGIN SELECT RAISE(ABORT,'purchase_return_immutable'); END;
CREATE TRIGGER TR_PurchaseReturnLines_PostedUpdate BEFORE UPDATE ON PurchaseReturnLines WHEN (SELECT Status FROM PurchaseReturns WHERE Id=OLD.PurchaseReturnId)<>'draft' BEGIN SELECT RAISE(ABORT,'purchase_return_immutable'); END;
CREATE TRIGGER TR_PurchaseReturnLines_PostedDelete BEFORE DELETE ON PurchaseReturnLines WHEN (SELECT Status FROM PurchaseReturns WHERE Id=OLD.PurchaseReturnId)<>'draft' BEGIN SELECT RAISE(ABORT,'purchase_return_immutable'); END;
