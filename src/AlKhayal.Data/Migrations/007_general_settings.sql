CREATE TABLE IF NOT EXISTS CompanyProfile (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    BusinessName TEXT NOT NULL DEFAULT '',
    DisplayName TEXT,
    Phone TEXT,
    Email TEXT,
    Address TEXT,
    TaxRegistrationNumber TEXT,
    CommercialRegistrationNumber TEXT,
    FooterText TEXT,
    UpdatedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

INSERT OR IGNORE INTO CompanyProfile (Id, BusinessName, UpdatedUtc)
VALUES (1, '', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

CREATE TABLE IF NOT EXISTS ApplicationSettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    LanguageCode TEXT NOT NULL DEFAULT 'ar' CHECK (LanguageCode = 'ar'),
    DateFormat TEXT NOT NULL DEFAULT 'dd/MM/yyyy',
    TimeFormat TEXT NOT NULL DEFAULT 'HH:mm',
    UseGroupSeparators INTEGER NOT NULL DEFAULT 1 CHECK (UseGroupSeparators IN (0, 1)),
    CurrencyCode TEXT NOT NULL DEFAULT 'EGP',
    CurrencySymbol TEXT NOT NULL DEFAULT 'ج.م',
    DecimalPrecision INTEGER NOT NULL DEFAULT 2 CHECK (DecimalPrecision BETWEEN 0 AND 4),
    TaxEnabled INTEGER NOT NULL DEFAULT 0 CHECK (TaxEnabled IN (0, 1)),
    DefaultTaxRate NUMERIC NOT NULL DEFAULT 0 CHECK (DefaultTaxRate >= 0 AND DefaultTaxRate <= 100),
    PricesIncludeTax INTEGER NOT NULL DEFAULT 0 CHECK (PricesIncludeTax IN (0, 1)),
    UpdatedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

INSERT OR IGNORE INTO ApplicationSettings
    (Id, LanguageCode, DateFormat, TimeFormat, UseGroupSeparators, CurrencyCode, CurrencySymbol, DecimalPrecision, TaxEnabled, DefaultTaxRate, PricesIncludeTax, UpdatedUtc)
VALUES
    (1, 'ar', 'dd/MM/yyyy', 'HH:mm', 1, 'EGP', 'ج.م', 2, 0, 0, 0, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

CREATE TABLE IF NOT EXISTS SettingsAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Category TEXT NOT NULL,
    ChangeDetails TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL,
    ChangedUtc TEXT NOT NULL,
    FOREIGN KEY(ChangedByUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_SettingsAuditLog_Category_ChangedUtc
ON SettingsAuditLog(Category, ChangedUtc);
