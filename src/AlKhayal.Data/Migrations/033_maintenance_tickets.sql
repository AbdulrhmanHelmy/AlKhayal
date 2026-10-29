CREATE TABLE MaintenanceTickets (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL REFERENCES Customers(Id) ON DELETE RESTRICT,
    TicketNumber TEXT NOT NULL UNIQUE,
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    Phone TEXT NOT NULL,
    Device TEXT NOT NULL,
    Model TEXT NOT NULL,
    Serial TEXT NOT NULL DEFAULT '',
    Accessories TEXT NOT NULL DEFAULT '',
    Issue TEXT NOT NULL,
    Technician TEXT NOT NULL DEFAULT '',
    EstimatedCostScaled INTEGER NOT NULL CHECK(EstimatedCostScaled >= 0),
    AdvancePaidScaled INTEGER NOT NULL CHECK(AdvancePaidScaled >= 0),
    Notes TEXT NOT NULL DEFAULT '',
    ReceivedOn TEXT NOT NULL,
    ExpectedOn TEXT NOT NULL,
    Status TEXT NOT NULL CHECK(Status IN ('تم الاستلام','قيد الفحص','قيد الإصلاح','بانتظار قطعة','جاهز للتسليم','تم التسليم')),
    FinalCostScaled INTEGER NULL CHECK(FinalCostScaled IS NULL OR FinalCostScaled >= 0),
    SettlementType TEXT NULL CHECK(SettlementType IS NULL OR SettlementType IN ('cash','installment')),
    DeliveredOn TEXT NULL,
    DeliveryOperationKey TEXT NULL UNIQUE,
    DeliveryRequestHash TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    CHECK((Status='تم التسليم' AND FinalCostScaled IS NOT NULL AND SettlementType IS NOT NULL AND DeliveredOn IS NOT NULL AND DeliveryOperationKey IS NOT NULL) OR (Status<>'تم التسليم' AND FinalCostScaled IS NULL AND SettlementType IS NULL AND DeliveredOn IS NULL AND DeliveryOperationKey IS NULL))
);
CREATE INDEX IX_MaintenanceTickets_CustomerStatus ON MaintenanceTickets(CustomerId,Status,Id);
CREATE INDEX IX_MaintenanceTickets_Expected ON MaintenanceTickets(Status,ExpectedOn);

CREATE TABLE MaintenanceTicketStatusHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TicketId INTEGER NOT NULL REFERENCES MaintenanceTickets(Id) ON DELETE RESTRICT,
    Status TEXT NOT NULL CHECK(Status IN ('تم الاستلام','قيد الفحص','قيد الإصلاح','بانتظار قطعة','جاهز للتسليم','تم التسليم')),
    OperationKey TEXT NOT NULL UNIQUE,
    ChangedUtc TEXT NOT NULL
);
CREATE INDEX IX_MaintenanceTicketStatusHistory_Ticket ON MaintenanceTicketStatusHistory(TicketId,Id);

CREATE TABLE MaintenanceTicketReceipts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TicketId INTEGER NOT NULL REFERENCES MaintenanceTickets(Id) ON DELETE RESTRICT,
    ReceiptType TEXT NOT NULL CHECK(ReceiptType IN ('advance','cash_delivery')),
    AmountScaled INTEGER NOT NULL CHECK(AmountScaled > 0),
    OperationKey TEXT NOT NULL UNIQUE,
    ReceivedOn TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_MaintenanceTicketReceipts_Ticket ON MaintenanceTicketReceipts(TicketId,Id);
CREATE TRIGGER TR_MaintenanceTicketStatusHistory_NoUpdate BEFORE UPDATE ON MaintenanceTicketStatusHistory BEGIN SELECT RAISE(ABORT,'maintenance_history_immutable'); END;
CREATE TRIGGER TR_MaintenanceTicketStatusHistory_NoDelete BEFORE DELETE ON MaintenanceTicketStatusHistory BEGIN SELECT RAISE(ABORT,'maintenance_history_immutable'); END;
CREATE TRIGGER TR_MaintenanceTicketReceipts_NoUpdate BEFORE UPDATE ON MaintenanceTicketReceipts BEGIN SELECT RAISE(ABORT,'maintenance_receipt_immutable'); END;
CREATE TRIGGER TR_MaintenanceTicketReceipts_NoDelete BEFORE DELETE ON MaintenanceTicketReceipts BEGIN SELECT RAISE(ABORT,'maintenance_receipt_immutable'); END;
