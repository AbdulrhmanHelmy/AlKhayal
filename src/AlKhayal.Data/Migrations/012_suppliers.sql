CREATE TABLE Suppliers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Name TEXT NOT NULL,
    Contact TEXT NOT NULL DEFAULT '',
    Phone TEXT NOT NULL,
    Address TEXT NOT NULL DEFAULT '',
    Payable NUMERIC NOT NULL DEFAULT 0 CHECK (Payable >= 0),
    Status TEXT NOT NULL DEFAULT 'نشط',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
CREATE INDEX IX_Suppliers_Name ON Suppliers(Name);
CREATE INDEX IX_Suppliers_Phone ON Suppliers(Phone);

CREATE TABLE SupplierInvoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    InvoiceNumber TEXT NOT NULL COLLATE NOCASE,
    InvoiceDate TEXT NOT NULL,
    DueDate TEXT,
    Total NUMERIC NOT NULL CHECK (Total > 0),
    PaidAmount NUMERIC NOT NULL DEFAULT 0 CHECK (PaidAmount >= 0 AND PaidAmount <= Total),
    Notes TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id) ON DELETE RESTRICT,
    UNIQUE (SupplierId, InvoiceNumber)
);
CREATE INDEX IX_SupplierInvoices_SupplierDate ON SupplierInvoices(SupplierId, InvoiceDate, Id);

CREATE TABLE SupplierPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierId INTEGER NOT NULL,
    InvoiceId INTEGER,
    Reference TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Amount NUMERIC NOT NULL CHECK (Amount > 0),
    PaymentDate TEXT NOT NULL,
    Method TEXT NOT NULL,
    Account TEXT NOT NULL,
    Notes TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id) ON DELETE RESTRICT,
    FOREIGN KEY (InvoiceId) REFERENCES SupplierInvoices(Id) ON DELETE RESTRICT
);
CREATE INDEX IX_SupplierPayments_SupplierDate ON SupplierPayments(SupplierId, PaymentDate, Id);
CREATE INDEX IX_SupplierPayments_Invoice ON SupplierPayments(InvoiceId);
