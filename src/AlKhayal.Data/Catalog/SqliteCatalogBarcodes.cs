using System;
using System.Data.SQLite;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AlKhayal.Application.Catalog;
using Dapper;

namespace AlKhayal.Data.Catalog
{
    public sealed partial class SqliteCatalogStore : IProductBarcodeStore
    {
        public async Task<BarcodeChangeResult> ChangeBarcodeAsync(BarcodeChange x, long userId, CancellationToken ct)
        {
            var json = new JavaScriptSerializer().Serialize(new { x.ProductId, x.VariantId, x.ProductUnitId, x.ReplaceBarcodeId, x.Version, x.Code, x.Format, x.Generate, userId });
            string hash; using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(json)));
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var previous = await c.QuerySingleOrDefaultAsync<Operation>(new CommandDefinition("SELECT o.RequestHash,o.ActorId,b.Id,b.Barcode Code,o.Format,o.ProductVersion Version FROM BarcodeOperations o JOIN ProductBarcodes b ON b.Id=o.BarcodeId WHERE o.OperationKey=@OperationKey", x, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (previous != null)
                    {
                        if (previous.RequestHash != hash || previous.ActorId != userId) throw Error("operation_key_conflict", "مفتاح العملية مستخدم لطلب مختلف.");
                        return new BarcodeChangeResult { Id = previous.Id, Code = previous.Code, Format = previous.Format, Version = previous.Version, Replayed = true };
                    }
                    var version = await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("SELECT d.Version FROM ProductDetails d JOIN ProductVariants v ON v.ProductId=d.ProductId JOIN ProductUnits u ON u.ProductId=d.ProductId WHERE d.ProductId=@ProductId AND v.Id=@VariantId AND u.Id=@ProductUnitId AND d.ArchivedUtc IS NULL AND v.ArchivedUtc IS NULL AND v.IsActive=1 AND u.IsActive=1 AND u.IsSaleUnit=1", x, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (!version.HasValue) throw Error("not_found", "اختر منتجًا ومتغيرًا ووحدة بيع محفوظة ومتاحة.");
                    if (version != x.Version) throw Error("version_conflict", "تغير المنتج. أعد تحميل بياناته قبل تعديل الباركود.");
                    var now = UtcNow();
                    ProductBarcodeRecord old = null;
                    if (x.ReplaceBarcodeId.HasValue)
                    {
                        old = await c.QuerySingleOrDefaultAsync<ProductBarcodeRecord>(new CommandDefinition("SELECT * FROM ProductBarcodes WHERE Id=@ReplaceBarcodeId AND ProductId=@ProductId AND VariantId=@VariantId AND ProductUnitId=@ProductUnitId AND RetiredUtc IS NULL", x, tx, cancellationToken: ct)).ConfigureAwait(false);
                        if (old == null) throw Error("not_found", "الباركود السابق غير متاح للتعديل.");
                    }
                    var code = x.Generate ? BarcodeRules.GenerateInternal() : x.Code;
                    var attempts = 0;
                    while (await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ProductBarcodes WHERE NormalizedBarcode=@code", new { code = NormalizeFormKc(code) }, tx, cancellationToken: ct)).ConfigureAwait(false) > 0 && (old == null || old.Barcode != code))
                    { if (!x.Generate || ++attempts > 12) throw Error("duplicate_barcode", "الباركود مستخدم أو محفوظ في سجل الأكواد المتقاعدة. اختر رمزًا آخر."); code = BarcodeRules.GenerateInternal(); }
                    long id;
                    if (old != null && old.Barcode == code) id = old.Id;
                    else
                    {
                        if (old != null) await c.ExecuteAsync(new CommandDefinition("UPDATE ProductBarcodes SET RetiredUtc=@now,RetiredByUserId=@userId,IsPrimary=0 WHERE Id=@Id", new { now, userId, old.Id }, tx, cancellationToken: ct)).ConfigureAwait(false);
                        var primary = old?.IsPrimary == true || await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ProductBarcodes WHERE VariantId=@VariantId AND ProductUnitId=@ProductUnitId AND RetiredUtc IS NULL AND IsPrimary=1", x, tx, cancellationToken: ct)).ConfigureAwait(false) == 0;
                        id = await c.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO ProductBarcodes(ProductId,VariantId,ProductUnitId,Barcode,NormalizedBarcode,BarcodeType,IsPrimary,CreatedUtc,CreatedByUserId) VALUES(@ProductId,@VariantId,@ProductUnitId,@code,@norm,@type,@primary,@now,@userId); SELECT last_insert_rowid();", new { x.ProductId, x.VariantId, x.ProductUnitId, code, norm = NormalizeFormKc(code), type = x.Format == "ean13" ? "gtin" : "internal", primary, now, userId }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    }
                    await c.ExecuteAsync(new CommandDefinition("INSERT OR REPLACE INTO BarcodeSymbologies(BarcodeId,Format) VALUES(@id,@Format); UPDATE ProductDetails SET Version=Version+1,UpdatedUtc=@now WHERE ProductId=@ProductId; INSERT INTO BarcodeOperations(OperationKey,RequestHash,ActorId,BarcodeId,ProductVersion,Format,CreatedUtc) VALUES(@OperationKey,@hash,@userId,@id,@next,@Format,@now)", new { id, x.Format, x.ProductId, now, x.OperationKey, hash, userId, next = version.Value + 1 }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    await AuditAsync(c, tx, x.ProductId, "barcode", id, old == null ? "create" : "replace", new JavaScriptSerializer().Serialize(new { barcodeId = id, replacedId = old?.Id, code, x.Format, x.VariantId, x.ProductUnitId }), userId, now, ct).ConfigureAwait(false);
                    tx.Commit(); return new BarcodeChangeResult { Id = id, Code = code, Format = x.Format, Version = version.Value + 1 };
                }
            }
        }
        private sealed class Operation { public string RequestHash { get; set; } public long ActorId { get; set; } public long Id { get; set; } public string Code { get; set; } public string Format { get; set; } public long Version { get; set; } }
    }
}
