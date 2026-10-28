CREATE TABLE SupplierPaymentAllocations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierLedgerEntryId INTEGER NOT NULL REFERENCES SupplierLedgerEntries(Id) ON DELETE RESTRICT,
    SupplierInvoiceId INTEGER NULL REFERENCES SupplierInvoices(Id) ON DELETE RESTRICT,
    PurchaseInvoiceId INTEGER NULL REFERENCES PurchaseInvoices(Id) ON DELETE RESTRICT,
    AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled>0),
    CHECK((SupplierInvoiceId IS NOT NULL AND PurchaseInvoiceId IS NULL) OR (SupplierInvoiceId IS NULL AND PurchaseInvoiceId IS NOT NULL))
);
CREATE UNIQUE INDEX UX_SupplierPaymentAllocations_Legacy ON SupplierPaymentAllocations(SupplierLedgerEntryId,SupplierInvoiceId) WHERE SupplierInvoiceId IS NOT NULL;
CREATE UNIQUE INDEX UX_SupplierPaymentAllocations_Purchase ON SupplierPaymentAllocations(SupplierLedgerEntryId,PurchaseInvoiceId) WHERE PurchaseInvoiceId IS NOT NULL;
CREATE INDEX IX_SupplierPaymentAllocations_Legacy ON SupplierPaymentAllocations(SupplierInvoiceId);
CREATE INDEX IX_SupplierPaymentAllocations_Purchase ON SupplierPaymentAllocations(PurchaseInvoiceId);
CREATE TRIGGER SupplierPaymentAllocations_NoUpdate BEFORE UPDATE ON SupplierPaymentAllocations BEGIN SELECT RAISE(ABORT,'Supplier payment allocation is immutable'); END;
CREATE TRIGGER SupplierPaymentAllocations_NoDelete BEFORE DELETE ON SupplierPaymentAllocations BEGIN SELECT RAISE(ABORT,'Supplier payment allocation is immutable'); END;
