CREATE TABLE SupplierLedgerEntries (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SupplierId INTEGER NOT NULL REFERENCES Suppliers(Id) ON DELETE RESTRICT,
 EntryType TEXT NOT NULL CHECK(EntryType IN ('opening','legacy_invoice','legacy_payment','purchase','payment','purchase_return_credit','purchase_return_refund')),
 AmountScaled INTEGER NOT NULL,
 OccurredOn TEXT NOT NULL,
 Reference TEXT NOT NULL,
 PurchaseInvoiceId INTEGER NULL,
 LegacyInvoiceId INTEGER NULL REFERENCES SupplierInvoices(Id) ON DELETE RESTRICT,
 LegacyPaymentId INTEGER NULL REFERENCES SupplierPayments(Id) ON DELETE RESTRICT,
 PaymentMethod TEXT NULL,
 Notes TEXT NOT NULL DEFAULT '',
 OperationKey TEXT NULL UNIQUE,
 RequestHash TEXT NULL,
 CreatedUtc TEXT NOT NULL,
 FOREIGN KEY (PurchaseInvoiceId) REFERENCES PurchaseInvoices(Id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX UX_SupplierLedger_LegacyInvoice ON SupplierLedgerEntries(LegacyInvoiceId) WHERE LegacyInvoiceId IS NOT NULL;
CREATE UNIQUE INDEX UX_SupplierLedger_LegacyPayment ON SupplierLedgerEntries(LegacyPaymentId) WHERE LegacyPaymentId IS NOT NULL;

CREATE TABLE PurchaseInvoices (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SupplierId INTEGER NOT NULL REFERENCES Suppliers(Id) ON DELETE RESTRICT,
 WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 InvoiceNumber TEXT NOT NULL,
 InvoiceDate TEXT NOT NULL,
 PaymentMode TEXT NOT NULL CHECK(PaymentMode IN ('cash','credit')),
 Status TEXT NOT NULL CHECK(Status IN ('draft','posted')),
 Notes TEXT NOT NULL DEFAULT '',
 TotalScaled INTEGER NOT NULL CHECK(TotalScaled>=0),
 InventoryDocumentId INTEGER NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 Version INTEGER NOT NULL DEFAULT 1,
 CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id),
 UpdatedByUserId INTEGER NOT NULL REFERENCES Users(Id),
 CreatedUtc TEXT NOT NULL,
 UpdatedUtc TEXT NOT NULL,
 PostedUtc TEXT NULL,
 PostOperationKey TEXT NULL UNIQUE,
 PostRequestHash TEXT NULL,
 UNIQUE(SupplierId,InvoiceNumber)
);
CREATE INDEX IX_PurchaseInvoices_SupplierDate ON PurchaseInvoices(SupplierId,InvoiceDate,Id);
CREATE INDEX IX_PurchaseInvoices_StatusDate ON PurchaseInvoices(Status,InvoiceDate,Id);

CREATE TABLE PurchaseInvoiceLines (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseInvoiceId INTEGER NOT NULL REFERENCES PurchaseInvoices(Id) ON DELETE RESTRICT,
 LineNumber INTEGER NOT NULL,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 ProductUnitId INTEGER NOT NULL REFERENCES ProductUnits(Id) ON DELETE RESTRICT,
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 PurchaseUnitPriceScaled INTEGER NOT NULL CHECK(PurchaseUnitPriceScaled>=0),
 SaleUnitPriceScaled INTEGER NOT NULL CHECK(SaleUnitPriceScaled>=0),
 LineTotalScaled INTEGER NOT NULL CHECK(LineTotalScaled>=0),
 LotNumber TEXT NULL,
 ExpirationDate TEXT NULL,
 UNIQUE(PurchaseInvoiceId,LineNumber)
);
CREATE INDEX IX_PurchaseInvoiceLines_Variant ON PurchaseInvoiceLines(VariantId,PurchaseInvoiceId);

CREATE TABLE PurchaseInvoiceLineUnits (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseInvoiceLineId INTEGER NOT NULL REFERENCES PurchaseInvoiceLines(Id) ON DELETE RESTRICT,
 UnitNumber INTEGER NOT NULL,
 Serial TEXT NOT NULL,
 UNIQUE(PurchaseInvoiceLineId,UnitNumber),
 UNIQUE(Serial)
);
CREATE TABLE PurchaseInvoiceLineImeis (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseInvoiceLineUnitId INTEGER NOT NULL REFERENCES PurchaseInvoiceLineUnits(Id) ON DELETE RESTRICT,
 Imei TEXT NOT NULL UNIQUE
);

CREATE TABLE PurchaseInvoiceAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseInvoiceId INTEGER NOT NULL REFERENCES PurchaseInvoices(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id),
 EventType TEXT NOT NULL CHECK(EventType IN ('created','draft_updated','posted')),
 EventUtc TEXT NOT NULL,
 Details TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IX_PurchaseInvoiceAudit_Invoice ON PurchaseInvoiceAudit(PurchaseInvoiceId,Id);

CREATE TABLE PurchaseReturns (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseInvoiceId INTEGER NOT NULL REFERENCES PurchaseInvoices(Id) ON DELETE RESTRICT,
 SupplierId INTEGER NOT NULL REFERENCES Suppliers(Id) ON DELETE RESTRICT,
 WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 ReturnMode TEXT NOT NULL CHECK(ReturnMode IN ('cash_refund','supplier_credit')),
 Status TEXT NOT NULL CHECK(Status IN ('draft','posted')),
 TotalScaled INTEGER NOT NULL CHECK(TotalScaled>0),
 InventoryDocumentId INTEGER NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 OperationKey TEXT NULL UNIQUE,
 RequestHash TEXT NULL,
 CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id),
 CreatedUtc TEXT NOT NULL,
 PostedUtc TEXT NULL
);
CREATE TABLE PurchaseReturnLines (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 PurchaseReturnId INTEGER NOT NULL REFERENCES PurchaseReturns(Id) ON DELETE RESTRICT,
 PurchaseInvoiceLineId INTEGER NOT NULL REFERENCES PurchaseInvoiceLines(Id) ON DELETE RESTRICT,
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 AccountAmountScaled INTEGER NOT NULL CHECK(AccountAmountScaled>=0),
 InventoryValueScaled INTEGER NOT NULL CHECK(InventoryValueScaled>=0),
 VarianceScaled INTEGER NOT NULL,
 UNIQUE(PurchaseReturnId,PurchaseInvoiceLineId)
);

