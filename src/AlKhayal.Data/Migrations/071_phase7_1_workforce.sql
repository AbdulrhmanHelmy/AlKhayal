-- Phase-specific allocation: leave 032-069 available for parallel Phase 6 migrations.
-- No existing financial history is assigned to a shift.
CREATE TABLE Employees (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL DEFAULT 1,
 Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 160),
 Phone TEXT, JobTitle TEXT, HireDate TEXT, Address TEXT, Notes TEXT,
 LinkedUserId INTEGER UNIQUE REFERENCES Users(Id) ON DELETE RESTRICT,
 IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
 AttendanceEnabled INTEGER NOT NULL DEFAULT 0 CHECK(AttendanceEnabled IN (0,1)),
 SalaryScaled INTEGER CHECK(SalaryScaled IS NULL OR (typeof(SalaryScaled)='integer' AND SalaryScaled>=0 AND SalaryScaled%100=0)),
 SalaryFrequency TEXT, Payday INTEGER CHECK(Payday IS NULL OR Payday BETWEEN 1 AND 31), CompensationNotes TEXT,
 CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IX_Employees_Status ON Employees(IsActive,Id);
CREATE TABLE EmployeeAttendance (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, EmployeeId INTEGER NOT NULL REFERENCES Employees(Id) ON DELETE RESTRICT,
 CheckInUtc TEXT NOT NULL, CheckOutUtc TEXT, Version INTEGER NOT NULL DEFAULT 1,
 CHECK(CheckOutUtc IS NULL OR CheckOutUtc>CheckInUtc)
);
CREATE UNIQUE INDEX IX_Attendance_Open ON EmployeeAttendance(EmployeeId) WHERE CheckOutUtc IS NULL;
CREATE INDEX IX_Attendance_Date ON EmployeeAttendance(CheckInUtc,EmployeeId);
CREATE INDEX IX_Attendance_Employee ON EmployeeAttendance(EmployeeId,CheckInUtc,CheckOutUtc);
CREATE TABLE CashierShifts (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, CashierId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 RegisterId TEXT NOT NULL, OpenedUtc TEXT NOT NULL, ClosedUtc TEXT,
 OpenedBy INTEGER NOT NULL REFERENCES Users(Id), ClosedBy INTEGER REFERENCES Users(Id),
 OpeningScaled INTEGER NOT NULL CHECK(typeof(OpeningScaled)='integer' AND OpeningScaled>=0 AND OpeningScaled%100=0),
 CountedScaled INTEGER CHECK(CountedScaled IS NULL OR (typeof(CountedScaled)='integer' AND CountedScaled>=0 AND CountedScaled%100=0)),
 ClosingExpectedScaled INTEGER, OpeningNotes TEXT, ClosingNotes TEXT,
 CHECK((ClosedUtc IS NULL AND CountedScaled IS NULL AND ClosingExpectedScaled IS NULL AND ClosedBy IS NULL) OR (ClosedUtc>=OpenedUtc AND CountedScaled IS NOT NULL AND ClosingExpectedScaled IS NOT NULL AND ClosedBy IS NOT NULL))
);
CREATE UNIQUE INDEX IX_Shift_OpenCashier ON CashierShifts(CashierId) WHERE ClosedUtc IS NULL;
CREATE UNIQUE INDEX IX_Shift_OpenRegister ON CashierShifts(RegisterId) WHERE ClosedUtc IS NULL;
CREATE INDEX IX_Shift_Date ON CashierShifts(OpenedUtc,Id);
CREATE INDEX IX_Shift_CashierDate ON CashierShifts(CashierId,OpenedUtc,Id);
CREATE TABLE CashierShiftEvents (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, ShiftId INTEGER NOT NULL REFERENCES CashierShifts(Id) ON DELETE RESTRICT,
 Kind TEXT NOT NULL CHECK(Kind IN ('cash_sale','credit_sale','collection','in','out')),
 AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled>=0 AND AmountScaled%100=0),
 SalesInvoiceId INTEGER REFERENCES SalesInvoices(Id), SalesPaymentId INTEGER REFERENCES SalesPayments(Id),
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), Reason TEXT, CreatedUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_ShiftEvent_Sale ON CashierShiftEvents(SalesInvoiceId) WHERE Kind IN ('cash_sale','credit_sale');
CREATE UNIQUE INDEX IX_ShiftEvent_Payment ON CashierShiftEvents(SalesPaymentId) WHERE SalesPaymentId IS NOT NULL;
CREATE INDEX IX_ShiftEvent_Shift ON CashierShiftEvents(ShiftId,Id);
CREATE INDEX IX_ShiftEvent_Totals ON CashierShiftEvents(ShiftId,Kind,AmountScaled);
CREATE TABLE WorkforceOperations (
 OperationKey TEXT PRIMARY KEY, Action TEXT NOT NULL, RequestHash TEXT NOT NULL, EntityId INTEGER NOT NULL,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), CreatedUtc TEXT NOT NULL
);
CREATE TABLE WorkforceAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, Action TEXT NOT NULL, EntityId INTEGER NOT NULL,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), Detail TEXT NOT NULL, CreatedUtc TEXT NOT NULL
);
CREATE INDEX IX_WorkforceAudit_Entity ON WorkforceAudit(Action,EntityId,Id);
CREATE TRIGGER TR_Employees_NoDelete BEFORE DELETE ON Employees BEGIN SELECT RAISE(ABORT,'employee_history_preserved'); END;
CREATE TRIGGER TR_Attendance_NoDelete BEFORE DELETE ON EmployeeAttendance BEGIN SELECT RAISE(ABORT,'attendance_history_preserved'); END;
CREATE TRIGGER TR_Shift_NoDelete BEFORE DELETE ON CashierShifts BEGIN SELECT RAISE(ABORT,'shift_immutable'); END;
CREATE TRIGGER TR_Shift_Closed BEFORE UPDATE ON CashierShifts WHEN OLD.ClosedUtc IS NOT NULL BEGIN SELECT RAISE(ABORT,'shift_closed'); END;
CREATE TRIGGER TR_Shift_Identity BEFORE UPDATE ON CashierShifts
 WHEN NEW.CashierId IS NOT OLD.CashierId OR NEW.RegisterId IS NOT OLD.RegisterId OR NEW.OpenedUtc IS NOT OLD.OpenedUtc
 OR NEW.OpenedBy IS NOT OLD.OpenedBy OR NEW.OpeningScaled IS NOT OLD.OpeningScaled OR NEW.OpeningNotes IS NOT OLD.OpeningNotes
 BEGIN SELECT RAISE(ABORT,'shift_opening_immutable'); END;
