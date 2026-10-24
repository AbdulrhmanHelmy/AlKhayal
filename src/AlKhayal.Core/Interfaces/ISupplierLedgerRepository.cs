using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ISupplierLedgerRepository
    {
        Task<SupplierStatement> StatementAsync(long supplierId, string from, string to, CancellationToken cancellationToken);
        Task<IReadOnlyList<SupplierLedgerEntry>> HistoryAsync(long supplierId, CancellationToken cancellationToken);
        Task<SupplierLedgerEntry> AddPaymentAsync(SupplierLedgerPaymentCommand command, CancellationToken cancellationToken);
    }
}
