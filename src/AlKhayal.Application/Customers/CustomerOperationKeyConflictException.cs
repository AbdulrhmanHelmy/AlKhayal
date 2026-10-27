using System;

namespace AlKhayal.Application.Customers
{
    // QA-101: a customer transaction operation key was reused with a payload that
    // does not match the stored authoritative ledger row. The repository throws
    // this instead of returning the first customer's transaction as a successful
    // replay; CustomersController translates it to HTTP 409 operation_key_conflict
    // (the repo-wide convention used by sales, purchasing, catalog and inventory).
    // The message carries the shared "مفتاح العملية" marker so repository-level
    // contract tests can match it without inventing new wording.
    public sealed class CustomerOperationKeyConflictException : InvalidOperationException
    {
        public CustomerOperationKeyConflictException(string message) : base(message) { }
    }
}