CREATE TRIGGER TR_ShiftEvent_Open BEFORE INSERT ON CashierShiftEvents WHEN NOT EXISTS(SELECT 1 FROM CashierShifts WHERE Id=NEW.ShiftId AND ClosedUtc IS NULL AND NEW.CreatedUtc>=OpenedUtc) BEGIN SELECT RAISE(ABORT,'shift_closed'); END;
CREATE TRIGGER TR_ShiftEvent_NoUpdate BEFORE UPDATE ON CashierShiftEvents BEGIN SELECT RAISE(ABORT,'shift_event_immutable'); END;
CREATE TRIGGER TR_ShiftEvent_NoDelete BEFORE DELETE ON CashierShiftEvents BEGIN SELECT RAISE(ABORT,'shift_event_immutable'); END;
CREATE TRIGGER TR_WorkforceAudit_NoUpdate BEFORE UPDATE ON WorkforceAudit BEGIN SELECT RAISE(ABORT,'audit_immutable'); END;
CREATE TRIGGER TR_WorkforceAudit_NoDelete BEFORE DELETE ON WorkforceAudit BEGIN SELECT RAISE(ABORT,'audit_immutable'); END;
CREATE TRIGGER TR_WorkforceOp_NoUpdate BEFORE UPDATE ON WorkforceOperations BEGIN SELECT RAISE(ABORT,'operation_immutable'); END;
CREATE TRIGGER TR_WorkforceOp_NoDelete BEFORE DELETE ON WorkforceOperations BEGIN SELECT RAISE(ABORT,'operation_immutable'); END;
