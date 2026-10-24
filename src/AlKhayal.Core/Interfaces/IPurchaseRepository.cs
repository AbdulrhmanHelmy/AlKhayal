using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface IPurchaseRepository
    {
        Task<PurchaseInvoice> CreateDraftAsync(PurchaseDraftCommand command, long totalScaled, long userId, CancellationToken cancellationToken);
        Task<PurchaseInvoice> UpdateDraftAsync(long id, PurchaseDraftCommand command, long totalScaled, long userId, CancellationToken cancellationToken);
        Task<PurchaseInvoice> GetAsync(long id, CancellationToken cancellationToken);
        Task<IReadOnlyList<PurchaseInvoice>> ListAsync(long? supplierId, string from, string to, CancellationToken cancellationToken);
        Task<PurchasePostResult> PostAsync(long id, long expectedVersion, string operationKey, string requestHash, long userId, CancellationToken cancellationToken);
        Task<IReadOnlyList<PurchaseReturnLine>> ReturnableLinesAsync(long id, long warehouseId, CancellationToken cancellationToken);
        Task<PurchaseReturn> PostReturnAsync(long id, PurchaseReturnCommand command, string operationKey, string requestHash, long userId, CancellationToken cancellationToken);
        Task<IReadOnlyList<PurchaseReturn>> ReturnsAsync(long id, CancellationToken cancellationToken);
    }
}
