using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AlKhayal.Application.Catalog;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using Dapper;

namespace AlKhayal.IntegrationTests
{
    public sealed class CatalogPersistenceTests
    {
        [Fact]
        public async Task Catalog_create_update_search_state_archive_and_idempotency_persist()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var service=new CatalogService(new SqliteCatalogStore(cs));
                var category=(await service.SaveCategoryAsync(new SaveCategoryCommand{Name="مشروبات",IsActive=true,SortOrder=1},CancellationToken.None)).Value;
                var unit=(await service.SaveUnitAsync(new SaveUnitCommand{Code="BOX",Name="علبة",Symbol="ع",IsActive=true},CancellationToken.None)).Value;
                var create=new CreateProductCommand{OperationKey="create-1",Name="شاي",Description="وصف",CategoryId=category.Id,BaseUnitId=unit.Id,ProductType=ProductTypes.Stock,SalePrice="12.34",PurchasePrice="9.87",IsActive=true};
                var first=await service.CreateProductAsync(create,CancellationToken.None);Assert.True(first.Succeeded);Assert.Equal("12.34",first.Value.SalePrice);Assert.Equal("9.87",first.Value.PurchasePrice);
                var retry=await service.CreateProductAsync(create,CancellationToken.None);Assert.True(retry.Succeeded);Assert.Equal(first.Value.Id,retry.Value.Id);
                create.Name="قهوة";var conflict=await service.CreateProductAsync(create,CancellationToken.None);Assert.Equal("operation_key_conflict",conflict.ErrorCode);
                var list=await service.ListProductsAsync(new ProductQuery{Search="شاي",CategoryId=category.Id,IsActive=true,Page=1,PageSize=10},CancellationToken.None);Assert.Single(list.Value.Items);
                var updated=await service.UpdateProductAsync(new UpdateProductCommand{ProductId=first.Value.Id,Name="شاي فاخر",CategoryId=category.Id,BaseUnitId=unit.Id,ProductType=ProductTypes.Stock,SalePrice="20",PurchasePrice="11.11",IsActive=true,Version=first.Value.Version},CancellationToken.None);Assert.True(updated.Succeeded);Assert.Equal(2,updated.Value.Version);Assert.Equal("11.11",updated.Value.PurchasePrice);
                var stale=await service.UpdateProductAsync(new UpdateProductCommand{ProductId=first.Value.Id,Name="قديم",BaseUnitId=unit.Id,ProductType=ProductTypes.Stock,SalePrice="1",IsActive=true,Version=1},CancellationToken.None);Assert.Equal("version_conflict",stale.ErrorCode);
                var stopped=await service.SetProductStateAsync(new SetProductStateCommand{ProductId=first.Value.Id,IsActive=false,Version=updated.Value.Version},CancellationToken.None);Assert.False(stopped.Value.IsActive);
                var archived=await service.ArchiveProductAsync(new ArchiveProductCommand{ProductId=first.Value.Id,Version=stopped.Value.Version},CancellationToken.None);Assert.True(archived.Value.IsArchived);
                var restarted=new CatalogService(new SqliteCatalogStore(cs));var found=await restarted.GetProductAsync(first.Value.Id,CancellationToken.None);Assert.True(found.Value.IsArchived);Assert.Equal("شاي فاخر",found.Value.Name);Assert.Equal("11.11",found.Value.PurchasePrice);
                using(var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    var defaultVariant = connection.QuerySingleOrDefault<dynamic>("SELECT Id, IsDefault, IsActive FROM ProductVariants WHERE ProductId=@ProductId", new { ProductId = first.Value.Id });
                    Assert.NotNull(defaultVariant);
                    Assert.Equal(1L, (long)defaultVariant.IsDefault);
                    var baseUnitRow = connection.QuerySingleOrDefault<dynamic>("SELECT UnitId, ToBaseNumerator, ToBaseDenominator, IsBase FROM ProductUnits WHERE ProductId=@ProductId", new { ProductId = first.Value.Id });
                    Assert.NotNull(baseUnitRow);
                    Assert.Equal(unit.Id, (long)baseUnitRow.UnitId);
                    Assert.Equal(1L, (long)baseUnitRow.IsBase);
                }
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Category_cycles_duplicates_unit_constraints_and_invalid_product_references_are_rejected()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var service=new CatalogService(new SqliteCatalogStore(cs));
                var root=(await service.SaveCategoryAsync(new SaveCategoryCommand{Name="أصل",IsActive=true},CancellationToken.None)).Value;
                var child=(await service.SaveCategoryAsync(new SaveCategoryCommand{Name="فرع",ParentCategoryId=root.Id,IsActive=true},CancellationToken.None)).Value;
                var edited=await service.SaveCategoryAsync(new SaveCategoryCommand{CategoryId=child.Id,Name="فرع معدل",ParentCategoryId=root.Id,IsActive=false,SortOrder=4},CancellationToken.None);Assert.True(edited.Succeeded);Assert.Equal("فرع معدل",edited.Value.Name);Assert.False(edited.Value.IsActive);Assert.Equal(root.Id,edited.Value.ParentCategoryId);
                Assert.Equal("category_cycle",(await service.SaveCategoryAsync(new SaveCategoryCommand{CategoryId=root.Id,Name="أصل",ParentCategoryId=child.Id,IsActive=true},CancellationToken.None)).ErrorCode);
                Assert.Equal("duplicate_category",(await service.SaveCategoryAsync(new SaveCategoryCommand{Name="أصل",IsActive=true},CancellationToken.None)).ErrorCode);
                Assert.Equal("duplicate_unit",(await service.SaveUnitAsync(new SaveUnitCommand{Code="pcs",Name="نسخة",Symbol="ن",IsActive=true},CancellationToken.None)).ErrorCode);
                Assert.Equal("protected_unit",(await service.SaveUnitAsync(new SaveUnitCommand{UnitId=1,Code="PCS",Name="قطعة",Symbol="قطعة",IsActive=false},CancellationToken.None)).ErrorCode);
                var invalid=await service.CreateProductAsync(new CreateProductCommand{OperationKey="invalid-ref",Name="منتج",BaseUnitId=999,ProductType="stock",SalePrice="5",IsActive=true},CancellationToken.None);Assert.Equal("invalid_unit",invalid.ErrorCode);
                Assert.False(CatalogService.TryScaleMoney("-1",out _));Assert.False(CatalogService.TryScaleMoney("1,000",out _));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
