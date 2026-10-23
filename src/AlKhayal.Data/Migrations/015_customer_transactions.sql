-- Additive customer ledger.  Existing customer balances and payments are retained unchanged.
CREATE TABLE CustomerTransactions (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 CustomerId INTEGER NOT NULL REFERENCES Customers(Id) ON DELETE RESTRICT,
 OperationKey TEXT NULL UNIQUE,
 RequestHash TEXT NULL,
 TransactionType TEXT NOT NULL CHECK(TransactionType IN ('opening','legacy_payment','increase','collection','reduction')),
 AmountScaled INTEGER NOT NULL CHECK(AmountScaled>0),
 OccurredOn TEXT NOT NULL,
 Notes TEXT NULL,
 CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_CustomerTransactions_CustomerDate ON CustomerTransactions(CustomerId,OccurredOn,Id);
CREATE TRIGGER TR_CustomerTransactions_NoUpdate BEFORE UPDATE ON CustomerTransactions BEGIN SELECT RAISE(ABORT,'customer_transaction_immutable'); END;
CREATE TRIGGER TR_CustomerTransactions_NoDelete BEFORE DELETE ON CustomerTransactions WHEN OLD.TransactionType<>'opening' BEGIN SELECT RAISE(ABORT,'customer_transaction_immutable'); END;
-- The opening entry is derived from the recorded balance plus recorded collections; no legacy row is changed.
INSERT INTO CustomerTransactions(CustomerId,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc)
SELECT c.Id,'opening',CAST((c.Balance + COALESCE((SELECT SUM(p.Amount) FROM CustomerPayments p WHERE p.CustomerId=c.Id),0))*10000 AS INTEGER),substr(COALESCE(c.CreatedAt,datetime('now')),1,10),'رصيد مرحّل من البيانات القديمة',COALESCE(c.CreatedAt,strftime('%Y-%m-%dT%H:%M:%fZ','now'))
FROM Customers c;
INSERT INTO CustomerTransactions(CustomerId,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc)
SELECT p.CustomerId,'legacy_payment',CAST(p.Amount*10000 AS INTEGER),p.PaymentDate,COALESCE(p.Notes,''),p.CreatedAt
FROM CustomerPayments p;
