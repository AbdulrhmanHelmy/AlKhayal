-- Sales financial events and returns are append-only. Invoice PaidScaled remains a projection.
CREATE TABLE SalesPayments (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 CustomerId INTEGER NULL REFERENCES Customers(Id) ON DELETE RESTRICT,
 SalesReturnId INTEGER NULL,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 EventType TEXT NOT NULL CHECK(EventType IN ('cash_sale','customer_collection','refund')),
 PaymentMethod TEXT NOT NULL CHECK(PaymentMethod IN ('cash','card','transfer','credit_adjustment')),
 AmountScaled INTEGER NOT NULL CHECK(AmountScaled>0),
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL,
 OccurredOn TEXT NOT NULL,
 Notes TEXT NULL,
 CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_SalesPayments_Invoice ON SalesPayments(SalesInvoiceId,OccurredOn,Id);
CREATE TABLE SalesReturns (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 ReturnNumber TEXT NOT NULL UNIQUE,
 SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 InventoryDocumentId INTEGER NOT NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL,
 TotalScaled INTEGER NOT NULL CHECK(TotalScaled>0),
 CashRefundScaled INTEGER NOT NULL CHECK(CashRefundScaled>=0),
 CreditReductionScaled INTEGER NOT NULL CHECK(CreditReductionScaled>=0),
 Reason TEXT NOT NULL CHECK(length(trim(Reason)) BETWEEN 2 AND 240),
 CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 CreatedUtc TEXT NOT NULL,
 CHECK(CashRefundScaled+CreditReductionScaled=TotalScaled)
);
CREATE TABLE SalesReturnLines (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SalesReturnId INTEGER NOT NULL REFERENCES SalesReturns(Id) ON DELETE RESTRICT,
 SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id) ON DELETE RESTRICT,
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 AmountScaled INTEGER NOT NULL CHECK(AmountScaled>0),
 UNIQUE(SalesReturnId,SalesInvoiceLineId)
);
CREATE TABLE SalesReturnCostAllocations (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SalesReturnLineId INTEGER NOT NULL REFERENCES SalesReturnLines(Id) ON DELETE RESTRICT,
 CostLayerId INTEGER NOT NULL REFERENCES InventoryCostLayers(Id) ON DELETE RESTRICT,
 QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
 ValueScaled INTEGER NOT NULL CHECK(ValueScaled>=0)
);
CREATE TABLE SalesReturnPhysicalUnits (
 SalesReturnLineId INTEGER NOT NULL REFERENCES SalesReturnLines(Id) ON DELETE RESTRICT,
 PhysicalUnitId INTEGER NOT NULL REFERENCES InventoryPhysicalUnits(Id) ON DELETE RESTRICT,
 Serial TEXT NOT NULL,
 PRIMARY KEY(SalesReturnLineId,PhysicalUnitId),
 UNIQUE(Serial)
);
CREATE TRIGGER TR_SalesReturnPhysicalUnits_NoUpdate BEFORE UPDATE ON SalesReturnPhysicalUnits BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturnPhysicalUnits_NoDelete BEFORE DELETE ON SalesReturnPhysicalUnits BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesPayments_NoUpdate BEFORE UPDATE ON SalesPayments BEGIN SELECT RAISE(ABORT,'sales_payment_immutable'); END;
CREATE TRIGGER TR_SalesPayments_NoDelete BEFORE DELETE ON SalesPayments BEGIN SELECT RAISE(ABORT,'sales_payment_immutable'); END;
CREATE TRIGGER TR_SalesReturns_NoUpdate BEFORE UPDATE ON SalesReturns BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturns_NoDelete BEFORE DELETE ON SalesReturns BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturnLines_NoUpdate BEFORE UPDATE ON SalesReturnLines BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturnLines_NoDelete BEFORE DELETE ON SalesReturnLines BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturnCostAllocations_NoUpdate BEFORE UPDATE ON SalesReturnCostAllocations BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturnCostAllocations_NoDelete BEFORE DELETE ON SalesReturnCostAllocations BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
DROP TRIGGER TR_SalesInvoices_PostedImmutable;
CREATE TRIGGER TR_SalesInvoices_PostedImmutable BEFORE UPDATE ON SalesInvoices WHEN OLD.Status='posted' AND
 (NEW.InvoiceNumber IS NOT OLD.InvoiceNumber OR NEW.InvoiceDate IS NOT OLD.InvoiceDate OR NEW.CustomerId IS NOT OLD.CustomerId OR NEW.WarehouseId IS NOT OLD.WarehouseId OR NEW.CashierUserId IS NOT OLD.CashierUserId OR NEW.Status IS NOT OLD.Status OR NEW.PaymentMode IS NOT OLD.PaymentMode OR NEW.SubtotalScaled IS NOT OLD.SubtotalScaled OR NEW.DiscountScaled IS NOT OLD.DiscountScaled OR NEW.TaxScaled IS NOT OLD.TaxScaled OR NEW.TotalScaled IS NOT OLD.TotalScaled OR NEW.TaxRate IS NOT OLD.TaxRate OR NEW.TaxIncluded IS NOT OLD.TaxIncluded OR NEW.InventoryDocumentId IS NOT OLD.InventoryDocumentId OR NEW.OperationKey IS NOT OLD.OperationKey OR NEW.RequestHash IS NOT OLD.RequestHash OR NEW.Version IS NOT OLD.Version OR NEW.CreatedUtc IS NOT OLD.CreatedUtc OR NEW.PostedUtc IS NOT OLD.PostedUtc OR NEW.PaidScaled<0 OR NEW.PaidScaled>NEW.TotalScaled)
BEGIN SELECT RAISE(ABORT,'sales_invoice_immutable'); END;
ALTER TABLE CustomerTransactions ADD COLUMN Reference TEXT NULL;
ALTER TABLE CustomerTransactions ADD COLUMN SalesInvoiceId INTEGER NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT;
ALTER TABLE CustomerTransactions ADD COLUMN SalesReturnId INTEGER NULL REFERENCES SalesReturns(Id) ON DELETE RESTRICT;
CREATE INDEX IX_CustomerTransactions_Invoice ON CustomerTransactions(SalesInvoiceId,Id);
