using System;
using System.Data.SQLite;
using System.IO;
using AlKhayal.Application.Customers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    // QA Batch 4 (phase 3) focused edge coverage. Customer create/edit validation,
    // duplicate-code conflict behavior, and the idempotent collection transaction /
    // statement reconciliation invariant had no direct test anywhere in the suite
    // (see docs/qa/phase-3-coverage.md). This single test exercises exactly that gap
    // against a disposable explicit SQLite path and never touches the operator's
    // LocalApplicationData database.
    public sealed class QaPhase3EdgeTests
    {
        [Fact]
        public void Customer_validation_duplicate_code_and_idempotent_collections_preserve_balances_and_statement()
        {
            var path = Path.Combine(Path.GetTempPath(), "qa-phase3-edge-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var connectionString = Db.ConnectionStringFor(path);
                Migrator.Run(connectionString);
                var repository = new SqliteCustomerRepository(connectionString);
                var customers = new CustomerService(repository);
                var accounts = new CustomerAccountService(new SqliteCustomerAccountRepository(connectionString));

                // Validation must reject the payload before any row is written:
                // missing name/phone, negative money, and sub-cent precision.
                Assert.Throws<ArgumentException>(() => customers.Create(new Customer { Code = "QA3-V", Name = " ", Phone = "01000000000" }));
                Assert.Throws<ArgumentException>(() => customers.Create(new Customer { Code = "QA3-V", Name = "عميل", Phone = " " }));
                Assert.Throws<ArgumentException>(() => customers.Create(new Customer { Code = "QA3-V", Name = "عميل", Phone = "01000000000", Balance = -1 }));
                Assert.Throws<ArgumentException>(() => customers.Create(new Customer { Code = "QA3-V", Name = "عميل", Phone = "01000000000", CreditLimit = 1.005m }));
                Assert.Throws<ArgumentException>(() => customers.Update(999, new Customer { Code = "QA3-V", Name = "عميل", Phone = "01000000000", Balance = -1 }));
                using (var check = new SQLiteConnection(connectionString))
                {
                    check.Open();
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM Customers"));
                }

                var created = customers.Create(new Customer { Code = "QA3-1", Name = "عميل مرحلة ثلاثة", Phone = "01000000001", Balance = 100.50m, CreditLimit = 500m, Status = "نشط" });
                Assert.True(created.Id > 0);
                var id = created.Id;

                // Codes are unique case-insensitively; the constraint SQLiteException is
                // what the API maps to 409 customer_conflict.
                Assert.Throws<SQLiteException>(() => repository.Add(new Customer { Code = "qa3-1", Name = "مرفوض", Phone = "01000000002" }));
                using (var check = new SQLiteConnection(connectionString))
                {
                    check.Open();
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM Customers"));
                }

                // Edit rewrites descriptive fields only; balance/credit-limit payloads are
                // ignored by the update so the ledger-opening value can never be corrupted.
                var model = repository.GetById(id);
                model.Name = "عميل مرحلة ثلاثة بعد التعديل";
                model.Balance = 0.01m;
                Assert.True(repository.Update(model));
                var reopened = new SqliteCustomerRepository(connectionString);
                Assert.Equal("عميل مرحلة ثلاثة بعد التعديل", reopened.GetById(id).Name);
                Assert.Equal(100.50m, reopened.GetById(id).Balance);
                Assert.Equal(500m, reopened.GetById(id).CreditLimit);

                // An identical collection retry replays (no duplicate row, balance unchanged).
                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var first = accounts.PostTransaction(id, "collection", 0.25m, occurredOn, "تحصيل", "qa3-coll-1");
                Assert.False(first.Replayed);
                var retry = accounts.PostTransaction(id, "collection", 0.25m, occurredOn, "تحصيل", "qa3-coll-1");
                Assert.True(retry.Replayed);
                Assert.Equal(first.Id, retry.Id);
                Assert.Equal(100.25m, reopened.GetById(id).Balance);

                using (var check = new SQLiteConnection(connectionString))
                {
                    check.Open();
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='collection'", new { Id = id }));
                    Assert.Equal(2500L, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='collection'", new { Id = id }));
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType NOT IN ('opening','collection')", new { Id = id }));
                }

                // The statement closes at exactly the persisted balance and lists the
                // collection as a credit movement.
                var statement = accounts.GetStatement(id, occurredOn, occurredOn);
                Assert.Equal(0.25m, statement.TotalCredit);
                Assert.Equal(100.25m, statement.ClosingBalance);
                Assert.Equal(reopened.GetById(id).Balance, statement.ClosingBalance);
                Assert.Contains(statement.Movements, m => m.Kind == "collection" && m.Credit == 0.25m);

                // A customer with a non-opening ledger row can no longer be deleted.
                Assert.False(repository.Delete(id));
                Assert.NotNull(reopened.GetById(id));
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
