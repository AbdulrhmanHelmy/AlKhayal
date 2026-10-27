CREATE TABLE SalesInvoices (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 InvoiceNumber TEXT NOT NULL UNIQUE,
 InvoiceDate TEXT NOT NULL,
 CustomerId INTEGER NULL REFERENCES Customers(Id) ON DELETE RESTRICT,
 WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id) ON DELETE RESTRICT,
 CashierUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 Status TEXT NOT NULL CHECK(Status IN ('draft','posted')),
 PaymentMode TEXT NOT NULL CHECK(PaymentMode IN ('cash','credit')),
 SubtotalScaled INTEGER NOT NULL CHECK(SubtotalScaled>=0),
 DiscountScaled INTEGER NOT NULL CHECK(DiscountScaled>=0),
 TaxScaled INTEGER NOT NULL CHECK(TaxScaled>=0),
 TotalScaled INTEGER NOT NULL CHECK(TotalScaled>=0),
 PaidScaled INTEGER NOT NULL DEFAULT 0 CHECK(PaidScaled>=0),
 TaxRate REAL NOT NULL DEFAULT 0 CHECK(TaxRate BETWEEN 0 AND 100),
 TaxIncluded INTEGER NOT NULL DEFAULT 0 CHECK(TaxIncluded IN (0,1)),
 InventoryDocumentId INTEGER NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 OperationKey TEXT NULL UNIQUE,
 RequestHash TEXT NULL,
 Version INTEGER NOT NULL DEFAULT 1,
 CreatedUtc TEXT NOT NULL,
 PostedUtc TEXT NULL
);
CREATE INDEX IX_SalesInvoices_DateStatus ON SalesInvoices(InvoiceDate,Status,Id);
CREATE INDEX IX_SalesInvoices_CustomerDate ON SalesInvoices(CustomerId,InvoiceDate,Id);
CREATE TABLE SalesInvoiceLines (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 LineNumber INTEGER NOT NULL,
 VariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
 ProductUnitId INTEGER NOT NULL REFERENCES ProductUnits(Id) ON DELETE RESTRICT,
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 UnitPriceScaled INTEGER NOT NULL CHECK(UnitPriceScaled>=0),
 DiscountScaled INTEGER NOT NULL CHECK(DiscountScaled>=0),
 InvoiceDiscountScaled INTEGER NOT NULL DEFAULT 0 CHECK(InvoiceDiscountScaled>=0),
 TaxScaled INTEGER NOT NULL CHECK(TaxScaled>=0),
 LineTotalScaled INTEGER NOT NULL CHECK(LineTotalScaled>=0),
  InventoryDocumentLineId INTEGER NULL REFERENCES InventoryDocumentLines(Id) ON DELETE RESTRICT,
 BatchLotNumber TEXT NULL,
 ExpirationDate TEXT NULL,
 UNIQUE(SalesInvoiceId,LineNumber)
);
CREATE TABLE SalesInvoiceLinePhysicalUnits (
 SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id) ON DELETE RESTRICT,
 UnitNumber INTEGER NOT NULL CHECK(UnitNumber>0),
 Serial TEXT NOT NULL,
 Imeis TEXT NOT NULL DEFAULT '',
 PRIMARY KEY(SalesInvoiceLineId,UnitNumber),
 UNIQUE(SalesInvoiceLineId,Serial)
);
CREATE TRIGGER TR_SalesInvoiceLinePhysicalUnits_Insert BEFORE INSERT ON SalesInvoiceLinePhysicalUnits WHEN (SELECT i.Status FROM SalesInvoices i JOIN SalesInvoiceLines l ON l.SalesInvoiceId=i.Id WHERE l.Id=NEW.SalesInvoiceLineId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceLinePhysicalUnits_Update BEFORE UPDATE ON SalesInvoiceLinePhysicalUnits WHEN (SELECT i.Status FROM SalesInvoices i JOIN SalesInvoiceLines l ON l.SalesInvoiceId=i.Id WHERE l.Id=OLD.SalesInvoiceLineId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceLinePhysicalUnits_Delete BEFORE DELETE ON SalesInvoiceLinePhysicalUnits WHEN (SELECT i.Status FROM SalesInvoices i JOIN SalesInvoiceLines l ON l.SalesInvoiceId=i.Id WHERE l.Id=OLD.SalesInvoiceLineId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
CREATE INDEX IX_SalesInvoiceLines_Variant ON SalesInvoiceLines(VariantId,SalesInvoiceId);
CREATE TABLE SalesInvoiceAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 EventType TEXT NOT NULL CHECK(EventType IN ('created','draft_updated','posted')),
 EventUtc TEXT NOT NULL,
 Details TEXT NOT NULL DEFAULT ''
);
CREATE TRIGGER TR_SalesInvoiceAudit_NoUpdate BEFORE UPDATE ON SalesInvoiceAudit BEGIN SELECT RAISE(ABORT,'sales_audit_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceAudit_NoDelete BEFORE DELETE ON SalesInvoiceAudit BEGIN SELECT RAISE(ABORT,'sales_audit_immutable'); END;
CREATE TRIGGER TR_SalesInvoices_PostedImmutable BEFORE UPDATE ON SalesInvoices WHEN OLD.Status='posted' AND
 (NEW.InvoiceNumber IS NOT OLD.InvoiceNumber OR NEW.InvoiceDate IS NOT OLD.InvoiceDate OR NEW.CustomerId IS NOT OLD.CustomerId OR NEW.WarehouseId IS NOT OLD.WarehouseId OR NEW.CashierUserId IS NOT OLD.CashierUserId OR NEW.Status IS NOT OLD.Status OR NEW.PaymentMode IS NOT OLD.PaymentMode OR NEW.SubtotalScaled IS NOT OLD.SubtotalScaled OR NEW.DiscountScaled IS NOT OLD.DiscountScaled OR NEW.TaxScaled IS NOT OLD.TaxScaled OR NEW.TotalScaled IS NOT OLD.TotalScaled OR NEW.TaxRate IS NOT OLD.TaxRate OR NEW.TaxIncluded IS NOT OLD.TaxIncluded OR NEW.InventoryDocumentId IS NOT OLD.InventoryDocumentId OR NEW.OperationKey IS NOT OLD.OperationKey OR NEW.RequestHash IS NOT OLD.RequestHash OR NEW.Version IS NOT OLD.Version OR NEW.CreatedUtc IS NOT OLD.CreatedUtc OR NEW.PostedUtc IS NOT OLD.PostedUtc)
BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceLines_PostedInsert BEFORE INSERT ON SalesInvoiceLines WHEN (SELECT Status FROM SalesInvoices WHERE Id=NEW.SalesInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceLines_PostedUpdate BEFORE UPDATE ON SalesInvoiceLines WHEN (SELECT Status FROM SalesInvoices WHERE Id=OLD.SalesInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceLines_PostedDelete BEFORE DELETE ON SalesInvoiceLines WHEN (SELECT Status FROM SalesInvoices WHERE Id=OLD.SalesInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
