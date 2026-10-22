CREATE TABLE Customers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Name TEXT NOT NULL,
    Phone TEXT NOT NULL,
    Address TEXT NOT NULL DEFAULT '',
    Balance NUMERIC NOT NULL DEFAULT 0 CHECK (Balance >= 0),
    CreditLimit NUMERIC NOT NULL DEFAULT 0 CHECK (CreditLimit >= 0),
    Subscription TEXT NOT NULL DEFAULT 'أساسي',
    Status TEXT NOT NULL DEFAULT 'نشط',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
CREATE INDEX IX_Customers_Name ON Customers(Name);
CREATE INDEX IX_Customers_Phone ON Customers(Phone);
