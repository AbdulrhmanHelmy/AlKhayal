CREATE TABLE CustomerPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,
    Reference TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Amount NUMERIC NOT NULL CHECK (Amount > 0),
    PaymentDate TEXT NOT NULL,
    Method TEXT NOT NULL,
    Account TEXT NOT NULL,
    Notes TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE RESTRICT
);
CREATE INDEX IX_CustomerPayments_CustomerDate ON CustomerPayments(CustomerId, PaymentDate, Id);
