using System;
using System.Collections.Generic;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;

namespace AlKhayal.Application.Customers
{
    public sealed class CustomerService
    {
        private readonly ICustomerRepository _customers;
        public CustomerService(ICustomerRepository customers) { _customers = customers; }

        public List<Customer> GetAll() { return _customers.GetAll(); }
        public Customer GetById(long id) { return _customers.GetById(id); }

        public Customer Create(Customer customer)
        {
            Validate(customer);
            customer.Id = _customers.Add(customer);
            return customer;
        }

        public Customer Update(long id, Customer customer)
        {
            Validate(customer);
            customer.Id = id;
            return _customers.Update(customer) ? customer : null;
        }

        public bool Delete(long id) { return _customers.Delete(id); }

        private static void Validate(Customer customer)
        {
            if (customer == null) throw new ArgumentException("بيانات العميل مطلوبة.");
            if (string.IsNullOrWhiteSpace(customer.Code)) throw new ArgumentException("كود العميل مطلوب.");
            if (string.IsNullOrWhiteSpace(customer.Name)) throw new ArgumentException("اسم العميل مطلوب.");
            if (string.IsNullOrWhiteSpace(customer.Phone)) throw new ArgumentException("رقم الهاتف مطلوب.");
            if (customer.Balance < 0 || customer.CreditLimit < 0) throw new ArgumentException("الرصيد وحد الائتمان لا يمكن أن يكونا سالبين.");
            customer.Code = customer.Code.Trim();
            customer.Name = customer.Name.Trim();
            customer.Phone = customer.Phone.Trim();
            customer.Address = (customer.Address ?? string.Empty).Trim();
            customer.Subscription = string.IsNullOrWhiteSpace(customer.Subscription) ? "أساسي" : customer.Subscription.Trim();
            customer.Status = string.IsNullOrWhiteSpace(customer.Status) ? "نشط" : customer.Status.Trim();
        }
    }
}
