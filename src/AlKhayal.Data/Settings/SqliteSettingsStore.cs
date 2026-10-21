using System;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Settings;
using Dapper;

namespace AlKhayal.Data.Settings
{
    public sealed class SqliteSettingsStore : ISettingsStore, IDatabaseInformationProvider
    {
        private readonly string _connectionString;
        private readonly string _databasePath;
        public SqliteSettingsStore(string connectionString, string databasePath) { _connectionString = connectionString; _databasePath = databasePath; }

        public async Task<CompanyProfile> GetCompanyAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return await connection.QuerySingleAsync<CompanyProfile>(new CommandDefinition("SELECT BusinessName, DisplayName, Phone, Email, Address, TaxRegistrationNumber, CommercialRegistrationNumber, FooterText, UpdatedUtc FROM CompanyProfile WHERE Id=1", cancellationToken: ct)).ConfigureAwait(false);
        }

        public async Task<GeneralSettings> GetGeneralAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return await connection.QuerySingleAsync<GeneralSettings>(new CommandDefinition("SELECT LanguageCode, DateFormat, TimeFormat, UseGroupSeparators, CurrencyCode, CurrencySymbol, DecimalPrecision, TaxEnabled, DefaultTaxRate, PricesIncludeTax, UpdatedUtc FROM ApplicationSettings WHERE Id=1", cancellationToken: ct)).ConfigureAwait(false);
        }

        public async Task<CompanyProfile> UpdateCompanyAsync(UpdateCompanyProfileCommand command, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var now = DateTime.UtcNow.ToString("o");
                    await connection.ExecuteAsync(new CommandDefinition("UPDATE CompanyProfile SET BusinessName=@BusinessName, DisplayName=@DisplayName, Phone=@Phone, Email=@Email, Address=@Address, TaxRegistrationNumber=@TaxRegistrationNumber, CommercialRegistrationNumber=@CommercialRegistrationNumber, FooterText=@FooterText, UpdatedUtc=@Now, ChangedByUserId=@ChangedByUserId WHERE Id=1", new { command.BusinessName, command.DisplayName, command.Phone, command.Email, command.Address, command.TaxRegistrationNumber, command.CommercialRegistrationNumber, command.FooterText, Now = now, command.ChangedByUserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(connection, transaction, "company", "company profile updated", command.ChangedByUserId, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
            return await GetCompanyAsync(ct).ConfigureAwait(false);
        }

        public async Task<GeneralSettings> UpdateGeneralAsync(UpdateGeneralSettingsCommand command, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var now = DateTime.UtcNow.ToString("o");
                    await connection.ExecuteAsync(new CommandDefinition("UPDATE ApplicationSettings SET LanguageCode=@LanguageCode, DateFormat=@DateFormat, TimeFormat=@TimeFormat, UseGroupSeparators=@UseGroupSeparators, CurrencyCode=@CurrencyCode, CurrencySymbol=@CurrencySymbol, DecimalPrecision=@DecimalPrecision, TaxEnabled=@TaxEnabled, DefaultTaxRate=@DefaultTaxRate, PricesIncludeTax=@PricesIncludeTax, UpdatedUtc=@Now, ChangedByUserId=@ChangedByUserId WHERE Id=1", new { command.LanguageCode, command.DateFormat, command.TimeFormat, command.UseGroupSeparators, command.CurrencyCode, command.CurrencySymbol, command.DecimalPrecision, command.TaxEnabled, command.DefaultTaxRate, command.PricesIncludeTax, Now = now, command.ChangedByUserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    await Audit(connection, transaction, "general", "currency/tax/display settings updated", command.ChangedByUserId, now, ct).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
            return await GetGeneralAsync(ct).ConfigureAwait(false);
        }

        public async Task<DatabaseInformation> GetAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM SchemaVersion", cancellationToken: ct)).ConfigureAwait(false);
                var latest = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT Name FROM SchemaVersion ORDER BY Name DESC LIMIT 1", cancellationToken: ct)).ConfigureAwait(false);
                var journal = await connection.QuerySingleAsync<string>(new CommandDefinition("PRAGMA journal_mode", cancellationToken: ct)).ConfigureAwait(false);
                var foreignKeys = await connection.ExecuteScalarAsync<long>(new CommandDefinition("PRAGMA foreign_keys", cancellationToken: ct)).ConfigureAwait(false);
                var file = new System.IO.FileInfo(_databasePath);
                return new DatabaseInformation { DatabasePath = _databasePath, SizeBytes = file.Exists ? file.Length : 0, AppliedMigrationCount = count, LatestMigration = latest, JournalMode = journal, ForeignKeysEnabled = foreignKeys == 1 };
            }
        }

        private static Task<int> Audit(SQLiteConnection connection, SQLiteTransaction transaction, string category, string details, long userId, string now, CancellationToken ct)
        {
            return connection.ExecuteAsync(new CommandDefinition("INSERT INTO SettingsAuditLog (Category, ChangeDetails, ChangedByUserId, ChangedUtc) VALUES (@Category, @Details, @UserId, @Now)", new { Category = category, Details = details, UserId = userId, Now = now }, transaction, cancellationToken: ct));
        }
    }
}
