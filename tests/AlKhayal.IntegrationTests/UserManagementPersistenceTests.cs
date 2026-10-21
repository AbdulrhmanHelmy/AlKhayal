using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Data.Users;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class UserManagementPersistenceTests
    {
        [Fact]
        public async Task Users_permissions_last_admin_and_rollback_are_persisted_transactionally()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path); Migrator.Run(cs); Migrator.Run(cs);
                var authUsers=new SqliteUserStore(cs); var hasher=new PasswordHasher();
                var originalHash=hasher.Hash("admin-secret"); var adminId=await authUsers.CreateAsync("owner","المالك",originalHash,UserRoles.Administrator,CancellationToken.None);
                var store=new SqliteUserManagementStore(cs); var service=new UserManagementService(store,new Catalog(),hasher);
                var secondAdmin=await service.CreateAsync(new CreateUserCommand{Username="admin2",DisplayName="مسؤول ثان",Password="secret",Role=UserRoles.Administrator,IsActive=true,ActingUserId=adminId},CancellationToken.None);
                var first=await service.CreateAsync(new CreateUserCommand{Username="cashier1",DisplayName="كاشير أول",Password="secret",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None);
                var second=await service.CreateAsync(new CreateUserCommand{Username="cashier2",DisplayName="كاشير ثان",Password="secret",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None);
                Assert.True(secondAdmin.Succeeded); Assert.True(first.Succeeded); Assert.True(second.Succeeded);
                Assert.Equal("duplicate_username",(await service.CreateAsync(new CreateUserCommand{Username="CASHIER1",DisplayName="مكرر",Password="x",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("validation",(await service.CreateAsync(new CreateUserCommand{Username="bad",DisplayName="غير صالح",Password="x",Role="manager",IsActive=true,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("invalid_screen",(await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"missing"},ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("administrator_only_screen",(await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"users-list"},ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.True((await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"dashboard","pos"},ActingUserId=adminId},CancellationToken.None)).Succeeded);
                Assert.True((await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=second.Value.Id,ScreenIds=new[]{"sales-list"},ActingUserId=adminId},CancellationToken.None)).Succeeded);
                Assert.Equal(new[]{"dashboard","pos"},await new SqliteUserManagementStore(cs).GetScreenIdsAsync(first.Value.Id,CancellationToken.None));
                Assert.Equal(new[]{"sales-list"},await new SqliteUserManagementStore(cs).GetScreenIdsAsync(second.Value.Id,CancellationToken.None));
                await service.SetStatusAsync(new SetUserStatusCommand{UserId=secondAdmin.Value.Id,IsActive=false,ActingUserId=adminId},CancellationToken.None);
                Assert.Equal("last_active_administrator",(await service.SetStatusAsync(new SetUserStatusCommand{UserId=adminId,IsActive=false,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("last_active_administrator",(await service.UpdateAsync(new UpdateUserCommand{UserId=adminId,Username="owner",DisplayName="المالك",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                await Assert.ThrowsAsync<SQLiteException>(()=>service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"sales-list"},ActingUserId=999999},CancellationToken.None));
                Assert.Equal(new[]{"dashboard","pos"},await store.GetScreenIdsAsync(first.Value.Id,CancellationToken.None));
                using(var connection=new SQLiteConnection(cs)){var hash=connection.ExecuteScalar<string>("SELECT PasswordHash FROM Users WHERE Id=@Id",new{Id=adminId});Assert.Equal(originalHash,hash);Assert.True(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM UserAuditLog")>=5);}
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        [Fact]
        public async Task Effective_access_combines_role_grants_and_enabled_modules()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var hasher=new PasswordHasher();var authUsers=new SqliteUserStore(cs);var admin=await authUsers.CreateAsync("admin","مسؤول",hasher.Hash("x"),UserRoles.Administrator,CancellationToken.None);var store=new SqliteUserManagementStore(cs);var service=new UserManagementService(store,new Catalog(),hasher);var cashier=(await service.CreateAsync(new CreateUserCommand{Username="cashier",DisplayName="كاشير",Password="x",Role=UserRoles.Cashier,IsActive=true,ActingUserId=admin},CancellationToken.None)).Value;await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=cashier.Id,ScreenIds=new[]{"dashboard","pos","sales-list"},ActingUserId=admin},CancellationToken.None);
                var features=new FeatureSnapshot{Modules=new[]{new EffectiveFeature{Key="sales",IsEnabled=true},new EffectiveFeature{Key="pos",IsEnabled=false}},Capabilities=new EffectiveFeature[0]};
                var access=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=cashier.Id,Role=UserRoles.Cashier},features,CancellationToken.None);Assert.Contains("dashboard",access.ScreenIds);Assert.Contains("sales-list",access.ScreenIds);Assert.DoesNotContain("pos",access.ScreenIds);Assert.DoesNotContain("users-list",access.ScreenIds);
                features.Modules=new[]{new EffectiveFeature{Key="sales",IsEnabled=true},new EffectiveFeature{Key="pos",IsEnabled=true}};access=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=cashier.Id,Role=UserRoles.Cashier},features,CancellationToken.None);Assert.Contains("pos",access.ScreenIds);
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        private sealed class Catalog:IScreenCatalog
        {
            public IReadOnlyList<ScreenDefinition> Screens{get;}=new[]{new ScreenDefinition{Id="dashboard",ArabicName="الرئيسية",Module="الرئيسية"},new ScreenDefinition{Id="pos",ArabicName="نقطة البيع",Module="المبيعات",FeatureKey="pos"},new ScreenDefinition{Id="sales-list",ArabicName="المبيعات",Module="المبيعات",FeatureKey="sales"},new ScreenDefinition{Id="users-list",ArabicName="المستخدمون",Module="الإعدادات",AdministratorOnly=true}};
            public ScreenDefinition Find(string id){return Screens.FirstOrDefault(item=>string.Equals(item.Id,id,StringComparison.OrdinalIgnoreCase));}
        }
    }
}
