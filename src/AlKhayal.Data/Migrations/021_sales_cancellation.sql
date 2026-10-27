-- Draft removal and posted voids are append-only tombstones. The posted invoice remains immutable.
CREATE TABLE SalesDraftDeletions (
 SalesInvoiceId INTEGER PRIMARY KEY REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id),
 CreatedUtc TEXT NOT NULL
);
CREATE TRIGGER TR_SalesDraftDeletions_NoUpdate BEFORE UPDATE ON SalesDraftDeletions BEGIN SELECT RAISE(ABORT,'sales_draft_deletion_immutable'); END;
CREATE TRIGGER TR_SalesDraftDeletions_NoDelete BEFORE DELETE ON SalesDraftDeletions BEGIN SELECT RAISE(ABORT,'sales_draft_deletion_immutable'); END;
CREATE TABLE SalesCancellations (
 SalesInvoiceId INTEGER PRIMARY KEY REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 InventoryDocumentId INTEGER NOT NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL,
 Reason TEXT NOT NULL CHECK(length(trim(Reason)) BETWEEN 2 AND 240),
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id),
 CreatedUtc TEXT NOT NULL
);
CREATE TRIGGER TR_SalesCancellations_NoUpdate BEFORE UPDATE ON SalesCancellations BEGIN SELECT RAISE(ABORT,'sales_cancellation_immutable'); END;
CREATE TRIGGER TR_SalesCancellations_NoDelete BEFORE DELETE ON SalesCancellations BEGIN SELECT RAISE(ABORT,'sales_cancellation_immutable'); END;
