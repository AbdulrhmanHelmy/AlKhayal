using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Expenses
{
    public sealed class ExpenseException : Exception
    {
        public string Code { get; }
        public ExpenseException(string code, string message) : base(message) { Code = code; }
    }
    public sealed class ExpenseCategory
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }
    public sealed class ExpenseCategoryCommand { public string Name { get; set; } }
    public sealed class ExpenseCommand
    {
        public long CategoryId { get; set; }
        public long TreasuryAccountId { get; set; }
        public string Amount { get; set; }
        public string ExpenseDate { get; set; }
        public string Payee { get; set; }
        public string Notes { get; set; }
        public string OperationKey { get; set; }
    }
    public sealed class ExpenseVoucher
    {
        public long Id { get; set; }
        public string Number => "EXP-" + Id.ToString("D6", CultureInfo.InvariantCulture);
        public long CategoryId { get; set; }
        public string CategoryName { get; set; }
        public long TreasuryAccountId { get; set; }
        public string AccountName { get; set; }
        public long TreasuryMovementId { get; set; }
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
        public string ExpenseDate { get; set; }
        public string Payee { get; set; }
        public string Notes { get; set; }
        public string CreatedUtc { get; set; }
        public long UserId { get; set; }
        public bool Replayed { get; set; }
    }
    public interface IExpenseStore
    {
        Task<IReadOnlyList<ExpenseCategory>> CategoriesAsync(CancellationToken ct);
        Task<ExpenseCategory> AddCategoryAsync(string name, string normalizedName, long userId, CancellationToken ct);
        Task<IReadOnlyList<ExpenseVoucher>> VouchersAsync(CancellationToken ct);
        Task<ExpenseVoucher> PostAsync(ExpenseCommand command, long amount, string hash, long userId, CancellationToken ct);
    }
    public sealed class ExpenseService
    {
        private readonly IExpenseStore _store;
        public ExpenseService(IExpenseStore store) { _store = store; }
        public Task<IReadOnlyList<ExpenseCategory>> CategoriesAsync(CancellationToken ct) => _store.CategoriesAsync(ct);
        public Task<IReadOnlyList<ExpenseVoucher>> VouchersAsync(CancellationToken ct) => _store.VouchersAsync(ct);
        public Task<ExpenseCategory> AddCategoryAsync(ExpenseCategoryCommand command, long userId, CancellationToken ct)
        {
            var name = Text(command?.Name, 160, true);
            return _store.AddCategoryAsync(name, name.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), userId, ct);
        }
        public Task<ExpenseVoucher> PostAsync(ExpenseCommand command, long userId, CancellationToken ct)
        {
            if (command == null || command.CategoryId <= 0 || command.TreasuryAccountId <= 0)
                throw new ExpenseException("validation", "اختر نوع المصروف والخزنة أو البنك.");
            long amount;
            if (!MoneyPolicy.TryParseUserAmount(command.Amount, false, out amount) || amount <= 0)
                throw new ExpenseException("validation", "أدخل مبلغًا أكبر من صفر بدقة خانتين عشريتين بحد أقصى.");
            DateTime date;
            if (!DateTime.TryParseExact(command.ExpenseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                throw new ExpenseException("validation", "أدخل تاريخًا صحيحًا للمصروف.");
            command.Payee = Text(command.Payee, 200, false);
            command.Notes = Text(command.Notes, 1000, false);
            command.OperationKey = Text(command.OperationKey, 100, true);
            var canonical = new StringBuilder("expense:v1:");
            foreach (var field in new[] { command.CategoryId.ToString(CultureInfo.InvariantCulture), command.TreasuryAccountId.ToString(CultureInfo.InvariantCulture), amount.ToString(CultureInfo.InvariantCulture), command.ExpenseDate, command.Payee, command.Notes, userId.ToString(CultureInfo.InvariantCulture) })
                canonical.Append(field.Length).Append(':').Append(field);
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
            return _store.PostAsync(command, amount, hash, userId, ct);
        }
        private static string Text(string value, int max, bool required)
        {
            value = (value ?? "").Trim();
            if ((required && value.Length == 0) || value.Length > max)
                throw new ExpenseException("validation", "أكمل الحقول المطلوبة والتزم بالطول المسموح.");
            return value;
        }
    }
}
