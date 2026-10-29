-- Phase 7 allocation; Phase 6 numbering remains untouched.
CREATE TABLE PrintingConfiguration (
 Id INTEGER PRIMARY KEY CHECK(Id=1), Version INTEGER NOT NULL,
 Json TEXT NOT NULL, ChangedByUserId INTEGER NOT NULL REFERENCES Users(Id), UpdatedUtc TEXT NOT NULL
);
CREATE TABLE PrintJobs (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestKey TEXT NOT NULL UNIQUE,
 DocumentType TEXT NOT NULL, DocumentId INTEGER NOT NULL, Reference TEXT NOT NULL,
 ActorId INTEGER NOT NULL REFERENCES Users(Id), PrinterName TEXT, TemplateId TEXT NOT NULL,
 Copies INTEGER NOT NULL CHECK(Copies BETWEEN 1 AND 10), Reprint INTEGER NOT NULL,
 CreatedUtc TEXT NOT NULL, Status TEXT NOT NULL CHECK(Status IN ('accepted','submitted_unknown','failed','uncertain')),
 ErrorCode TEXT, Message TEXT NOT NULL, ReviewedByUserId INTEGER REFERENCES Users(Id), ReviewedUtc TEXT
);
CREATE UNIQUE INDEX IX_PrintJobs_ActiveDocument ON PrintJobs(DocumentType,DocumentId) WHERE Status='accepted';
CREATE INDEX IX_PrintJobs_Recent ON PrintJobs(Id DESC);
CREATE TABLE PrintingAudit (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, Version INTEGER NOT NULL, ActorId INTEGER NOT NULL REFERENCES Users(Id), CreatedUtc TEXT NOT NULL
);
