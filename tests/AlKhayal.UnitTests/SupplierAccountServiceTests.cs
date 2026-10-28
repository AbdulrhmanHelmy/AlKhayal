using System;
using System.Collections.Generic;
using AlKhayal.Application.Suppliers;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class SupplierAccountServiceTests
    {
        [Fact]
        public void Invoice_total_requires_two_decimals_and_dates_are_normalized()
        {
            var repo = new FakeAccounts(); var service = new SupplierAccountService(repo);
            service.AddInvoice(1, new SupplierInvoice { InvoiceNumber = " A-1 ", InvoiceDate = "2026-10-20", DueDate = "2026-11-20", Total = 100.01m });
            Assert.Equal(100.01m, repo.LastInvoiceTotal);
            Assert.Equal("A-1", repo.LastInvoiceNumber);
            Assert.Equal(1, repo.InvoiceCalls);
        }

        [Fact]
        public void Invalid_invoices_never_reach_the_repository()
        {
            var repo = new FakeAccounts(); var service = new SupplierAccountService(repo);
            Assert.Throws<ArgumentException>(() => service.AddInvoice(1, new SupplierInvoice { InvoiceDate = "2026-10-20", Total = 0 }));
            Assert.Throws<ArgumentException>(() => service.AddInvoice(1, new SupplierInvoice { InvoiceDate = "2026-10-20", Total = 0.004m }));
            Assert.Throws<ArgumentException>(() => service.AddInvoice(1, new SupplierInvoice { InvoiceDate = "not-a-date", Total = 10 }));
            Assert.Throws<ArgumentException>(() => service.AddInvoice(1, new SupplierInvoice { InvoiceDate = "2026-10-20", DueDate = "2026-10-01", Total = 10 }));
            Assert.Throws<ArgumentException>(() => service.AddInvoice(0, new SupplierInvoice { InvoiceDate = "2026-10-20", Total = 10 }));
            Assert.Equal(0, repo.InvoiceCalls);
        }

        [Fact]
        public void Invalid_payments_never_reach_the_repository()
        {
            var repo = new FakeAccounts(); var service = new SupplierAccountService(repo);
            Assert.Throws<ArgumentException>(() => service.AddPayment(1, new SupplierPayment { Amount = 0, PaymentDate = "2026-10-20", Method = "نقدي", Account = "الخزنة الرئيسية" }));
            Assert.Throws<ArgumentException>(() => service.AddPayment(1, new SupplierPayment { Amount = 5, PaymentDate = "2026-10-20", Method = " ", Account = "الخزنة الرئيسية" }));
            Assert.Throws<ArgumentException>(() => service.AddPayment(1, new SupplierPayment { Amount = 5, PaymentDate = "2026-10-20", Method = "نقدي", Account = "" }));
            Assert.Throws<ArgumentException>(() => service.AddPayment(1, new SupplierPayment { Amount = 5, PaymentDate = "", Method = "نقدي", Account = "الخزنة الرئيسية" }));
            Assert.Throws<ArgumentException>(() => service.AddPayment(1, new SupplierPayment { InvoiceId = 0, Amount = 5, PaymentDate = "2026-10-20", Method = "نقدي", Account = "الخزنة الرئيسية" }));
            Assert.Equal(0, repo.PaymentCalls);
        }

        [Fact]
        public void Valid_payment_is_forwarded_and_excess_precision_is_rejected()
        {
            var repo = new FakeAccounts(); var service = new SupplierAccountService(repo);
            Assert.Throws<ArgumentException>(()=>service.AddPayment(3, new SupplierPayment { InvoiceId = 9, Amount = 12.345m, PaymentDate = "2026-10-20", Method = " نقدي ", Account = " الخزنة الرئيسية " }));
            service.AddPayment(3, new SupplierPayment { InvoiceId = 9, Amount = 12.35m, PaymentDate = "2026-10-20", Method = " نقدي ", Account = " الخزنة الرئيسية " });
            Assert.Equal(1, repo.PaymentCalls); Assert.Equal(9L, repo.LastInvoiceId); Assert.Equal(12.35m, repo.LastPaymentAmount); Assert.Equal("نقدي", repo.LastMethod);
        }

        [Fact]
        public void Statement_rejects_reversed_or_malformed_periods()
        {
            var service = new SupplierAccountService(new FakeAccounts());
            Assert.Throws<ArgumentException>(() => service.GetStatement(1, "2026-10-20", "2026-10-01"));
            Assert.Throws<ArgumentException>(() => service.GetStatement(1, "20-10-2026", null));
        }

        [Fact]
        public void Supplier_requires_code_name_and_phone_and_rejects_negative_opening_balance()
        {
            var service = new SupplierService(new FakeSuppliers());
            Assert.Throws<ArgumentException>(() => service.Create(new Supplier { Code = "", Name = "x", Phone = "1" }));
            Assert.Throws<ArgumentException>(() => service.Create(new Supplier { Code = "S", Name = " ", Phone = "1" }));
            Assert.Throws<ArgumentException>(() => service.Create(new Supplier { Code = "S", Name = "x", Phone = "" }));
            Assert.Throws<ArgumentException>(() => service.Create(new Supplier { Code = "S", Name = "x", Phone = "1", Payable = -1 }));
            Assert.Throws<ArgumentException>(()=>service.Create(new Supplier { Code = "S-X", Name = "مورد", Phone = "010", Payable = 10.555m }));
            var created = service.Create(new Supplier { Code = " S-1 ", Name = " مورد ", Phone = " 010 ", Payable = 10.56m });
            Assert.Equal("S-1", created.Code); Assert.Equal("مورد", created.Name); Assert.Equal(10.56m, created.Payable); Assert.Equal("نشط", created.Status);
        }

        private sealed class FakeAccounts : ISupplierAccountRepository
        {
            public int InvoiceCalls, PaymentCalls; public decimal LastInvoiceTotal, LastPaymentAmount; public string LastInvoiceNumber, LastMethod; public long? LastInvoiceId;
            public SupplierInvoice AddInvoice(long supplierId, string invoiceNumber, string invoiceDate, string dueDate, decimal total, string notes, string operationKey = null, string requestHash = null, long? actorUserId = null) { InvoiceCalls++; LastInvoiceTotal = total; LastInvoiceNumber = invoiceNumber; return new SupplierInvoice { SupplierId = supplierId, Total = total }; }
            public SupplierPayment AddPayment(long supplierId, long? invoiceId, decimal amount, string paymentDate, string method, string account, string notes) { PaymentCalls++; LastInvoiceId = invoiceId; LastPaymentAmount = amount; LastMethod = method; return new SupplierPayment { SupplierId = supplierId, Amount = amount }; }
            public List<SupplierInvoice> GetInvoices(long supplierId) { return new List<SupplierInvoice>(); }
            public List<SupplierPayment> GetPayments(long supplierId) { return new List<SupplierPayment>(); }
            public SupplierStatement GetStatement(long supplierId, string from, string to) { return new SupplierStatement { SupplierId = supplierId }; }
        }

        private sealed class FakeSuppliers : ISupplierRepository
        {
            private readonly Dictionary<long, Supplier> _rows = new Dictionary<long, Supplier>();
            public List<Supplier> GetAll() { return new List<Supplier>(_rows.Values); }
            public System.Threading.Tasks.Task<SupplierPage> ListAsync(string search, int page, int pageSize, System.Threading.CancellationToken ct) { return System.Threading.Tasks.Task.FromResult(new SupplierPage { Items = GetAll(), TotalCount = _rows.Count, Page = page, PageSize = pageSize }); }
            public Supplier GetById(long id) { Supplier s; return _rows.TryGetValue(id, out s) ? s : null; }
            public long Add(Supplier supplier) { var id = _rows.Count + 1; supplier.Id = id; _rows[id] = supplier; return id; }
            public bool Update(Supplier supplier) { return _rows.ContainsKey(supplier.Id); }
            public bool Delete(long id) { return _rows.Remove(id); }
        }
    }
}
