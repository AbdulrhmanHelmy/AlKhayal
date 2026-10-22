using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Settings;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Data.Settings;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SettingsPersistenceTests
    {
        [Fact]
        public async Task Company_general_audit_and_database_information_persist_after_restart()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var userId=await new SqliteUserStore(cs).CreateAsync("settings-admin","مسؤول",new PasswordHasher().Hash("secret"),"administrator",CancellationToken.None);var store=new SqliteSettingsStore(cs,path);var service=new SettingsService(store,store);
                Assert.Equal("EGP",(await service.GetGeneralAsync(CancellationToken.None)).CurrencyCode);
                Assert.True((await service.UpdateCompanyAsync(new UpdateCompanyProfileCommand{BusinessName="مؤسسة الخيال",DisplayName="الخيال",Email="info@example.com",ChangedByUserId=userId},CancellationToken.None)).Succeeded);
                Assert.True((await service.UpdateGeneralAsync(new UpdateGeneralSettingsCommand{LanguageCode="ar",DateFormat="yyyy-MM-dd",TimeFormat="hh:mm tt",UseGroupSeparators=true,CurrencyCode="SAR",CurrencySymbol="ر.س",DecimalPrecision=2,TaxEnabled=true,DefaultTaxRate=15,PricesIncludeTax=true,ChangedByUserId=userId},CancellationToken.None)).Succeeded);
                var restarted=new SettingsService(new SqliteSettingsStore(cs,path),new SqliteSettingsStore(cs,path));Assert.Equal("مؤسسة الخيال",(await restarted.GetCompanyAsync(CancellationToken.None)).BusinessName);Assert.Equal("SAR",(await restarted.GetGeneralAsync(CancellationToken.None)).CurrencyCode);Assert.Equal(15m,(await restarted.GetGeneralAsync(CancellationToken.None)).DefaultTaxRate);var info=await restarted.GetDatabaseInformationAsync(CancellationToken.None);Assert.Equal(11,info.AppliedMigrationCount);Assert.True(info.ForeignKeysEnabled);
                using(var connection=new SQLiteConnection(cs))Assert.Equal(2L,await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SettingsAuditLog"));
            }finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Settings_update_and_audit_are_one_transaction()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var userId=await new SqliteUserStore(cs).CreateAsync("rollback-admin","مسؤول",new PasswordHasher().Hash("secret"),"administrator",CancellationToken.None);using(var connection=new SQLiteConnection(cs)){connection.Open();connection.Execute("CREATE TRIGGER RejectSettingsAudit BEFORE INSERT ON SettingsAuditLog BEGIN SELECT RAISE(ABORT, 'audit rejected'); END;");}
                var store=new SqliteSettingsStore(cs,path);await Assert.ThrowsAnyAsync<SQLiteException>(()=>store.UpdateGeneralAsync(new UpdateGeneralSettingsCommand{LanguageCode="ar",DateFormat="yyyy-MM-dd",TimeFormat="HH:mm",UseGroupSeparators=true,CurrencyCode="USD",CurrencySymbol="$",DecimalPrecision=2,ChangedByUserId=userId},CancellationToken.None));Assert.Equal("EGP",(await store.GetGeneralAsync(CancellationToken.None)).CurrencyCode);
            }finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
