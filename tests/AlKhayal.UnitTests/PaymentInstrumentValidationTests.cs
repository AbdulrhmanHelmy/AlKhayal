using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Payments;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class PaymentInstrumentValidationTests
    {
        private sealed class Store : IPaymentInstrumentStore
        {
            public string Identity;
            public Task<PaymentInstrument> SaveAsync(PaymentInstrument value,string identity,long actor,CancellationToken ct){Identity=identity;return Task.FromResult(value);}
            public Task<IReadOnlyList<PaymentInstrument>> ListAsync(string type,bool activeOnly,CancellationToken ct)=>throw new NotImplementedException();
            public Task<PaymentHistoryPage> HistoryAsync(PaymentHistoryQuery q,CancellationToken ct)=>throw new NotImplementedException();
            public Task<PaymentOverview> OverviewAsync(CancellationToken ct)=>throw new NotImplementedException();
        }
        [Fact]
        public async Task Identity_normalization_is_unambiguous_case_and_space_insensitive_and_preserves_zeroes()
        {
            var store=new Store();var service=new PaymentInstrumentService(store);
            var value=await service.SaveAsync(new PaymentInstrument{Type="wallet",Name=" محفظة ",Number=" 00123 ",Provider=" Provider "},1,CancellationToken.None);var first=store.Identity;
            Assert.Equal("00123",value.Number);Assert.Equal("محفظة",value.Name);
            await service.SaveAsync(new PaymentInstrument{Type="wallet",Name="محفظة",Number="00 123",Provider="pRoViDeR"},1,CancellationToken.None);Assert.Equal(first,store.Identity);
            await service.SaveAsync(new PaymentInstrument{Type="wallet",Name="محفظة",Number="123",Provider="Provider"},1,CancellationToken.None);Assert.NotEqual(first,store.Identity);
        }
        [Theory]
        [InlineData("bank","name","123")]
        [InlineData("wallet"," ","123")]
        [InlineData("card","name"," ")]
        public async Task Invalid_instrument_data_is_rejected_before_persistence(string type,string name,string number)
        {await Assert.ThrowsAsync<ArgumentException>(()=>new PaymentInstrumentService(new Store()).SaveAsync(new PaymentInstrument{Type=type,Name=name,Number=number},1,CancellationToken.None));}
        [Theory]
        [InlineData("2026-13-01",null,1,20)]
        [InlineData("2026-10-29","2026-10-28",1,20)]
        [InlineData(null,null,0,20)]
        [InlineData(null,null,1,101)]
        public async Task History_rejects_invalid_dates_and_unbounded_pages(string from,string to,int page,int pageSize)
        {await Assert.ThrowsAsync<ArgumentException>(()=>new PaymentInstrumentService(new Store()).HistoryAsync(new PaymentHistoryQuery{From=from,To=to,Page=page,PageSize=pageSize},CancellationToken.None));}
    }
}
