using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlKhayal.Application.Finance;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;

namespace AlKhayal.Application.Maintenance
{
    public sealed class MaintenanceTicketCommand
    {
        public long CustomerId { get; set; }
        public string Phone { get; set; }
        public string Device { get; set; }
        public string Model { get; set; }
        public string Serial { get; set; }
        public string Accessories { get; set; }
        public string Issue { get; set; }
        public string Technician { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal AdvancePaid { get; set; }
        public string Notes { get; set; }
        public string ReceivedOn { get; set; }
        public string ExpectedOn { get; set; }
        public string OperationKey { get; set; }
    }

    public sealed class MaintenanceService
    {
        public static readonly string[] Statuses = { "تم الاستلام", "قيد الفحص", "قيد الإصلاح", "بانتظار قطعة", "جاهز للتسليم", "تم التسليم" };
        private readonly IMaintenanceRepository _repository;
        public MaintenanceService(IMaintenanceRepository repository) { _repository = repository; }

        public MaintenanceTicket Create(MaintenanceTicketCommand command)
        {
            if (command == null) throw new ArgumentException("بيانات الصيانة مطلوبة.");
            if (command.CustomerId <= 0) throw new ArgumentException("اختر العميل.");
            var device = Required(command.Device, "نوع الجهاز", 100); var model = Required(command.Model, "الموديل", 120);
            var issue = Required(command.Issue, "وصف العطل", 1000); var phone = Required(command.Phone, "الهاتف", 30);
            if (command.Serial != null && command.Serial.Length > 100) throw new ArgumentException("الرقم التسلسلي أطول من المسموح.");
            if (command.Accessories != null && command.Accessories.Length > 500) throw new ArgumentException("الملحقات أطول من المسموح.");
            if (command.Technician != null && command.Technician.Length > 120) throw new ArgumentException("اسم الفني أطول من المسموح.");
            if (command.Notes != null && command.Notes.Length > 1000) throw new ArgumentException("الملاحظات أطول من المسموح.");
            if (command.EstimatedCost < 0 || !MoneyPolicy.HasAtMostTwoDecimals(command.EstimatedCost)) throw new ArgumentException("التكلفة التقديرية غير صالحة.");
            if (command.AdvancePaid < 0 || !MoneyPolicy.HasAtMostTwoDecimals(command.AdvancePaid)) throw new ArgumentException("المقدم غير صالح.");
            var received = Date(command.ReceivedOn, "تاريخ الاستلام"); var expected = Date(command.ExpectedOn, "موعد التسليم المتوقع");
            var key = Key(command.OperationKey);
            var ticket = new MaintenanceTicket { CustomerId=command.CustomerId, Phone=phone, Device=device, Model=model, Serial=(command.Serial??"").Trim(), Accessories=(command.Accessories??"").Trim(), Issue=issue, Technician=(command.Technician??"").Trim(), EstimatedCost=command.EstimatedCost, AdvancePaid=command.AdvancePaid, Notes=(command.Notes??"").Trim(), ReceivedOn=received, ExpectedOn=expected, Status="تم الاستلام" };
            var canonical = string.Join("|", ticket.CustomerId,ticket.Phone,ticket.Device,ticket.Model,ticket.Serial,ticket.Accessories,ticket.Issue,ticket.Technician,F(ticket.EstimatedCost),F(ticket.AdvancePaid),ticket.Notes,ticket.ReceivedOn,ticket.ExpectedOn);
            return _repository.Create(ticket,key,Hash(canonical));
        }

        public List<MaintenanceTicket> List(long? customerId)
        {
            if (customerId.HasValue && customerId.Value <= 0) throw new ArgumentException("معرف العميل غير صالح.");
            return _repository.List(customerId);
        }

        public MaintenanceTicket UpdateStatus(long ticketId, string status, string operationKey)
        {
            if(ticketId<=0)throw new ArgumentException("رقم تذكرة الصيانة غير صالح.");
            if(Array.IndexOf(Statuses,status)<0||status=="تم التسليم")throw new ArgumentException("حالة الصيانة غير صالحة؛ يتم التسليم من إجراء التسليم.");
            return _repository.UpdateStatus(ticketId,status,Key(operationKey));
        }

        public MaintenanceTicket Deliver(long ticketId, decimal finalCost, string settlementType, string deliveredOn, string operationKey)
        {
            if(ticketId<=0)throw new ArgumentException("رقم تذكرة الصيانة غير صالح.");
            if(finalCost<0||!MoneyPolicy.HasAtMostTwoDecimals(finalCost))throw new ArgumentException("التكلفة النهائية غير صالحة.");
            if(settlementType!="cash"&&settlementType!="installment")throw new ArgumentException("اختر السداد النقدي أو الآجل.");
            var date=Date(deliveredOn,"تاريخ التسليم");var key=Key(operationKey);
            return _repository.Deliver(ticketId,finalCost,settlementType,date,key,Hash(string.Join("|",ticketId,F(finalCost),settlementType,date)));
        }

        private static string Required(string value,string label,int max){var text=(value??"").Trim();if(text.Length==0||text.Length>max)throw new ArgumentException("أدخل "+label+" بطول صحيح.");return text;}
        private static string Date(string value,string label){DateTime date;if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))throw new ArgumentException(label+" غير صحيح.");return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        private static string Key(string key){if(string.IsNullOrWhiteSpace(key)||key.Trim().Length>120)throw new ArgumentException("مفتاح العملية مطلوب.");return key.Trim();}
        private static string F(decimal value){return value.ToString("0.00",CultureInfo.InvariantCulture);}
        private static string Hash(string value){using(var sha=SHA256.Create()){var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(value));var result=new StringBuilder();foreach(var b in bytes)result.Append(b.ToString("x2",CultureInfo.InvariantCulture));return result.ToString();}}
    }
}
