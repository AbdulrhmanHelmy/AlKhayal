CREATE TABLE ExpenseCategories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL UNIQUE,
    UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
CREATE TABLE ExpenseVouchers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CategoryId INTEGER NOT NULL REFERENCES ExpenseCategories(Id) ON DELETE RESTRICT,
    CategoryName TEXT NOT NULL,
    TreasuryAccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    AccountName TEXT NOT NULL,
    TreasuryMovementId INTEGER NOT NULL UNIQUE REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
    AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled>0 AND AmountScaled%100=0),
    ExpenseDate TEXT NOT NULL,
    Payee TEXT NOT NULL,
    Notes TEXT NOT NULL,
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_ExpenseVouchers_Category ON ExpenseVouchers(CategoryId,Id);
CREATE INDEX IX_ExpenseVouchers_Account ON ExpenseVouchers(TreasuryAccountId,Id);
CREATE TRIGGER ExpenseVouchers_NoUpdate BEFORE UPDATE ON ExpenseVouchers BEGIN SELECT RAISE(ABORT,'Expense vouchers are immutable'); END;
CREATE TRIGGER ExpenseVouchers_NoDelete BEFORE DELETE ON ExpenseVouchers BEGIN SELECT RAISE(ABORT,'Expense vouchers are immutable'); END;
