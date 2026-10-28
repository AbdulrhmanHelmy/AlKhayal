using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Returns;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Suppliers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Returns;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Suppliers;
using AlKhayal.Data.Customers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class ReturnManagementTests
    {
        internal static PurchaseService Purchases(SalesTreasuryTests.Fixture f) => new PurchaseService(new SqlitePurchaseRepository(f.Db.ConnectionString), new SqliteSupplierRepository(f.Db.ConnectionString));
        internal static ReturnService Service(SalesTreasuryTests.Fixture f) => new ReturnService(new SqliteReturnReadStore(f.Db.ConnectionString), f.Sales, Purchases(f));
        private static SaleReturnCommand SaleReturn(SaleInvoice invoice) => new SaleReturnCommand { OperationKey = "managed-return", Reason = "مرتجع موثق", Lines = new System.Collections.Generic.List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = invoice.Lines[0].Id, Quantity = "1" } } };
        [Fact]
        public async Task Sales_list_and_detail_track_partial_then_complete_returns_without_changing_posted_status()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); await f.FundRefund("24.68"); var draft=await f.Draft(quantity:"2");
                Assert.Equal("none",draft.ReturnStatus); Assert.False(draft.IsFullyReturned);
                var invoice=(await f.Post(draft.Id)).Invoice; Assert.Equal("none",invoice.ReturnStatus);
                var command=SaleReturn(invoice); Assert.True((await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None)).Succeeded);
                Assert.Equal("partial",(await f.Sales.ListAsync(null,null,null,CancellationToken.None)).Single().ReturnStatus);
                Assert.Equal("partial",(await f.Sales.GetAsync(invoice.Id,CancellationToken.None)).ReturnStatus);
                command.OperationKey="second-part"; Assert.True((await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None)).Succeeded);
                var complete=(await f.Sales.ListAsync(null,null,null,CancellationToken.None)).Single(); Assert.Equal("full",complete.ReturnStatus); Assert.Equal("posted",complete.Status);
                Assert.Equal("full",(await f.Sales.GetAsync(invoice.Id,CancellationToken.None)).ReturnStatus);
            }
        }
        [Fact]
        public async Task Sales_return_withdraws_only_from_returns_cashbox_and_leaves_shift_ledger_version_and_audit_unchanged()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var draft = await f.Draft(); var posted = await f.Post(draft.Id); Assert.True(posted.Succeeded);
                await f.FundRefund();
                long shiftVersion, shiftAudit;
                using (var c = f.Db.Open()) { shiftVersion = c.ExecuteScalar<long>("SELECT Version FROM TreasuryAccounts WHERE Id=@id", new { id = posted.Invoice.TreasuryAccountId }); shiftAudit = c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryAudit WHERE AccountId=@id", new { id = posted.Invoice.TreasuryAccountId }); }
                var found = await Service(f).LookupAsync("sales", draft.InvoiceNumber, CancellationToken.None); Assert.Equal(draft.Id, found.Id); Assert.Equal("1", found.Lines[0].Available);
                var result = await f.Sales.ReturnAsync(draft.Id, SaleReturn(posted.Invoice), f.Db.UserId, CancellationToken.None); Assert.True(result.Succeeded, result.ErrorMessage);
                var detail = await Service(f).GetAsync("sales", result.ReturnId, CancellationToken.None); Assert.Equal("12.34", detail.Total); Assert.Equal("12.34", detail.Cash); Assert.Single(detail.Movements); Assert.Single(detail.Lines); Assert.Equal("1", detail.Lines[0].Quantity);
                Assert.Equal("refund", detail.Movements[0].Role); Assert.Equal("مرتجعات البيع", detail.Movements[0].AccountName);
                var balances = await f.Db.Service.ListAsync(CancellationToken.None); Assert.Equal("12.34", balances.Single(x => x.Name == "إيراد الوردية").Balance); Assert.Equal("0.00", balances.Single(x => x.Name == "مرتجعات البيع").Balance);
                var replay = await f.Sales.ReturnAsync(draft.Id, SaleReturn(posted.Invoice), f.Db.UserId, CancellationToken.None); Assert.True(replay.Replayed); Assert.Equal(result.ReturnId, replay.ReturnId);
                var rows = await Service(f).ListAsync(draft.InvoiceNumber, CancellationToken.None); Assert.Single(rows); Assert.Equal("sales", rows[0].Type);
                Assert.Empty(await Service(f).ListAsync("missing-number", CancellationToken.None)); Assert.Null(await Service(f).LookupAsync("sales", "missing-number", CancellationToken.None));
                var refreshed = await Service(f).LookupAsync("sales", draft.InvoiceNumber, CancellationToken.None); Assert.Equal("0", refreshed.Lines[0].Available);
                using (var c = f.Db.Open()) { Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements")); Assert.Equal(shiftVersion,c.ExecuteScalar<long>("SELECT Version FROM TreasuryAccounts WHERE Id=@id",new{id=posted.Invoice.TreasuryAccountId})); Assert.Equal(shiftAudit,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryAudit WHERE AccountId=@id",new{id=posted.Invoice.TreasuryAccountId})); Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements WHERE AccountId=@id",new{id=posted.Invoice.TreasuryAccountId})); Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check")); Assert.Throws<System.Data.SQLite.SQLiteException>(() => c.Execute("DELETE FROM ReturnTreasuryLinks")); }
            }
        }
        [Fact]
        public async Task Sales_refund_from_zero_creates_negative_returns_balance_and_late_failure_rolls_back_all_writes()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var draft = await f.Draft(); var sale = (await f.Post(draft.Id)).Invoice;
                using (var c = f.Db.Open()) c.Execute("CREATE TRIGGER ReturnFailure BEFORE INSERT ON ReturnTreasuryLinks WHEN NEW.Role='refund' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Sales.ReturnAsync(sale.Id, SaleReturn(sale), f.Db.UserId, CancellationToken.None)).Succeeded);
                using (var c = f.Db.Open()) { Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryLinks")); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements")); Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns")); Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryAccounts")); Assert.Equal(19000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances")); Assert.Equal(123400,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices")); c.Execute("DROP TRIGGER ReturnFailure"); }
                var returned=await f.Sales.ReturnAsync(sale.Id, SaleReturn(sale), f.Db.UserId, CancellationToken.None); Assert.True(returned.Succeeded,returned.ErrorMessage);
                var accounts=await f.Db.Service.ListAsync(CancellationToken.None); var refunds=accounts.Single(x=>x.Name=="مرتجعات البيع"); Assert.Equal("-12.34",refunds.Balance); Assert.Equal("12.34",accounts.Single(x=>x.Name=="إيراد الوردية").Balance);
                Assert.True((await f.Sales.ReturnAsync(sale.Id,SaleReturn(sale),f.Db.UserId,CancellationToken.None)).Replayed);
                Assert.Equal("insufficient_balance",(await Assert.ThrowsAsync<AlKhayal.Application.Treasury.TreasuryException>(()=>f.Db.Post(refunds.Id,"1","withdraw"))).Code);
                await f.Db.Post(refunds.Id,"2"); Assert.Equal("-10.34",(await f.Db.Service.ListAsync(CancellationToken.None)).Single(x=>x.Id==refunds.Id).Balance);
                using(var c=f.Db.Open()) { Assert.Equal(-123400,c.ExecuteScalar<long>("SELECT BalanceAfterScaled FROM TreasuryMovements WHERE OperationKey='sale-return:refund:managed-return'")); Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check")); }
            }
        }
        [Theory]
        [InlineData("0", "24.68", "0.00", "0.00")]
        [InlineData("20", "4.68", "20.00", "0.00")]
        [InlineData("24.68", "0.00", "24.68", "0.00")]
        public async Task Credit_returns_reduce_debt_to_zero_and_refund_the_remainder_even_without_invoice_linked_payments(string collected,string debtReduction,string refund,string remainingDebt)
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var customerService=new AlKhayal.Application.Customers.CustomerService(new SqliteCustomerRepository(f.Db.ConnectionString));
                var customer=customerService.Create(new Customer{Code="REFUND-C",Name="عميل مرتجع آجل",Phone="01000000000",Status="نشط"});
                var draft=await f.Draft(customer:customer.Id,quantity:"2");var invoice=(await f.Post(draft.Id)).Invoice;
                if(collected!="0") new AlKhayal.Application.Customers.CustomerAccountService(new SqliteCustomerAccountRepository(f.Db.ConnectionString)).PostTransaction(customer.Id,"collection",decimal.Parse(collected,System.Globalization.CultureInfo.InvariantCulture),"2026-10-29","تحصيل على الحساب","refund-account-collection");
                var command=SaleReturn(invoice);command.Lines[0].Quantity="2";
                using(var c=f.Db.Open())c.Execute("CREATE TRIGGER CreditRefundFailure BEFORE UPDATE OF PaidScaled ON SalesInvoices BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None)).Succeeded);
                using(var c=f.Db.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));Assert.Equal(18000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));c.Execute("DROP TRIGGER CreditRefundFailure");}
                var result=await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None);Assert.True(result.Succeeded,result.ErrorMessage);
                Assert.Equal(debtReduction,AlKhayal.Application.Finance.MoneyPolicy.FormatForUser(result.CreditReductionScaled));Assert.Equal(refund,AlKhayal.Application.Finance.MoneyPolicy.FormatForUser(result.CashRefundScaled));
                using(var c=f.Db.Open()){Assert.Equal(decimal.Parse(remainingDebt,System.Globalization.CultureInfo.InvariantCulture),c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@id",new{id=customer.Id}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ShiftRevenueTreasury"));}
                Assert.True((await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None)).Replayed);
                var accounts=await f.Db.Service.ListAsync(CancellationToken.None);
                if(refund=="0.00")Assert.Empty(accounts);else Assert.Equal("-"+refund,Assert.Single(accounts).Balance);
            }
        }
        [Fact]
        public async Task Credit_return_reduces_debt_first_even_when_the_invoice_already_has_a_cash_payment()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare();var customer=new AlKhayal.Application.Customers.CustomerService(new SqliteCustomerRepository(f.Db.ConnectionString)).Create(new Customer{Code="DEBT-FIRST",Name="عميل أولوية المديونية",Phone="01000000000",Status="نشط"});
                var invoice=(await f.Post((await f.Draft(customer:customer.Id,quantity:"2")).Id)).Invoice;
                var paid=await f.Sales.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OccurredOn="2026-10-29",OperationKey="debt-first-paid"},f.Db.UserId,CancellationToken.None);Assert.True(paid.Succeeded,paid.ErrorMessage);
                var command=SaleReturn(invoice);var first=await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);Assert.Equal(123400,first.CreditReductionScaled);Assert.Equal(0,first.CashRefundScaled);Assert.Empty(await f.Db.Service.ListAsync(CancellationToken.None));
                command.OperationKey="debt-first-second";var second=await f.Sales.ReturnAsync(invoice.Id,command,f.Db.UserId,CancellationToken.None);Assert.True(second.Succeeded,second.ErrorMessage);Assert.Equal(73400,second.CreditReductionScaled);Assert.Equal(50000,second.CashRefundScaled);Assert.Equal("-5.00",Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
                using(var c=f.Db.Open()){Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@id",new{id=customer.Id}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ShiftRevenueTreasury"));}
            }
        }
        [Fact]
        public async Task Purchase_cash_return_deposits_in_own_cashbox_while_credit_settlement_has_no_treasury_movement()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(); var supplier = new SupplierService(new SqliteSupplierRepository(f.Db.ConnectionString)).Create(new Supplier { Code = "MANAGED-S", Name = "مورد المرتجعات", Phone = "01000000000", Status = "نشط" });
                var service = Purchases(f); var invoice = await service.CreateDraftAsync(new PurchaseDraftCommand { SupplierId = supplier.Id, WarehouseId = 1, InvoiceNumber = "PUR-MANAGED", InvoiceDate = "2026-10-28", PaymentMode = "credit", Lines = new System.Collections.Generic.List<PurchaseInvoiceLine> { new PurchaseInvoiceLine { VariantId = f.Variant, ProductUnitId = f.Unit, Quantity = "3", PurchaseUnitPrice = "9", SaleUnitPrice = "12.34" } } }, f.Db.UserId, CancellationToken.None);
                var post = await service.PostAsync(invoice.Id, "managed-purchase", f.Db.UserId, CancellationToken.None); Assert.True(post.Succeeded, post.ErrorMessage);
                Assert.Equal("none",post.Invoice.ReturnStatus);
                var eligible = await Service(f).LookupAsync("purchase", invoice.InvoiceNumber, CancellationToken.None); Assert.Equal("3", eligible.Lines[0].Available);
                var command = new PurchaseReturnCommand { WarehouseId = 1, ReturnMode = "cash_refund", Lines = new System.Collections.Generic.List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = post.Invoice.Lines[0].Id, Quantity = "1" } } };
                using (var c = f.Db.Open()) c.Execute("CREATE TRIGGER PurchaseReturnFailure BEFORE INSERT ON ReturnTreasuryLinks WHEN NEW.Role='purchase_refund' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await service.PostReturnAsync(invoice.Id, command, "managed-purchase-return", f.Db.UserId, CancellationToken.None)).Succeeded);
                using (var c = f.Db.Open()) { Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseReturns")); Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements")); Assert.Equal(23000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances")); c.Execute("DROP TRIGGER PurchaseReturnFailure"); }
                var cash = await service.PostReturnAsync(invoice.Id, command, "managed-purchase-return", f.Db.UserId, CancellationToken.None); Assert.True(cash.Succeeded, cash.ErrorMessage);
                Assert.Equal("9.00", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
                Assert.Equal("partial",(await service.ListAsync(null,null,null,CancellationToken.None)).Single().ReturnStatus);
                Assert.Equal("partial",(await service.GetAsync(invoice.Id,CancellationToken.None)).ReturnStatus);
                var detail = await Service(f).GetAsync("purchase", cash.Id, CancellationToken.None); Assert.Single(detail.Movements); Assert.Equal("مرتجعات الشراء", detail.Movements[0].AccountName); Assert.Equal("deposit", detail.Movements[0].Type); Assert.Single(detail.Lines);
                Assert.True((await service.PostReturnAsync(invoice.Id, command, "managed-purchase-return", f.Db.UserId, CancellationToken.None)).Replayed);
                command.ReturnMode = "supplier_credit"; var credit = await service.PostReturnAsync(invoice.Id, command, "managed-purchase-credit", f.Db.UserId, CancellationToken.None); Assert.True(credit.Succeeded, credit.ErrorMessage); Assert.Empty((await Service(f).GetAsync("purchase", credit.Id, CancellationToken.None)).Movements);
                Assert.Equal(2, (await Service(f).ListAsync("PUR-MANAGED", CancellationToken.None)).Count);
                Assert.Equal("9.00", Assert.Single(await f.Db.Service.ListAsync(CancellationToken.None)).Balance);
                Assert.Equal("partial",(await service.GetAsync(invoice.Id,CancellationToken.None)).ReturnStatus);
                Assert.True((await service.PostReturnAsync(invoice.Id,command,"managed-purchase-final",f.Db.UserId,CancellationToken.None)).Succeeded);
                var complete=(await service.ListAsync(null,null,null,CancellationToken.None)).Single(); Assert.Equal("full",complete.ReturnStatus); Assert.Equal("posted",complete.Status);
                Assert.Equal("full",(await service.GetAsync(invoice.Id,CancellationToken.None)).ReturnStatus);
            }
        }
    }
}
