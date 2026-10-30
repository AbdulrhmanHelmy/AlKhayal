-- Metadata only. Original vouchers and linked treasury facts remain immutable.
-- Absence of an event means unclassified; no historical name-based inference.
CREATE TABLE ExpenseProfitClassifications (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 VoucherId INTEGER NOT NULL REFERENCES ExpenseVouchers(Id) ON DELETE RESTRICT,
 Revision INTEGER NOT NULL CHECK(Revision>0),
 Classification TEXT NOT NULL CHECK(Classification IN ('included','excluded','unclassified')),
 Reason TEXT NOT NULL CHECK(length(trim(Reason)) BETWEEN 2 AND 500),
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 CreatedUtc TEXT NOT NULL,
 OperationKey TEXT NOT NULL UNIQUE,
 UNIQUE(VoucherId,Revision)
);
CREATE TRIGGER ExpenseProfitClassifications_NoUpdate BEFORE UPDATE ON ExpenseProfitClassifications BEGIN SELECT RAISE(ABORT,'classification_audit_immutable'); END;
CREATE TRIGGER ExpenseProfitClassifications_NoDelete BEFORE DELETE ON ExpenseProfitClassifications BEGIN SELECT RAISE(ABORT,'classification_audit_immutable'); END;
CREATE INDEX IX_SalesReturns_Event ON SalesReturns(CreatedUtc,Id);
CREATE INDEX IX_SalesReturnLines_Original ON SalesReturnLines(SalesInvoiceLineId,SalesReturnId);
CREATE INDEX IX_ExpenseVouchers_Event ON ExpenseVouchers(CreatedUtc,Id);
CREATE INDEX IX_SalesInvoices_PostedEvent ON SalesInvoices(Status,PostedUtc,Id);
CREATE INDEX IX_SalesCancellations_Event ON SalesCancellations(CreatedUtc,SalesInvoiceId);
