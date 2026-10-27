using System.Collections.Generic;

namespace AlKhayal.Core.Models
{
    public sealed class SupplierPage
    {
        public IReadOnlyList<Supplier> Items { get; set; }
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
