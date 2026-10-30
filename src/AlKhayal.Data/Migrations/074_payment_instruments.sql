-- Additive: legacy cash/credit mode and payment classifications remain readable.
CREATE TABLE PaymentInstruments (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 Type TEXT NOT NULL CHECK(Type IN ('wallet','card')),
 Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 120),
 Number TEXT NOT NULL CHECK(length(trim(Number)) BETWEEN 1 AND 80),
 Provider TEXT NOT NULL DEFAULT '',
 IdentityKey TEXT NOT NULL,
 Notes TEXT NOT NULL DEFAULT '',
 IsActive INTEGER NOT NULL CHECK(IsActive IN (0,1)),
 Version INTEGER NOT NULL DEFAULT 1,
 CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_PaymentInstruments_ActiveIdentity ON PaymentInstruments(Type,IdentityKey) WHERE IsActive=1;
CREATE INDEX IX_PaymentInstruments_TypeStatus ON PaymentInstruments(Type,IsActive,Id);
CREATE TABLE SalePaymentSelections (
 SalesInvoiceId INTEGER PRIMARY KEY REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 Method TEXT NOT NULL CHECK(Method IN ('cash','wallet','card','credit')),
 InstrumentId INTEGER REFERENCES PaymentInstruments(Id) ON DELETE RESTRICT,
 CHECK((Method IN ('cash','credit') AND InstrumentId IS NULL) OR (Method IN ('wallet','card') AND InstrumentId IS NOT NULL))
);
CREATE INDEX IX_SalePaymentSelections_Instrument ON SalePaymentSelections(InstrumentId,SalesInvoiceId);
CREATE TRIGGER TR_SalePaymentSelections_Insert BEFORE INSERT ON SalePaymentSelections WHEN (SELECT Status FROM SalesInvoices WHERE Id=NEW.SalesInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_payment_selection_immutable'); END;
CREATE TRIGGER TR_SalePaymentSelections_Update BEFORE UPDATE ON SalePaymentSelections WHEN (SELECT Status FROM SalesInvoices WHERE Id=OLD.SalesInvoiceId)<>'draft' OR NEW.SalesInvoiceId<>OLD.SalesInvoiceId BEGIN SELECT RAISE(ABORT,'sales_payment_selection_immutable'); END;
CREATE TRIGGER TR_SalePaymentSelections_Delete BEFORE DELETE ON SalePaymentSelections WHEN (SELECT Status FROM SalesInvoices WHERE Id=OLD.SalesInvoiceId)<>'draft' BEGIN SELECT RAISE(ABORT,'sales_payment_selection_immutable'); END;
CREATE TABLE PaymentInstrumentAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, InstrumentId INTEGER NOT NULL REFERENCES PaymentInstruments(Id),
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), Version INTEGER NOT NULL,
 Name TEXT NOT NULL, Number TEXT NOT NULL, Provider TEXT NOT NULL, Notes TEXT NOT NULL,
 IsActive INTEGER NOT NULL, CreatedUtc TEXT NOT NULL
);
CREATE TRIGGER TR_PaymentInstrumentAudit_Update BEFORE UPDATE ON PaymentInstrumentAudit BEGIN SELECT RAISE(ABORT,'payment_instrument_audit_immutable'); END;
CREATE TRIGGER TR_PaymentInstrumentAudit_Delete BEFORE DELETE ON PaymentInstrumentAudit BEGIN SELECT RAISE(ABORT,'payment_instrument_audit_immutable'); END;
