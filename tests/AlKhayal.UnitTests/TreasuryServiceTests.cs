using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class TreasuryServiceTests
    {
        private sealed class FakeStore : ITreasuryStore
        {
            public TreasuryAccountCommand Saved;
            public string Code, Type, Hash;
            public long? Id;
            public long Actor, Amount, Version;
            public TreasuryMovementCommand Movement;
            public Task<IReadOnlyList<TreasuryAccount>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TreasuryAccount>>(new[] { new TreasuryAccount { Id=1,Type="cash",BalanceScaled=1000000 },new TreasuryAccount { Id=2,Type="bank",BalanceScaled=2500000 } });
            public Task<TreasuryAccount> SaveAsync(long? id,TreasuryAccountCommand command,string normalizedCode,long userId,CancellationToken ct)
            { Saved=command;Code=normalizedCode;Id=id;Actor=userId;return Task.FromResult(new TreasuryAccount { Id=id??9,Code=command.Code,Name=command.Name,Type=command.Type,Notes=command.Notes,Version=command.Version }); }
            public Task DeleteAsync(long id,long version,long userId,CancellationToken ct) { Id=id;Version=version;Actor=userId;return Task.CompletedTask; }
            public Task<TreasuryMovement> PostAsync(TreasuryMovementCommand command,string type,long amount,string hash,long userId,CancellationToken ct)
            { Movement=command;Type=type;Amount=amount;Hash=hash;Actor=userId;return Task.FromResult(new TreasuryMovement { Id=7,AccountId=command.AccountId,Type=type,AmountScaled=amount }); }
        }
        private static TreasuryAccountCommand Account(string type="cash") => new TreasuryAccountCommand { Code=" box-1 ",Name=" الخزنة الرئيسية ",Type=type,Notes=" بيان " };
        private static TreasuryMovementCommand Movement(string amount="100.25") => new TreasuryMovementCommand { AccountId=1,Amount=amount,Notes=" إيداع ",OperationKey=" key-1 " };
        [Theory]
        [InlineData("cash")]
        [InlineData("bank")]
        public async Task Account_creation_trims_metadata_and_normalizes_code(string type)
        {
            var store=new FakeStore();await new TreasuryService(store).SaveAsync(null,Account(type),7,CancellationToken.None);
            Assert.Equal("BOX-1",store.Code);Assert.Equal("box-1",store.Saved.Code);Assert.Equal("الخزنة الرئيسية",store.Saved.Name);Assert.Equal("بيان",store.Saved.Notes);Assert.Null(store.Id);Assert.Equal(7,store.Actor);
        }
        [Theory]
        [InlineData("","cash")]
        [InlineData("حساب","wallet")]
        [InlineData("حساب",null)]
        public async Task Account_validation_rejects_missing_name_or_invalid_type(string name,string type)
        {
            var input=Account(type);input.Name=name;var store=new FakeStore();await Assert.ThrowsAsync<TreasuryException>(()=>new TreasuryService(store).SaveAsync(null,input,1,CancellationToken.None));Assert.Null(store.Saved);
        }
        [Fact]
        public async Task Editing_requires_valid_identity_and_version_and_preserves_the_version()
        {
            var store=new FakeStore();var service=new TreasuryService(store);var input=Account();await Assert.ThrowsAsync<TreasuryException>(()=>service.SaveAsync(1,input,1,CancellationToken.None));input.Version=3;await service.SaveAsync(2,input,7,CancellationToken.None);Assert.Equal(2,store.Id);Assert.Equal(3,store.Saved.Version);
        }
        [Fact]
        public async Task Delete_validates_identity_and_version_before_delegating()
        {
            var store=new FakeStore();var service=new TreasuryService(store);await Assert.ThrowsAsync<TreasuryException>(()=>service.DeleteAsync(0,1,1,CancellationToken.None));await Assert.ThrowsAsync<TreasuryException>(()=>service.DeleteAsync(1,0,1,CancellationToken.None));await service.DeleteAsync(2,3,7,CancellationToken.None);Assert.Equal(2,store.Id);Assert.Equal(3,store.Version);Assert.Equal(7,store.Actor);
        }
        [Fact]
        public async Task Deposit_scales_exact_money_and_hash_canonicalization_is_actor_and_payload_bound()
        {
            var store=new FakeStore();var service=new TreasuryService(store);await service.PostAsync(Movement(),"deposit",7,CancellationToken.None);var first=store.Hash;
            Assert.Equal(1002500,store.Amount);Assert.Equal("إيداع",store.Movement.Notes);Assert.Equal("key-1",store.Movement.OperationKey);Assert.Equal("deposit",store.Type);Assert.Equal(7,store.Actor);
            await Assert.ThrowsAsync<TreasuryException>(()=>service.PostAsync(Movement("100.250"),"deposit",7,CancellationToken.None));
            await service.PostAsync(Movement("100.25"),"deposit",7,CancellationToken.None);Assert.Equal(first,store.Hash);
            await service.PostAsync(Movement("100.26"),"deposit",7,CancellationToken.None);Assert.NotEqual(first,store.Hash);
            await service.PostAsync(Movement(),"deposit",8,CancellationToken.None);Assert.NotEqual(first,store.Hash);
            await service.PostAsync(Movement(),"withdraw",7,CancellationToken.None);Assert.NotEqual(first,store.Hash);
        }
        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("10.005")]
        [InlineData("abc")]
        public async Task Monetary_validation_rejects_zero_negative_excess_precision_and_invalid_text(string amount)
        {
            var store=new FakeStore();await Assert.ThrowsAsync<TreasuryException>(()=>new TreasuryService(store).PostAsync(Movement(amount),"deposit",1,CancellationToken.None));Assert.Null(store.Movement);
        }
        [Fact]
        public async Task Movements_require_account_operation_key_and_supported_direction()
        {
            var service=new TreasuryService(new FakeStore());var input=Movement();input.AccountId=0;await Assert.ThrowsAsync<TreasuryException>(()=>service.PostAsync(input,"deposit",1,CancellationToken.None));input.AccountId=1;input.OperationKey=" ";await Assert.ThrowsAsync<TreasuryException>(()=>service.PostAsync(input,"deposit",1,CancellationToken.None));input.OperationKey="key";await Assert.ThrowsAsync<TreasuryException>(()=>service.PostAsync(input,"transfer",1,CancellationToken.None));
        }
        [Fact]
        public async Task List_preserves_cash_and_bank_balances_as_exact_two_decimal_text()
        {
            var rows=await new TreasuryService(new FakeStore()).ListAsync(CancellationToken.None);Assert.Collection(rows,cash=>Assert.Equal("100.00",cash.Balance),bank=>Assert.Equal("250.00",bank.Balance));
        }
    }
}
