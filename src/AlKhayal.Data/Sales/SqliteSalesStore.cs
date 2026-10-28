using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Inventory;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore : ISalesStore
    {
        private readonly string _connectionString;
        private readonly SqliteInventoryStore _inventory;
        public SqliteSalesStore(string connectionString){_connectionString=connectionString+"Default Timeout=5;";_inventory=new SqliteInventoryStore(connectionString);}
        private SQLiteConnection Open(){var c=new SQLiteConnection(_connectionString);c.Open();using(var cmd=new SQLiteCommand("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",c))cmd.ExecuteNonQuery();return c;}
        private static CommandDefinition Cmd(string sql,object args,SQLiteTransaction tx,CancellationToken ct){return new CommandDefinition(sql,args,tx,cancellationToken:ct);}
        private static string Now(){return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);}

        public async Task<SaleInvoice> CreateDraftAsync(SaleDraftCommand command,long subtotal,long discount,long tax,long total,long userId,CancellationToken ct)
        {
            using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var settings=await TaxAsync(c,tx,ct).ConfigureAwait(false);var totals=Totals(subtotal,discount,settings.Rate,settings.Enabled,settings.Included);
                ValidateReferences(c,tx,command,ct).GetAwaiter().GetResult();var now=Now();var number=NextNumber();
                await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,CustomerId,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,PaidScaled,TaxRate,TaxIncluded,CreatedUtc)
                    VALUES(@Number,@Date,@CustomerId,@WarehouseId,@UserId,'draft',@Mode,@Subtotal,@Discount,@Tax,@Total,0,@Rate,@Included,@Now)",new{Number=number,Date=command.InvoiceDate,command.CustomerId,command.WarehouseId,UserId=userId,Mode=command.PaymentMode,Subtotal=subtotal,Discount=discount,Tax=totals.tax,Total=totals.total,Rate=settings.Rate,Included=settings.Included?1:0,Now=now},tx,ct)).ConfigureAwait(false);
                var id=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                await SaveLinesAsync(c,tx,id,command.Lines,ct).ConfigureAwait(false);await FinalizeLinesAsync(c,tx,id,discount,totals.tax,settings.Included,ct).ConfigureAwait(false);await AuditAsync(c,tx,id,userId,"created",now,"",ct).ConfigureAwait(false);tx.Commit();return await GetAsync(id,ct).ConfigureAwait(false);
            }
        }
        public async Task<SaleInvoice> UpdateDraftAsync(long id,SaleDraftCommand command,long subtotal,long discount,long tax,long total,long userId,CancellationToken ct)
        {
            using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var row=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT * FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);if(row==null)return null;if(row.Status!="draft"||await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesDraftDeletions WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)throw new InvalidOperationException("المسودة محذوفة أو الفاتورة منشورة.");
                await ValidateReferences(c,tx,command,ct).ConfigureAwait(false);var settings=await TaxAsync(c,tx,ct).ConfigureAwait(false);var totals=Totals(subtotal,discount,settings.Rate,settings.Enabled,settings.Included);var now=Now();
                var changed=await c.ExecuteAsync(Cmd(@"UPDATE SalesInvoices SET InvoiceDate=@Date,CustomerId=@CustomerId,WarehouseId=@WarehouseId,PaymentMode=@Mode,SubtotalScaled=@Subtotal,DiscountScaled=@Discount,TaxScaled=@Tax,TotalScaled=@Total,TaxRate=@Rate,TaxIncluded=@Included,Version=Version+1 WHERE Id=@Id AND Status='draft' AND Version=@Version",new{Date=command.InvoiceDate,command.CustomerId,command.WarehouseId,Mode=command.PaymentMode,Subtotal=subtotal,Discount=discount,Tax=totals.tax,Total=totals.total,Rate=settings.Rate,Included=settings.Included?1:0,Id=id,row.Version},tx,ct)).ConfigureAwait(false);if(changed!=1)throw new InvalidOperationException("تغيرت المسودة. حدّث الشاشة ثم أعد المحاولة.");
                await c.ExecuteAsync(Cmd("DELETE FROM SalesInvoiceLines WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);await SaveLinesAsync(c,tx,id,command.Lines,ct).ConfigureAwait(false);await FinalizeLinesAsync(c,tx,id,discount,totals.tax,settings.Included,ct).ConfigureAwait(false);await AuditAsync(c,tx,id,userId,"draft_updated",now,"",ct).ConfigureAwait(false);tx.Commit();return await GetAsync(id,ct).ConfigureAwait(false);
            }
        }
        public async Task<SaleInvoice> GetAsync(long id,CancellationToken ct)
        {using(var c=Open()){var row=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd(HeaderQuery+" WHERE si.Id=@Id AND NOT EXISTS(SELECT 1 FROM SalesDraftDeletions dd WHERE dd.SalesInvoiceId=si.Id)",new{Id=id},null,ct)).ConfigureAwait(false);if(row==null)return null;row.Lines=await LinesAsync(c,null,id,ct).ConfigureAwait(false);row.Returns=(await c.QueryAsync<SaleReturnSummary>(new CommandDefinition("SELECT Id,ReturnNumber,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedUtc FROM SalesReturns WHERE SalesInvoiceId=@Id ORDER BY Id",new{Id=id},cancellationToken:ct)).ConfigureAwait(false)).ToList();return row;}}
        public async Task<IReadOnlyList<SaleInvoice>> ListAsync(string search,string from,string to,CancellationToken ct)
        {using(var c=Open()){var rows=(await c.QueryAsync<SaleInvoice>(new CommandDefinition(HeaderQuery+@" WHERE NOT EXISTS(SELECT 1 FROM SalesDraftDeletions dd WHERE dd.SalesInvoiceId=si.Id) AND (@Search IS NULL OR si.InvoiceNumber LIKE @Like OR COALESCE(c.Name,'') LIKE @Like) AND (@From IS NULL OR si.InvoiceDate>=@From) AND (@To IS NULL OR si.InvoiceDate<=@To) ORDER BY si.Id DESC LIMIT 500",new{Search=string.IsNullOrWhiteSpace(search)?null:search,Like="%"+(search??"")+"%",From=from,To=to},cancellationToken:ct)).ConfigureAwait(false)).ToList();return rows;}}
        private const string HeaderQuery=@"SELECT EXISTS(SELECT 1 FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id) HasReturns,EXISTS(SELECT 1 FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id) AND NOT EXISTS(SELECT 1 FROM SalesInvoiceLines sl WHERE sl.SalesInvoiceId=si.Id AND sl.QuantityScaled>COALESCE((SELECT SUM(rl.QuantityScaled) FROM SalesReturnLines rl WHERE rl.SalesInvoiceLineId=sl.Id),0)) IsFullyReturned,si.Id,si.InvoiceNumber,si.InvoiceDate,si.CustomerId,c.Name CustomerName,si.WarehouseId,w.Name WarehouseName,si.CashierUserId,u.DisplayName CashierName,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId=si.Id) THEN 'canceled' ELSE si.Status END Status,si.PaymentMode,si.SubtotalScaled,si.DiscountScaled,si.TaxScaled,si.TotalScaled,si.PaidScaled,COALESCE((SELECT SUM(sr.TotalScaled) FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id),0) ReturnedScaled,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId=si.Id) THEN 0 ELSE MAX(0,si.TotalScaled-COALESCE((SELECT SUM(sr.TotalScaled) FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id),0)-si.PaidScaled) END OutstandingScaled,si.InventoryDocumentId,si.OperationKey,si.RequestHash,si.Version
             ,(SELECT TreasuryAccountId FROM SalesTreasuryReceipts r WHERE r.SalesInvoiceId=si.Id) TreasuryAccountId,(SELECT TreasuryMovementId FROM SalesTreasuryReceipts r WHERE r.SalesInvoiceId=si.Id) TreasuryMovementId,(SELECT a.Name FROM SalesTreasuryReceipts r JOIN TreasuryAccounts a ON a.Id=r.TreasuryAccountId WHERE r.SalesInvoiceId=si.Id) TreasuryAccountName FROM SalesInvoices si JOIN Warehouses w ON w.Id=si.WarehouseId JOIN Users u ON u.Id=si.CashierUserId LEFT JOIN Customers c ON c.Id=si.CustomerId";

        public async Task<SalePostResult> PostAsync(long id,long version,string key,string hash,long userId,CancellationToken ct)
        {
            try{using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var invoice=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT * FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);if(invoice==null)return SalePostResult.Fail("not_found","الفاتورة غير موجودة.");
                var priorKey=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT Id,RequestHash FROM SalesInvoices WHERE OperationKey=@Key",new{Key=key},tx,ct)).ConfigureAwait(false);
                if(priorKey!=null&&(priorKey.Id!=id||priorKey.RequestHash!=hash))return SalePostResult.Fail("operation_key_conflict","مفتاح العملية مستخدم لطلب بيع مختلف.");
                if(invoice.Status=="posted")return invoice.OperationKey==key&&invoice.RequestHash==hash?new SalePostResult{Succeeded=true,Invoice=await GetAsync(id,ct).ConfigureAwait(false)}:SalePostResult.Fail("already_posted","الفاتورة منشورة بالفعل.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesDraftDeletions WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return SalePostResult.Fail("draft_deleted","حُذفت المسودة ولا يمكن نشرها.");
                if(invoice.Version!=version)return SalePostResult.Fail("draft_changed","تغيرت المسودة. حدّث الشاشة وأعد النشر.");
                invoice.Lines=await LinesAsync(c,tx,id,ct).ConfigureAwait(false);var command=new InventoryPostCommand{OperationKey="sale:"+key,Type="manual_issue",WarehouseId=invoice.WarehouseId,UserId=userId,Reason="صرف فاتورة بيع "+invoice.InvoiceNumber,Notes="salesInvoiceId="+id,Lines=invoice.Lines.Select(x=>new InventoryLineInput{VariantId=x.VariantId,ProductUnitId=x.ProductUnitId,Quantity=x.Quantity,LotNumber=x.BatchLotNumber,ExpirationDate=x.ExpirationDate,PhysicalUnits=x.PhysicalUnits.Select(u=>new InventoryUnitInput{Serial=u.Serial,Imeis=u.Imeis}).ToList()}).ToList()};
                var stock=await _inventory.PostWithinTransactionAsync(c,tx,command,InventoryService.CanonicalHashPost(command),ct).ConfigureAwait(false);if(!stock.Succeeded)return SalePostResult.Fail(stock.ErrorCode,stock.ErrorMessage);
                await c.ExecuteAsync(Cmd("UPDATE SalesInvoiceLines SET InventoryDocumentLineId=(SELECT dl.Id FROM InventoryDocumentLines dl WHERE dl.DocumentId=@DocumentId AND dl.LineNumber=SalesInvoiceLines.LineNumber) WHERE SalesInvoiceId=@Id",new{DocumentId=stock.Value.DocumentId,Id=id},tx,ct)).ConfigureAwait(false);
                if(invoice.PaymentMode=="credit")
                {
                    if(!invoice.CustomerId.HasValue)return SalePostResult.Fail("customer_required","البيع الآجل يتطلب اختيار عميل.");
                    var customer=await c.QuerySingleOrDefaultAsync<CustomerFinancial>(Cmd("SELECT Balance,CreditLimit FROM Customers WHERE Id=@Id AND Status='نشط'",new{Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                    if(customer==null)return SalePostResult.Fail("customer_unavailable","العميل غير متاح.");
                    var next=checked(customer.Balance+invoice.TotalScaled/10000m);
                    if(customer.CreditLimit>0&&next>customer.CreditLimit)return SalePostResult.Fail("credit_limit","تتجاوز الفاتورة حد ائتمان العميل.");
                    await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId) VALUES(@CustomerId,@Key,@Hash,'increase',@Amount,@Date,@Notes,@Now,@Reference,@InvoiceId)",new{CustomerId=invoice.CustomerId,Key="sale-invoice:"+id,Hash=hash,Amount=invoice.TotalScaled,Date=invoice.InvoiceDate,Notes="فاتورة بيع "+invoice.InvoiceNumber,Now=Now(),Reference=invoice.InvoiceNumber,InvoiceId=id},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=@Balance,UpdatedAt=@Now WHERE Id=@Id",new{Balance=next,Now=Now(),Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                }
                else { await DepositCashSaleAsync(c,tx,invoice,key,hash,userId,ct).ConfigureAwait(false); await c.ExecuteAsync(Cmd("INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc) VALUES(@Id,@CustomerId,@UserId,'cash_sale','cash',@Amount,@Key,@Hash,@Date,@Now)",new{Id=id,invoice.CustomerId,UserId=userId,Amount=invoice.TotalScaled,Key="cash-sale:"+key,Hash=hash,Date=invoice.InvoiceDate,Now=Now()},tx,ct)).ConfigureAwait(false); }
                var updated=await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET Status='posted',InventoryDocumentId=@DocumentId,OperationKey=@Key,RequestHash=@Hash,PostedUtc=@Now,PaidScaled=CASE WHEN PaymentMode='cash' THEN TotalScaled ELSE 0 END WHERE Id=@Id AND Status='draft' AND Version=@Version",new{DocumentId=stock.Value.DocumentId,Key=key,Hash=hash,Now=Now(),Id=id,Version=version},tx,ct)).ConfigureAwait(false);if(updated!=1)return SalePostResult.Fail("draft_changed","تغيرت المسودة قبل النشر.");
                await AuditAsync(c,tx,id,userId,"posted",Now(),"inventoryDocumentId="+stock.Value.DocumentId.ToString(CultureInfo.InvariantCulture),ct).ConfigureAwait(false);tx.Commit();return new SalePostResult{Succeeded=true,Invoice=await GetAsync(id,ct).ConfigureAwait(false)};
            }}catch(AlKhayal.Application.Treasury.TreasuryException e){return SalePostResult.Fail("treasury_"+e.Code,e.Message);}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return SalePostResult.Fail("database_busy","قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه.");}catch(OverflowException){return SalePostResult.Fail("numeric_overflow","قيمة الفاتورة تتجاوز الحد المسموح.");}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return SalePostResult.Fail("sales_conflict","تعارضت الفاتورة مع حركة أخرى. حدّث الشاشة وأعد المحاولة.");}
        }
        private static async Task<List<SaleLine>> LinesAsync(SQLiteConnection c,SQLiteTransaction tx,long id,CancellationToken ct)
        {
            var lines=(await c.QueryAsync<SaleLine>(Cmd(@"SELECT l.Id,l.VariantId,p.Name ProductName,COALESCE(NULLIF((SELECT group_concat(Value,' / ') FROM (SELECT pav.Value Value FROM ProductVariantValues vv JOIN ProductAttributeValues pav ON pav.Id=vv.AttributeValueId WHERE vv.VariantId=v.Id ORDER BY vv.AttributeId,pav.SortOrder,pav.Id)),''),v.Name) VariantName,l.ProductUnitId,u.Name UnitName,
                CAST(l.QuantityScaled AS TEXT) Quantity,CAST(COALESCE((SELECT SUM(rl.QuantityScaled) FROM SalesReturnLines rl WHERE rl.SalesInvoiceLineId=l.Id),0) AS TEXT) ReturnedQuantity,
                l.InventoryDocumentLineId,l.BatchLotNumber,l.ExpirationDate,CAST(l.UnitPriceScaled AS TEXT) UnitPrice,CAST(l.DiscountScaled AS TEXT) Discount,l.InvoiceDiscountScaled,l.TaxScaled,l.LineTotalScaled
                FROM SalesInvoiceLines l JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId
                WHERE l.SalesInvoiceId=@Id ORDER BY l.LineNumber",new{Id=id},tx,ct)).ConfigureAwait(false)).ToList();
            foreach(var x in lines)
            {
                x.Quantity=Format(Scale(x.Quantity),6);x.ReturnedQuantity=Format(Scale(x.ReturnedQuantity),6);x.UnitPrice=MoneyPolicy.FormatForUser(Scale(x.UnitPrice));x.Discount=MoneyPolicy.FormatForUser(Scale(x.Discount));x.LineTotal=MoneyPolicy.FormatForUser(x.LineTotalScaled);
                x.PhysicalUnits=(await c.QueryAsync<PersistedUnit>(Cmd("SELECT Serial,Imeis FROM SalesInvoiceLinePhysicalUnits WHERE SalesInvoiceLineId=@Id ORDER BY UnitNumber",new{Id=x.Id},tx,ct)).ConfigureAwait(false))
                    .Select(unit=>new SaleTrackingUnit{Serial=unit.Serial,Imeis=string.IsNullOrEmpty(unit.Imeis)?new List<string>():unit.Imeis.Split('|').ToList()}).ToList();
                x.AvailableTrackingUnits=(await c.QueryAsync<AvailableUnit>(Cmd(@"SELECT serial.DisplayValue Serial,COALESCE((SELECT group_concat(imei.DisplayValue,'|') FROM InventoryIdentifiers imei WHERE imei.PhysicalUnitId=p.Id AND imei.Kind='imei'),'') ImeiValues
                    FROM StockMovementUnits smu JOIN StockMovements m ON m.Id=smu.MovementId JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId JOIN InventoryIdentifiers serial ON serial.PhysicalUnitId=p.Id AND serial.Kind='serial'
                    WHERE m.Id=(SELECT Id FROM StockMovements WHERE DocumentLineId=@DocumentLineId AND QuantityBaseScaled<0 LIMIT 1) AND p.WarehouseId IS NULL ORDER BY p.Id",new{DocumentLineId=x.InventoryDocumentLineId},tx,ct)).ConfigureAwait(false))
                    .Select(u=>new SaleTrackingUnit{Serial=u.Serial,Imeis=string.IsNullOrEmpty(u.ImeiValues)?new List<string>():u.ImeiValues.Split('|').ToList()}).ToList();
            }
            return lines;
        }
        private sealed class AvailableUnit{public string Serial{get;set;}public string ImeiValues{get;set;}}
        private sealed class PersistedUnit{public string Serial{get;set;}public string Imeis{get;set;}}
        private static async Task SaveLinesAsync(SQLiteConnection c,SQLiteTransaction tx,long id,IList<SaleLine> lines,CancellationToken ct)
        {var n=0;foreach(var x in lines){n++;long q,p,d;ScaleInput(x.Quantity,6,out q);MoneyPolicy.TryParseUserAmount(x.UnitPrice,false,out p);MoneyPolicy.TryParseUserAmount(x.Discount??"0",false,out d);var gross=MoneyPolicy.FinalizeQuantityAmount(q,p);await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceLines(SalesInvoiceId,LineNumber,VariantId,ProductUnitId,QuantityScaled,UnitPriceScaled,DiscountScaled,InvoiceDiscountScaled,TaxScaled,LineTotalScaled,BatchLotNumber,ExpirationDate) VALUES(@Id,@N,@VariantId,@Unit,@Q,@P,@D,0,0,@Total,@Lot,@Expiry)",new{Id=id,N=n,x.VariantId,Unit=x.ProductUnitId,Q=q,P=p,D=d,Total=gross-d,Lot=x.BatchLotNumber,Expiry=x.ExpirationDate},tx,ct)).ConfigureAwait(false);var lineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);var index=0;foreach(var unit in x.PhysicalUnits??new List<SaleTrackingUnit>()){index++;await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceLinePhysicalUnits(SalesInvoiceLineId,UnitNumber,Serial,Imeis) VALUES(@Line,@Number,@Serial,@Imeis)",new{Line=lineId,Number=index,unit.Serial,Imeis=string.Join("|",unit.Imeis??new List<string>())},tx,ct)).ConfigureAwait(false);}}}
        private sealed class LineValue { public long Id{get;set;} public long NetScaled{get;set;} }
        private static async Task FinalizeLinesAsync(SQLiteConnection c,SQLiteTransaction tx,long id,long invoiceDiscount,long tax,bool included,CancellationToken ct)
        {
            var lines=(await c.QueryAsync<LineValue>(Cmd("SELECT Id,LineTotalScaled NetScaled FROM SalesInvoiceLines WHERE SalesInvoiceId=@Id ORDER BY LineNumber",new{Id=id},tx,ct)).ConfigureAwait(false)).ToList();
            var weight=lines.Sum(x=>(decimal)x.NetScaled);long discountLeft=invoiceDiscount,taxLeft=tax;
            for(var i=0;i<lines.Count;i++)
            {
                var line=lines[i];var last=i==lines.Count-1;var discount=last?discountLeft:weight==0?0:checked((long)(decimal.Floor((decimal)invoiceDiscount*line.NetScaled/weight/100m)*100m));discountLeft-=discount;
                var taxable=line.NetScaled-discount;var lineTax=last?taxLeft:weight==0?0:checked((long)(decimal.Floor((decimal)tax*taxable/(weight-invoiceDiscount)/100m)*100m));taxLeft-=lineTax;
                var finalTotal=included?taxable:checked(taxable+lineTax);
                await c.ExecuteAsync(Cmd("UPDATE SalesInvoiceLines SET InvoiceDiscountScaled=@Discount,TaxScaled=@Tax,LineTotalScaled=@Total WHERE Id=@Id",new{Discount=discount,Tax=lineTax,Total=finalTotal,Id=line.Id},tx,ct)).ConfigureAwait(false);
            }
        }
        private static async Task ValidateReferences(SQLiteConnection c,SQLiteTransaction tx,SaleDraftCommand command,CancellationToken ct)
        {var warehouse=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Warehouses WHERE Id=@Id AND IsActive=1",new{Id=command.WarehouseId},tx,ct)).ConfigureAwait(false);if(warehouse!=1)throw new ArgumentException("المخزن غير نشط أو غير موجود.");if(command.CustomerId.HasValue&&await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Customers WHERE Id=@Id AND Status='نشط'",new{Id=command.CustomerId},tx,ct)).ConfigureAwait(false)!=1)throw new ArgumentException("العميل غير نشط أو غير موجود.");foreach(var x in command.Lines){var product=await c.QuerySingleOrDefaultAsync<SellableProduct>(Cmd(@"SELECT COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) Price FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId JOIN ProductUnits pu ON pu.ProductId=v.ProductId AND pu.Id=@Unit AND pu.IsActive=1 AND pu.IsSaleUnit=1 WHERE v.Id=@Variant AND v.IsActive=1 AND v.ArchivedUtc IS NULL AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND d.ProductType='stock'",new{Unit=x.ProductUnitId,Variant=x.VariantId},tx,ct)).ConfigureAwait(false);long entered;if(product==null||!MoneyPolicy.TryParseUserAmount(x.UnitPrice,false,out entered)||entered!=product.Price)throw new ArgumentException("المنتج أو وحدة البيع غير متاح، أو تغير سعر البيع؛ حدّث السعر قبل الحفظ.");}}
        private sealed class SellableProduct{public long Price{get;set;}}
        private sealed class TaxState{public bool Enabled{get;set;}public decimal Rate{get;set;}public bool Included{get;set;}}
        private static Task<TaxState> TaxAsync(SQLiteConnection c,SQLiteTransaction tx,CancellationToken ct){return c.QuerySingleAsync<TaxState>(Cmd("SELECT TaxEnabled Enabled,DefaultTaxRate Rate,PricesIncludeTax Included FROM ApplicationSettings WHERE Id=1",null,tx,ct));}
        private static (long tax,long total) Totals(long subtotal,long discount,decimal rate,bool enabled,bool included){var baseAmount=checked(subtotal-discount);if(!enabled||rate==0)return(0,baseAmount);var taxAmount=included?baseAmount*rate/(100m+rate):baseAmount*rate/100m;var tax=MoneyPolicy.RoundInternalToPiastre(checked((long)decimal.Round(taxAmount,0,MidpointRounding.AwayFromZero)));return(tax,included?baseAmount:checked(baseAmount+tax));}
        private static Task AuditAsync(SQLiteConnection c,SQLiteTransaction tx,long id,long userId,string type,string now,string details,CancellationToken ct){return c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceAudit(SalesInvoiceId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,@Type,@Now,@Details)",new{Id=id,UserId=userId,Type=type,Now=now,Details=details},tx,ct));}
        private static string NextNumber(){return "SAL-"+DateTime.UtcNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8).ToUpperInvariant();}
        private static long Scale(string text){long x;long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out x);return x;}
        private static bool ScaleInput(string text,int digits,out long result){decimal d;if(!decimal.TryParse(text,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out d)){result=0;return false;}result=checked((long)(d*(digits==6?1000000m:10000m)));return true;}
        private static string Format(long value,int digits){return (value/(digits==6?1000000m:10000m)).ToString(digits==6?"0.######":"0.0000",CultureInfo.InvariantCulture);}
    }
}
