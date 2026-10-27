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
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        public async Task<SalePaymentResult> AddPaymentAsync(long id,SalePaymentCommand command,long userId,CancellationToken ct)
        {
            long amount;MoneyPolicy.TryParseUserAmount(command.Amount,false,out amount);var hash=PaymentHash(id,command,userId);
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var replay=await c.QuerySingleOrDefaultAsync<PaymentReplay>(Cmd("SELECT Id,SalesInvoiceId,RequestHash FROM SalesPayments WHERE OperationKey=@Key",new{Key=command.OperationKey},tx,ct)).ConfigureAwait(false);
                    if(replay!=null){tx.Commit();return replay.SalesInvoiceId==id&&replay.RequestHash==hash?new SalePaymentResult{Succeeded=true,Replayed=true,PaymentId=replay.Id,Invoice=await GetAsync(id,ct).ConfigureAwait(false)}:new SalePaymentResult{ErrorCode="operation_key_conflict",ErrorMessage="مفتاح العملية مستخدم لبيانات مختلفة."};}
                    var invoice=await c.QuerySingleOrDefaultAsync<PaymentInvoice>(Cmd("SELECT si.Id,si.CustomerId,si.Status,si.PaymentMode,si.TotalScaled,si.PaidScaled,si.InvoiceDate,si.InvoiceNumber,COALESCE((SELECT SUM(TotalScaled) FROM SalesReturns WHERE SalesInvoiceId=si.Id),0) ReturnedScaled FROM SalesInvoices si WHERE si.Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                    if(invoice==null)return new SalePaymentResult{ErrorCode="not_found",ErrorMessage="الفاتورة غير موجودة."};
                    if(invoice.Status!="posted"||invoice.PaymentMode!="credit"||!invoice.CustomerId.HasValue||await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return new SalePaymentResult{ErrorCode="payment_not_allowed",ErrorMessage="لا تقبل هذه الفاتورة تحصيلًا إضافيًا."};
                    if(amount<=0||amount>invoice.TotalScaled-invoice.ReturnedScaled-invoice.PaidScaled)return new SalePaymentResult{ErrorCode="amount_exceeds_due",ErrorMessage="مبلغ التحصيل أكبر من الرصيد المستحق على الفاتورة."};
                    var customer=await c.QuerySingleOrDefaultAsync<CustomerFinancial>(Cmd("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                    if(customer==null||customer.Balance<amount/10000m)return new SalePaymentResult{ErrorCode="ledger_mismatch",ErrorMessage="رصيد العميل لا يطابق مبلغ التحصيل."};
                    var now=Now();
                    await c.ExecuteAsync(Cmd("INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc) VALUES(@Id,@CustomerId,@UserId,'customer_collection',@Method,@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Id=id,CustomerId=invoice.CustomerId,UserId=userId,Method=command.Method,Amount=amount,Key=command.OperationKey,Hash=hash,Date=command.OccurredOn,command.Notes,Now=now},tx,ct)).ConfigureAwait(false);
                    var paymentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId) VALUES(@CustomerId,@Key,@Hash,'collection',@Amount,@Date,@Notes,@Now,@Reference,@Id)",new{CustomerId=invoice.CustomerId,Key="sale-payment:"+command.OperationKey,Hash=hash,Amount=amount,Date=command.OccurredOn,Notes="تحصيل فاتورة "+invoice.InvoiceNumber,Now=now,Reference=invoice.InvoiceNumber,Id=id},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=Balance-@Amount,UpdatedAt=@Now WHERE Id=@Id AND Balance>=@Amount",new{Amount=amount/10000m,Now=now,Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET PaidScaled=PaidScaled+@Amount WHERE Id=@Id AND PaidScaled+@Amount<=TotalScaled",new{Amount=amount,Id=id},tx,ct)).ConfigureAwait(false);
                    tx.Commit();return new SalePaymentResult{Succeeded=true,PaymentId=paymentId,Invoice=await GetAsync(id,ct).ConfigureAwait(false)};
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return new SalePaymentResult{ErrorCode="database_busy",ErrorMessage="قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه."};}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return new SalePaymentResult{ErrorCode="sales_conflict",ErrorMessage="تعارضت الدفعة مع حركة أخرى."};}
            catch(OverflowException){return new SalePaymentResult{ErrorCode="numeric_overflow",ErrorMessage="قيمة الدفعة تتجاوز الحد المسموح."};}
        }

        private static string PaymentHash(long id,SalePaymentCommand x,long userId)
        {
            using(var sha=SHA256.Create()){var input=id+"|"+userId+"|"+x.Amount+"|"+x.Method+"|"+x.OccurredOn+"|"+(x.Notes??"");return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-","").ToLowerInvariant();}
        }
        private sealed class PaymentInvoice{public long Id{get;set;}public long? CustomerId{get;set;}public string Status{get;set;}public string PaymentMode{get;set;}public long TotalScaled{get;set;}public long PaidScaled{get;set;}public long ReturnedScaled{get;set;}public string InvoiceDate{get;set;}public string InvoiceNumber{get;set;}}
        private sealed class PaymentReplay{public long Id{get;set;}public long SalesInvoiceId{get;set;}public string RequestHash{get;set;}}
        private sealed class CustomerFinancial{public decimal Balance{get;set;}public decimal CreditLimit{get;set;}}
    }
}
