using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class CatalogServiceTests
    {
        [Fact]
        public async Task Create_validates_and_scales_money_before_calling_the_store()
        {
            var store=new FakeStore();var service=new CatalogService(store);
            Assert.Equal("validation",(await service.CreateProductAsync(new CreateProductCommand{OperationKey="x",Name="منتج",BaseUnitId=1,ProductType="stock",SalePrice="-1"},CancellationToken.None)).ErrorCode);
            Assert.Null(store.Created);
            Assert.Equal("validation",(await service.CreateProductAsync(new CreateProductCommand{OperationKey="x2",Name="منتج",BaseUnitId=1,ProductType="stock",SalePrice="1",PurchasePrice="-0.01"},CancellationToken.None)).ErrorCode);
            var result=await service.CreateProductAsync(new CreateProductCommand{OperationKey="op-1",Name="  منتج  ",BaseUnitId=1,ProductType="stock",SalePrice="1.23456",PurchasePrice="3.45678",IsActive=true},CancellationToken.None);
            Assert.True(result.Succeeded);Assert.Equal(12346,store.Created.SellPriceScaled);Assert.Equal(34568,store.Created.PurchasePriceScaled);Assert.Equal("منتج",store.Created.Name);Assert.False(string.IsNullOrWhiteSpace(store.Created.RequestHash));
            var unknown=await service.CreateProductAsync(new CreateProductCommand{OperationKey="op-2",Name="بلا تكلفة",BaseUnitId=1,ProductType="stock",SalePrice="2",PurchasePrice=null,IsActive=true},CancellationToken.None);
            Assert.True(unknown.Succeeded);Assert.Null(store.Created.PurchasePriceScaled);Assert.Null(unknown.Value.PurchasePrice);
        }

        [Fact]
        public async Task Tracking_dependencies_are_persisted_on_initial_create_and_affect_idempotency_hash()
        {
            var store = new FakeStore();
            var service = new CatalogService(store);
            var command = new CreateProductCommand { OperationKey="tracking-1", Name="عصير", BaseUnitId=1, ProductType="stock", SalePrice="5", IsActive=true,
                Configuration=new ProductConfigurationCommand { CapabilityKeys=new[] { "expiry-dates", "imei-tracking" } } };
            Assert.True((await service.CreateProductAsync(command, CancellationToken.None)).Succeeded);
            Assert.Contains("batch-tracking", store.Created.Configuration.CapabilityKeys);
            Assert.Contains("serial-numbers", store.Created.Configuration.CapabilityKeys);
            var firstHash=store.Created.RequestHash;
            command.Configuration.CapabilityKeys=new[] { "batch-tracking" };
            Assert.True((await service.CreateProductAsync(command, CancellationToken.None)).Succeeded);
            Assert.NotEqual(firstHash, store.Created.RequestHash);
        }

        [Fact]
        public async Task Simplified_product_choices_are_accepted_when_legacy_global_capabilities_are_disabled()
        {
            var store = new FakeStore();
            var service = new CatalogService(store, new DisabledFeatureReader());
            var result = await service.CreateProductAsync(new CreateProductCommand
            {
                OperationKey = "simple-properties", Name = "منتج جديد", BaseUnitId = 1, ProductType = "stock", SalePrice = "9", IsActive = true,
                Configuration = new ProductConfigurationCommand { CapabilityKeys = new[] { "expiry-dates", "sizes", "colors" } }
            }, CancellationToken.None);
            Assert.True(result.Succeeded);
            Assert.Contains("expiry-dates", store.Created.Configuration.CapabilityKeys);
            Assert.Contains("batch-tracking", store.Created.Configuration.CapabilityKeys);
            Assert.Contains("sizes", store.Created.Configuration.CapabilityKeys);
            Assert.Contains("colors", store.Created.Configuration.CapabilityKeys);
        }

        [Fact]
        public async Task Review_required_legacy_product_cannot_be_activated()
        {
            var store=new FakeStore{Found=new CatalogProductRecord{Id=7,Name="قديم",BaseUnitId=1,BaseUnitName="قطعة",UnitSymbol="قطعة",ProductType="stock",MigrationState="review_required",Version=1}};
            var result=await new CatalogService(store).SetProductStateAsync(new SetProductStateCommand{ProductId=7,IsActive=true,Version=1},CancellationToken.None);
            Assert.Equal("review_required",result.ErrorCode);Assert.False(store.StateChanged);
        }

        private sealed class FakeStore:ICatalogStore
        {
            public CreateProductRecord Created{get;set;}public CatalogProductRecord Found{get;set;}public bool StateChanged{get;set;}
            public Task<CatalogProductRecord> CreateProductAsync(CreateProductRecord record,CancellationToken ct){Created=record;return Task.FromResult(new CatalogProductRecord{Id=1,Name=record.Name,BaseUnitId=record.BaseUnitId,BaseUnitName="قطعة",UnitSymbol="قطعة",ProductType=record.ProductType,SellPriceScaled=record.SellPriceScaled,PurchasePriceScaled=record.PurchasePriceScaled,IsActive=record.IsActive,MigrationState="ready",Version=1});}
            public Task<CatalogProductRecord> CreateAggregateProductAsync(CreateProductRecord record,CancellationToken ct){return CreateProductAsync(record,ct);}
            public Task<CatalogProductRecord> FindProductAsync(long id,CancellationToken ct){return Task.FromResult(Found);}
            public Task<CatalogProductRecord> SetProductStateAsync(long id,bool active,long version,CancellationToken ct){StateChanged=true;return Task.FromResult(Found);}
            public Task<ProductStorePage> ListProductsAsync(ProductStoreQuery query,CancellationToken ct){throw new NotSupportedException();}
            public Task<CatalogProductRecord> UpdateProductAsync(UpdateProductRecord record,CancellationToken ct){throw new NotSupportedException();}
            public Task<CatalogProductRecord> UpdateAggregateProductAsync(UpdateProductRecord record,CancellationToken ct){throw new NotSupportedException();}
            public Task<CatalogProductRecord> ArchiveProductAsync(long id,long version,CancellationToken ct){throw new NotSupportedException();}
            public Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(bool includeInactive,CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductCategory> SaveCategoryAsync(SaveCategoryCommand command,string normalizedName,CancellationToken ct){throw new NotSupportedException();}
            public Task<IReadOnlyList<UnitOfMeasurement>> ListUnitsAsync(bool includeInactive,CancellationToken ct){throw new NotSupportedException();}
            public Task<UnitOfMeasurement> SaveUnitAsync(SaveUnitCommand command,string normalizedCode,CancellationToken ct){throw new NotSupportedException();}
            public Task<IReadOnlyList<ProductMigrationIssue>> ListMigrationIssuesAsync(CancellationToken ct){throw new NotSupportedException();}
            public Task<CatalogProductDetailRecord> GetProductDetailAsync(long productId, CancellationToken ct){throw new NotSupportedException();}
            public Task<IReadOnlyList<ProductAttributeRecord>> ListAttributesAsync(string kind, bool includeInactive, CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductAttributeRecord> GetAttributeAsync(long id, CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductAttributeRecord> CreateAttributeAsync(CreateAttributeRecord record, CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductAttributeRecord> UpdateAttributeAsync(UpdateAttributeRecord record, CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductAttributeValueRecord> GetAttributeValueAsync(long id, CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductAttributeValueRecord> CreateAttributeValueAsync(CreateAttributeValueRecord record, CancellationToken ct){throw new NotSupportedException();}
            public Task<ProductAttributeValueRecord> UpdateAttributeValueAsync(UpdateAttributeValueRecord record, CancellationToken ct){throw new NotSupportedException();}
            public Task<BarcodeLookupRecord> LookupBarcodeAsync(string normalizedBarcode, CancellationToken ct){throw new NotSupportedException();}
        }

        private sealed class DisabledFeatureReader : ICatalogFeatureReader
        {
            public Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(CancellationToken ct) { return Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>()); }
            public Task<bool> IsFeatureEnabledAsync(string featureKey, CancellationToken ct) { return Task.FromResult(false); }
        }
    }
}
