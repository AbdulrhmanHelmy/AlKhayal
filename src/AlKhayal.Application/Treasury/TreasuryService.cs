using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Treasury
{
    public sealed class TreasuryException : Exception
    {
        public string Code { get; }
        public TreasuryException(string code, string message) : base(message) { Code = code; }
    }

    public sealed class TreasuryAccount
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Notes { get; set; }
        public long Version { get; set; }
        public long BalanceScaled { get; set; }
        public string Balance => MoneyPolicy.FormatForUser(BalanceScaled);
    }

    public sealed class TreasuryAccountCommand
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Notes { get; set; }
        public long Version { get; set; }
    }

    public sealed class TreasuryMovementCommand
    {
        public long AccountId { get; set; }
        public string Amount { get; set; }
        public string Notes { get; set; }
        public string OperationKey { get; set; }
    }

    public class TreasuryMovement
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string Type { get; set; }
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
        public string Notes { get; set; }
        public string CreatedUtc { get; set; }
        public long BalanceAfterScaled { get; set; }
        public string BalanceAfter => MoneyPolicy.FormatForUser(BalanceAfterScaled);
        public bool Replayed { get; set; }
    }

    public interface ITreasuryStore
    {
        Task<IReadOnlyList<TreasuryAccount>> ListAsync(CancellationToken ct);
        Task<TreasuryAccount> SaveAsync(long? id, TreasuryAccountCommand command, string normalizedCode, long userId, CancellationToken ct);
        Task DeleteAsync(long id, long version, long userId, CancellationToken ct);
        Task<TreasuryMovement> PostAsync(TreasuryMovementCommand command, string type, long amount, string hash, long userId, CancellationToken ct);
    }

    // These rules are also evaluated against the locked transaction snapshot by the store.
    public static class TreasuryRules
    {
        public static long NextBalance(long balance, long amount, string type, bool allowSalesRefundOverdraft = false)
        {
            if (type == "withdraw" && amount > balance && !allowSalesRefundOverdraft)
                throw new TreasuryException("insufficient_balance", "الرصيد غير كافٍ لإتمام السحب.");
            try { return type == "deposit" ? checked(balance + amount) : checked(balance - amount); }
            catch (OverflowException) { throw new TreasuryException("balance_overflow", "المبلغ يتجاوز الحد المسموح للرصيد."); }
        }
        public static void EnsureEditable(TreasuryAccount account, long version)
        {
            if (account == null) throw new TreasuryException("not_found", "الخزنة أو البنك غير موجود.");
            if (account.Version != version) throw new TreasuryException("version_conflict", "تم تعديل الحساب. حدّث القائمة وحاول مرة أخرى.");
        }
        public static void EnsureDeletable(TreasuryAccount account, bool hasMovements)
        {
            if (hasMovements || account.BalanceScaled != 0)
                throw new TreasuryException("account_in_use", "لا يمكن حذف حساب له حركات مالية، حتى لو كان رصيده صفرًا، حفاظًا على السجل.");
        }
        public static void EnsureTypeChange(TreasuryAccount account, string type, bool hasMovements)
        {
            if (account.Type != type && hasMovements)
                throw new TreasuryException("account_in_use", "لا يمكن تغيير نوع حساب له حركات مالية.");
        }
    }

    public sealed class TreasuryService
    {
        private readonly ITreasuryStore _store;
        public TreasuryService(ITreasuryStore store) { _store = store; }
        public Task<IReadOnlyList<TreasuryAccount>> ListAsync(CancellationToken ct) => _store.ListAsync(ct);
        public Task<TreasuryAccount> SaveAsync(long? id, TreasuryAccountCommand command, long userId, CancellationToken ct)
        {
            if (command == null) throw new TreasuryException("validation", "أدخل بيانات الخزنة أو البنك.");
            command.Code = Text(command.Code, 40, true);
            command.Name = Text(command.Name, 160, true);
            command.Notes = Text(command.Notes, 1000, false);
            if (command.Type != "cash" && command.Type != "bank") throw new TreasuryException("validation", "اختر خزنة أو بنك.");
            if (id.HasValue && (id.Value <= 0 || command.Version <= 0)) throw new TreasuryException("validation", "بيانات التعديل غير صالحة.");
            return _store.SaveAsync(id, command, command.Code.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), userId, ct);
        }
        public Task DeleteAsync(long id, long version, long userId, CancellationToken ct)
        {
            if (id <= 0 || version <= 0) throw new TreasuryException("validation", "بيانات الحذف غير صالحة.");
            return _store.DeleteAsync(id, version, userId, ct);
        }
        public Task<TreasuryMovement> PostAsync(TreasuryMovementCommand command, string type, long userId, CancellationToken ct)
        {
            if (command == null || command.AccountId <= 0 || (type != "deposit" && type != "withdraw"))
                throw new TreasuryException("validation", "اختر خزنة أو بنك لإتمام العملية.");
            long amount;
            if (!MoneyPolicy.TryParseUserAmount(command.Amount, false, out amount) || amount <= 0)
                throw new TreasuryException("validation", "أدخل مبلغًا أكبر من صفر بدقة لا تتجاوز خانتين عشريتين.");
            command.Notes = Text(command.Notes, 1000, false);
            command.OperationKey = Text(command.OperationKey, 100, true);
            string hash;
            // Length prefixes prevent delimiter ambiguity; canonical scaled money makes equivalent amounts replay safely.
            var fields = new[] { command.AccountId.ToString(CultureInfo.InvariantCulture), type, amount.ToString(CultureInfo.InvariantCulture), command.Notes, userId.ToString(CultureInfo.InvariantCulture) };
            var canonical = new StringBuilder("treasury:v1:");
            foreach (var field in fields) canonical.Append(field.Length).Append(':').Append(field);
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
            return _store.PostAsync(command, type, amount, hash, userId, ct);
        }
        private static string Text(string value, int max, bool required)
        {
            value = (value ?? "").Trim();
            if ((required && value.Length == 0) || value.Length > max)
                throw new TreasuryException("validation", "أكمل الحقول المطلوبة والتزم بالطول المسموح.");
            return value;
        }
    }
}