CREATE TRIGGER TR_SupplierLedgerEntries_NoUpdate BEFORE UPDATE ON SupplierLedgerEntries BEGIN SELECT RAISE(ABORT,'supplier_ledger_immutable'); END;
CREATE TRIGGER TR_SupplierLedgerEntries_NoDelete BEFORE DELETE ON SupplierLedgerEntries BEGIN SELECT RAISE(ABORT,'supplier_ledger_immutable'); END;
CREATE TRIGGER TR_PurchaseInvoiceAudit_NoUpdate BEFORE UPDATE ON PurchaseInvoiceAudit BEGIN SELECT RAISE(ABORT,'purchase_audit_immutable'); END;
CREATE TRIGGER TR_PurchaseInvoiceAudit_NoDelete BEFORE DELETE ON PurchaseInvoiceAudit BEGIN SELECT RAISE(ABORT,'purchase_audit_immutable'); END;
CREATE TRIGGER TR_PurchaseInvoices_PostedImmutable BEFORE UPDATE ON PurchaseInvoices WHEN OLD.Status='posted' BEGIN SELECT RAISE(ABORT,'purchase_invoice_immutable'); END;
CREATE TRIGGER TR_PurchaseInvoiceLines_PostedInsert BEFORE INSERT ON PurchaseInvoiceLines WHEN (SELECT Status FROM PurchaseInvoices WHERE Id=NEW.PurchaseInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'purchase_invoice_immutable'); END;
CREATE TRIGGER TR_PurchaseInvoiceLines_PostedUpdate BEFORE UPDATE ON PurchaseInvoiceLines WHEN (SELECT Status FROM PurchaseInvoices WHERE Id=OLD.PurchaseInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'purchase_invoice_immutable'); END;
CREATE TRIGGER TR_PurchaseInvoiceLines_PostedDelete BEFORE DELETE ON PurchaseInvoiceLines WHEN (SELECT Status FROM PurchaseInvoices WHERE Id=OLD.PurchaseInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'purchase_invoice_immutable'); END;

-- Import each historical document once and derive the opening balance as the
-- reconciled pre-document balance. Existing rows and their IDs remain unchanged.
INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,Notes,CreatedUtc)
SELECT s.Id,'opening',CAST(ROUND((s.Payable
       -COALESCE((SELECT SUM(i.Total) FROM SupplierInvoices i WHERE i.SupplierId=s.Id),0)
       +COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.SupplierId=s.Id),0))*10000,0) AS INTEGER),
       MIN(substr(s.CreatedAt,1,10),COALESCE((SELECT MIN(i.InvoiceDate) FROM SupplierInvoices i WHERE i.SupplierId=s.Id),substr(s.CreatedAt,1,10)),COALESCE((SELECT MIN(p.PaymentDate) FROM SupplierPayments p WHERE p.SupplierId=s.Id),substr(s.CreatedAt,1,10))),
       'OPEN-'||s.Id,'رصيد افتتاحي مرحّل بعد مطابقة الفواتير والمدفوعات التاريخية',
       COALESCE(s.CreatedAt,strftime('%Y-%m-%dT%H:%M:%fZ','now'))
FROM Suppliers s
WHERE ABS(s.Payable
       -COALESCE((SELECT SUM(i.Total) FROM SupplierInvoices i WHERE i.SupplierId=s.Id),0)
       +COALESCE((SELECT SUM(p.Amount) FROM SupplierPayments p WHERE p.SupplierId=s.Id),0))>0.0000001;
INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,LegacyInvoiceId,Notes,CreatedUtc)
SELECT SupplierId,'legacy_invoice',CAST(ROUND(Total*10000,0) AS INTEGER),InvoiceDate,InvoiceNumber,Id,
       'فاتورة تاريخية محفوظة قبل دفتر الموردين الجديد',CreatedAt FROM SupplierInvoices;
INSERT INTO SupplierLedgerEntries(SupplierId,EntryType,AmountScaled,OccurredOn,Reference,LegacyPaymentId,Notes,CreatedUtc)
SELECT SupplierId,'legacy_payment',-CAST(ROUND(Amount*10000,0) AS INTEGER),PaymentDate,Reference,Id,
       'دفعة تاريخية محفوظة قبل دفتر الموردين الجديد',CreatedAt FROM SupplierPayments;
