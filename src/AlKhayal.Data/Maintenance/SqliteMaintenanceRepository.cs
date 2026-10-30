using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Maintenance;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Maintenance
{
    public sealed class SqliteMaintenanceRepository : IMaintenanceRepository
    {
        private readonly string _connectionString;
        public SqliteMaintenanceRepository(string connectionString) { _connectionString=connectionString; }

        public MaintenanceTicket Create(MaintenanceTicket ticket,string operationKey,string requestHash)
        {
            using(var c=new SQLiteConnection(_connectionString))
            {
                c.Open();using(var tx=c.BeginTransaction())
                {
                    var replay=c.QueryFirstOrDefault<StoredTicket>("SELECT Id,RequestHash FROM MaintenanceTickets WHERE OperationKey=@operationKey",new{operationKey},tx);
                    if(replay!=null){if(!string.Equals(replay.RequestHash,requestHash,StringComparison.Ordinal))throw new CustomerOperationKeyConflictException("مفتاح العملية مستخدم لطلب صيانة مختلف.");tx.Commit();return Get(c,replay.Id);}
                    if(c.ExecuteScalar<long>("SELECT COUNT(*) FROM Customers WHERE Id=@Id",new{Id=ticket.CustomerId},tx)==0)throw new KeyNotFoundException("العميل غير موجود.");
                    var now=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);var number="MNT-"+DateTime.UtcNow.ToString("yyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8).ToUpperInvariant();
                    var estimated=checked((long)(ticket.EstimatedCost*10000m));var advance=checked((long)(ticket.AdvancePaid*10000m));
                    c.Execute(@"INSERT INTO MaintenanceTickets(CustomerId,TicketNumber,OperationKey,RequestHash,Phone,Device,Model,Serial,Accessories,Issue,Technician,EstimatedCostScaled,AdvancePaidScaled,Notes,ReceivedOn,ExpectedOn,Status,CreatedUtc)
VALUES(@CustomerId,@number,@operationKey,@requestHash,@Phone,@Device,@Model,@Serial,@Accessories,@Issue,@Technician,@estimated,@advance,@Notes,@ReceivedOn,@ExpectedOn,'تم الاستلام',@now)",new{ticket.CustomerId,number,operationKey,requestHash,ticket.Phone,ticket.Device,ticket.Model,ticket.Serial,ticket.Accessories,ticket.Issue,ticket.Technician,estimated,advance,ticket.Notes,ticket.ReceivedOn,ticket.ExpectedOn,now},tx);
                    var id=c.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
                    c.Execute("INSERT INTO MaintenanceTicketStatusHistory(TicketId,Status,OperationKey,ChangedUtc) VALUES(@id,'تم الاستلام',@key,@now)",new{id,key="maintenance:"+id+":status:received",now},tx);
                    if(advance>0)
                    {
                        // An advance prepays this uncharged ticket; it is not a collection of existing customer debt.
                        var receiptKey="maintenance:"+id+":advance";
                        c.Execute("INSERT INTO MaintenanceTicketReceipts(TicketId,ReceiptType,AmountScaled,OperationKey,ReceivedOn,CreatedUtc) VALUES(@id,'advance',@advance,@receiptKey,@date,@now)",new{id,advance,receiptKey,date=ticket.ReceivedOn,now},tx);
                    }
                    tx.Commit();return Get(c,id);
                }
            }
        }

        public List<MaintenanceTicket> List(long? customerId)
        {
            using(var c=new SQLiteConnection(_connectionString))
            {
                var ids=c.Query<long>("SELECT Id FROM MaintenanceTickets WHERE @CustomerId IS NULL OR CustomerId=@CustomerId ORDER BY Id DESC",new{CustomerId=customerId}).ToList();
                return ids.Select(id=>Get(c,id)).ToList();
            }
        }

        public MaintenanceTicket UpdateStatus(long ticketId,string status,string operationKey)
        {
            using(var c=new SQLiteConnection(_connectionString))
            {
                c.Open();using(var tx=c.BeginTransaction())
                {
                    var previous=c.QueryFirstOrDefault<StatusEventRow>("SELECT TicketId,Status FROM MaintenanceTicketStatusHistory WHERE OperationKey=@operationKey",new{operationKey},tx);
                    if(previous!=null){if(previous.TicketId!=ticketId||previous.Status!=status)throw new CustomerOperationKeyConflictException("مفتاح العملية مستخدم لتغيير حالة مختلف.");tx.Commit();return Get(c,ticketId);}
                    var ticket=c.QueryFirstOrDefault<StoredTicket>("SELECT Id,Status FROM MaintenanceTickets WHERE Id=@ticketId",new{ticketId},tx);
                    if(ticket==null)throw new KeyNotFoundException("تذكرة الصيانة غير موجودة.");if(ticket.Status=="تم التسليم")throw new ArgumentException("لا يمكن تغيير حالة تذكرة تم تسليمها.");
                    var now=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
                    c.Execute("UPDATE MaintenanceTickets SET Status=@status WHERE Id=@ticketId AND Status<>'تم التسليم'",new{status,ticketId},tx);
                    c.Execute("INSERT INTO MaintenanceTicketStatusHistory(TicketId,Status,OperationKey,ChangedUtc) VALUES(@ticketId,@status,@operationKey,@now)",new{ticketId,status,operationKey,now},tx);
                    tx.Commit();return Get(c,ticketId);
                }
            }
        }

        public MaintenanceTicket Deliver(long ticketId,decimal finalCost,string settlementType,string deliveredOn,string operationKey,string requestHash)
        {
            using(var c=new SQLiteConnection(_connectionString))
            {
                c.Open();using(var tx=c.BeginTransaction())
                {
                    var ticket=c.QueryFirstOrDefault<TicketRow>("SELECT Id,CustomerId,TicketNumber,Device,Model,AdvancePaidScaled,Status,DeliveryOperationKey,DeliveryRequestHash FROM MaintenanceTickets WHERE Id=@ticketId",new{ticketId},tx);
                    if(ticket==null)throw new KeyNotFoundException("تذكرة الصيانة غير موجودة.");
                    if(ticket.Status=="تم التسليم")
                    {
                        if(ticket.DeliveryOperationKey==operationKey&&ticket.DeliveryRequestHash==requestHash){tx.Commit();return Get(c,ticketId);}
                        throw new CustomerOperationKeyConflictException("تم تسليم هذه التذكرة بمعلومات مختلفة.");
                    }
                    if(ticket.Status!="جاهز للتسليم")throw new ArgumentException("غيّر حالة الجهاز إلى جاهز للتسليم أولًا.");
                    var total=checked((long)(finalCost*10000m));var advance=ticket.AdvancePaidScaled;if(total<advance)throw new ArgumentException("التكلفة النهائية أقل من المقدم المسجل.");
                    var cash=settlementType=="cash"?checked(total-advance):0L;
                    var ledger=c.ExecuteScalar<long>("SELECT COALESCE(SUM(CASE WHEN TransactionType IN ('opening','increase') THEN AmountScaled ELSE -AmountScaled END),0) FROM CustomerTransactions WHERE CustomerId=@CustomerId",new{ticket.CustomerId},tx);
                    var advanceKey="maintenance:"+ticketId+":advance";
                    var appliedAdvance=c.QuerySingleOrDefault<AdvanceLedgerRow>("SELECT CustomerId,TransactionType,AmountScaled FROM CustomerTransactions WHERE OperationKey=@advanceKey",new{advanceKey},tx);
                    if(appliedAdvance!=null&&(appliedAdvance.CustomerId!=ticket.CustomerId||appliedAdvance.TransactionType!="collection"||appliedAdvance.AmountScaled!=advance))throw new ArgumentException("قيد مقدم الصيانة لا يطابق التذكرة؛ لم يتم التسليم.");
                    // Older tickets may already have collected their advance at intake. Never collect it twice.
                    var advanceToApply=appliedAdvance==null?advance:0L;
                    var next=checked(ledger+total-cash-advanceToApply);var now=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);var reference=ticket.TicketNumber;
                    if(advance>0&&c.ExecuteScalar<long>("SELECT COUNT(*) FROM MaintenanceTicketReceipts WHERE TicketId=@ticketId AND ReceiptType='advance' AND AmountScaled=@advance AND OperationKey=@advanceKey",new{ticketId,advance,advanceKey},tx)!=1)throw new ArgumentException("إيصال مقدم الصيانة غير مطابق؛ لم يتم التسليم.");
                    if(total>0)c.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@CustomerId,@key,@requestHash,'increase',@total,@deliveredOn,@notes,@now,@reference)",new{ticket.CustomerId,key="maintenance:"+ticketId+":charge",requestHash,total,deliveredOn,notes="صيانة رقم "+reference+" - "+ticket.Device+" "+ticket.Model,now,reference},tx);
                    if(advanceToApply>0)c.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@CustomerId,@advanceKey,@requestHash,'collection',@advanceToApply,@deliveredOn,@notes,@now,@reference)",new{ticket.CustomerId,advanceKey,requestHash,advanceToApply,deliveredOn,notes="مقدم صيانة رقم "+reference,now,reference},tx);
                    if(cash>0)
                    {
                        c.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@CustomerId,@key,@requestHash,'collection',@cash,@deliveredOn,@notes,@now,@reference)",new{ticket.CustomerId,key="maintenance:"+ticketId+":cash-statement",requestHash,cash,deliveredOn,notes="سداد صيانة نقدي رقم "+reference,now,reference},tx);
                        c.Execute("INSERT INTO MaintenanceTicketReceipts(TicketId,ReceiptType,AmountScaled,OperationKey,ReceivedOn,CreatedUtc) VALUES(@ticketId,'cash_delivery',@cash,@key,@deliveredOn,@now)",new{ticketId,cash,key="maintenance:"+ticketId+":cash",deliveredOn,now},tx);
                    }
                    c.Execute("UPDATE Customers SET Balance=@balance,UpdatedAt=@now WHERE Id=@CustomerId",new{balance=next/10000m,now,ticket.CustomerId},tx);
                    c.Execute("UPDATE MaintenanceTickets SET Status='تم التسليم',FinalCostScaled=@total,SettlementType=@settlementType,DeliveredOn=@deliveredOn,DeliveryOperationKey=@operationKey,DeliveryRequestHash=@requestHash WHERE Id=@ticketId AND Status='جاهز للتسليم'",new{total,settlementType,deliveredOn,operationKey,requestHash,ticketId},tx);
                    c.Execute("INSERT INTO MaintenanceTicketStatusHistory(TicketId,Status,OperationKey,ChangedUtc) VALUES(@ticketId,'تم التسليم',@operationKey,@now)",new{ticketId,operationKey,now},tx);
                    tx.Commit();return Get(c,ticketId);
                }
            }
        }

        private static MaintenanceTicket Get(SQLiteConnection c,long id)
        {
            var row=c.QueryFirstOrDefault<MaintenanceTicketRow>(@"SELECT t.Id,t.CustomerId,c.Name CustomerName,t.TicketNumber,t.Phone,t.Device,t.Model,t.Serial,t.Accessories,t.Issue,t.Technician,t.EstimatedCostScaled,t.AdvancePaidScaled,t.Notes,t.ReceivedOn,t.ExpectedOn,t.Status,t.FinalCostScaled,t.SettlementType,t.DeliveredOn FROM MaintenanceTickets t JOIN Customers c ON c.Id=t.CustomerId WHERE t.Id=@id",new{id});
            if(row==null)return null;
            var result=new MaintenanceTicket{Id=row.Id,CustomerId=row.CustomerId,CustomerName=row.CustomerName,TicketNumber=row.TicketNumber,Phone=row.Phone,Device=row.Device,Model=row.Model,Serial=row.Serial,Accessories=row.Accessories,Issue=row.Issue,Technician=row.Technician,EstimatedCost=row.EstimatedCostScaled/10000m,AdvancePaid=row.AdvancePaidScaled/10000m,Notes=row.Notes,ReceivedOn=row.ReceivedOn,ExpectedOn=row.ExpectedOn,Status=row.Status,FinalCost=row.FinalCostScaled.HasValue?(decimal?)(row.FinalCostScaled.Value/10000m):null,SettlementType=row.SettlementType,DeliveredOn=row.DeliveredOn};
            result.History=c.Query<MaintenanceStatusEvent>("SELECT Status,ChangedUtc FROM MaintenanceTicketStatusHistory WHERE TicketId=@id ORDER BY Id",new{id}).ToList();return result;
        }
        private sealed class AdvanceLedgerRow{public long CustomerId{get;set;}public string TransactionType{get;set;}public long AmountScaled{get;set;}}
        private sealed class StoredTicket{public long Id{get;set;}public string RequestHash{get;set;}public string Status{get;set;}}
        private sealed class StatusEventRow{public long TicketId{get;set;}public string Status{get;set;}}
        private sealed class TicketRow{public long Id{get;set;}public long CustomerId{get;set;}public string TicketNumber{get;set;}public string Device{get;set;}public string Model{get;set;}public long AdvancePaidScaled{get;set;}public string Status{get;set;}public string DeliveryOperationKey{get;set;}public string DeliveryRequestHash{get;set;}}
        private sealed class MaintenanceTicketRow{public long Id{get;set;}public long CustomerId{get;set;}public string CustomerName{get;set;}public string TicketNumber{get;set;}public string Phone{get;set;}public string Device{get;set;}public string Model{get;set;}public string Serial{get;set;}public string Accessories{get;set;}public string Issue{get;set;}public string Technician{get;set;}public long EstimatedCostScaled{get;set;}public long AdvancePaidScaled{get;set;}public string Notes{get;set;}public string ReceivedOn{get;set;}public string ExpectedOn{get;set;}public string Status{get;set;}public long? FinalCostScaled{get;set;}public string SettlementType{get;set;}public string DeliveredOn{get;set;}}
    }
}
