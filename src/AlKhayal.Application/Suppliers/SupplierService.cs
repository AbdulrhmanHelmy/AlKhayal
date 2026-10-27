using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Suppliers
{
    public sealed class SupplierService
    {
        private readonly ISupplierRepository _suppliers;
        public SupplierService(ISupplierRepository suppliers) { _suppliers = suppliers; }

        public List<Supplier> GetAll() { return _suppliers.GetAll(); }
        public Task<SupplierPage> ListAsync(string search, int page, int pageSize, CancellationToken ct)
        { return _suppliers.ListAsync((search ?? string.Empty).Trim(), System.Math.Max(1, page), System.Math.Min(100, System.Math.Max(1, pageSize)), ct); }
        public Supplier GetById(long id) { return _suppliers.GetById(id); }

        public Supplier Create(Supplier supplier)
        {
            Validate(supplier);
            if (supplier.Payable < 0) throw new ArgumentException("الرصيد المستحق لا يمكن أن يكون سالبًا.");
            if (!MoneyPolicy.HasAtMostTwoDecimals(supplier.Payable)) throw new ArgumentException("الرصيد المستحق يجب ألا يتجاوز خانتين عشريتين.");
            supplier.Id = _suppliers.Add(supplier);
            return _suppliers.GetById(supplier.Id);
        }

        public Supplier Update(long id, Supplier supplier)
        {
            Validate(supplier);
            supplier.Id = id;
            return _suppliers.Update(supplier) ? _suppliers.GetById(id) : null;
        }

        public bool Delete(long id) { return _suppliers.Delete(id); }

        private static void Validate(Supplier supplier)
        {
            if (supplier == null) throw new ArgumentException("بيانات المورد مطلوبة.");
            if (string.IsNullOrWhiteSpace(supplier.Code)) throw new ArgumentException("كود المورد مطلوب.");
            if (string.IsNullOrWhiteSpace(supplier.Name)) throw new ArgumentException("اسم المورد مطلوب.");
            if (string.IsNullOrWhiteSpace(supplier.Phone)) throw new ArgumentException("رقم الهاتف مطلوب.");
            supplier.Code = supplier.Code.Trim();
            supplier.Name = supplier.Name.Trim();
            supplier.Phone = supplier.Phone.Trim();
            supplier.Contact = (supplier.Contact ?? string.Empty).Trim();
            supplier.Address = (supplier.Address ?? string.Empty).Trim();
            supplier.Status = string.IsNullOrWhiteSpace(supplier.Status) ? "نشط" : supplier.Status.Trim();
        }
    }
}
