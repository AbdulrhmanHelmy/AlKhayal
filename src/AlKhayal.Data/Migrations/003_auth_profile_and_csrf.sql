ALTER TABLE Users ADD COLUMN DisplayName TEXT NOT NULL DEFAULT '';
ALTER TABLE Users ADD COLUMN Role TEXT NOT NULL DEFAULT 'operator';
ALTER TABLE Users ADD COLUMN UpdatedUtc TEXT;
UPDATE Users SET DisplayName = Username WHERE DisplayName = '';
UPDATE Users SET UpdatedUtc = CreatedUtc WHERE UpdatedUtc IS NULL;

ALTER TABLE Sessions ADD COLUMN CsrfToken TEXT NOT NULL DEFAULT '';
CREATE UNIQUE INDEX IF NOT EXISTS IX_Sessions_CsrfToken ON Sessions(CsrfToken) WHERE CsrfToken <> '';
