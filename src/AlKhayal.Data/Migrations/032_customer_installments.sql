CREATE TABLE CustomerInstallmentContracts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL REFERENCES Customers(Id) ON DELETE RESTRICT,
    ProductId INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    ContractNumber TEXT NOT NULL UNIQUE,
    ProductName TEXT NOT NULL,
    ProductPriceScaled INTEGER NOT NULL CHECK(ProductPriceScaled > 0),
    DownPaymentScaled INTEGER NOT NULL CHECK(DownPaymentScaled >= 0 AND DownPaymentScaled < ProductPriceScaled),
    InterestRateBasisPoints INTEGER NOT NULL CHECK(InterestRateBasisPoints >= 0),
    TotalAmountScaled INTEGER NOT NULL CHECK(TotalAmountScaled > 0),
    InstallmentCount INTEGER NOT NULL CHECK(InstallmentCount BETWEEN 1 AND 120),
    IntervalMonths INTEGER NOT NULL CHECK(IntervalMonths BETWEEN 1 AND 12),
    FirstDueOn TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_CustomerInstallmentContracts_Customer ON CustomerInstallmentContracts(CustomerId,Id);

CREATE TABLE CustomerInstallmentSchedule (
    ContractId INTEGER NOT NULL REFERENCES CustomerInstallmentContracts(Id) ON DELETE RESTRICT,
    InstallmentNumber INTEGER NOT NULL CHECK(InstallmentNumber > 0),
    DueOn TEXT NOT NULL,
    AmountScaled INTEGER NOT NULL CHECK(AmountScaled > 0),
    PRIMARY KEY(ContractId,InstallmentNumber)
);

CREATE TABLE CustomerInstallmentPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ContractId INTEGER NOT NULL,
    InstallmentNumber INTEGER NOT NULL,
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    AmountScaled INTEGER NOT NULL CHECK(AmountScaled > 0),
    PaidOn TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY(ContractId,InstallmentNumber) REFERENCES CustomerInstallmentSchedule(ContractId,InstallmentNumber) ON DELETE RESTRICT,
    UNIQUE(ContractId,InstallmentNumber)
);
CREATE INDEX IX_CustomerInstallmentPayments_Contract ON CustomerInstallmentPayments(ContractId,InstallmentNumber);
CREATE TRIGGER TR_CustomerInstallmentContracts_NoUpdate BEFORE UPDATE ON CustomerInstallmentContracts BEGIN SELECT RAISE(ABORT,'installment_contract_immutable'); END;
CREATE TRIGGER TR_CustomerInstallmentContracts_NoDelete BEFORE DELETE ON CustomerInstallmentContracts BEGIN SELECT RAISE(ABORT,'installment_contract_immutable'); END;
CREATE TRIGGER TR_CustomerInstallmentSchedule_NoUpdate BEFORE UPDATE ON CustomerInstallmentSchedule BEGIN SELECT RAISE(ABORT,'installment_schedule_immutable'); END;
CREATE TRIGGER TR_CustomerInstallmentSchedule_NoDelete BEFORE DELETE ON CustomerInstallmentSchedule BEGIN SELECT RAISE(ABORT,'installment_schedule_immutable'); END;
CREATE TRIGGER TR_CustomerInstallmentPayments_NoUpdate BEFORE UPDATE ON CustomerInstallmentPayments BEGIN SELECT RAISE(ABORT,'installment_payment_immutable'); END;
CREATE TRIGGER TR_CustomerInstallmentPayments_NoDelete BEFORE DELETE ON CustomerInstallmentPayments BEGIN SELECT RAISE(ABORT,'installment_payment_immutable'); END;
