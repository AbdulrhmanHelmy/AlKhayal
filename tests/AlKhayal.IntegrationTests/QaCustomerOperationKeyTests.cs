using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Customers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using AlKhayal.Server.Host;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    // QA Batch 4A — financial-safety regression for the customer ledger operation
    // key (QA-101, docs/qa/scenario-manifest.json). Codex reviewed
    // SqliteCustomerAccountRepository.PostTransaction: the replay lookup selects
    // any CustomerTransactions row by OperationKey alone and returns it as
    // Replayed=true without verifying customerId/transaction type/amount/date/notes
    // against the incoming request (no RequestHash comparison either). These tests
    // pin the required contract BEFORE any production fix:
    //   - an identical retry replays the same transaction id (one ledger row,
    //     exact balance);
    //   - a reuse of the same operation key with a changed amount, changed type,
    //     changed date, changed notes or a different customer must be rejected as
    //     an operation-key conflict. The intended API contract is HTTP 409 with
    //     error code operation_key_conflict (the code every other posting store in
    //     this repo already implements: sales, purchasing, catalog, inventory);
    //     until the customers API surfaces that code, the repository-level
    //     equivalent is a rejection whose message matches the repo-wide operation-key
    //     conflict convention (the "مفتاح العملية" marker already used verbatim by
    //     the supplier, purchasing, sales and inventory stores). No specific Arabic
    //     sentence is invented here — only that shared marker is asserted.
    // Each mismatched-payload case runs in a fresh GUID temp database so every
    // mismatch is independently exercised and every post-conflict database state
    // (both opening rows, the single collection row, unchanged balances) is
    // verified after the conflict.
    // No production code is changed in this batch; while the behavior is absent
    // the conflict assertions fail and report the exact replayed result.
    public sealed class QaCustomerOperationKeyTests
    {
        private const string ConflictKey = "qa-b4a-op-key-1";
        private const string CookieName = "alkhayal_session";
        private const string ExpectedConflictMarker = "مفتاح العملية";
        private const string ExpectedConflictCode = "operation_key_conflict"; // API code, HTTP 409

        [Fact]
        public void Customer_operation_key_replays_identical_retry_exactly_once()
        {
            using (var fixture = QaFixture.Create())
            {
                var customers = fixture.CreateCustomerService();
                var accounts = fixture.CreateAccountService();

                var customerA = customers.Create(new Customer { Code = "QA-B4A", Name = "عميل أ", Phone = "01000000001", Balance = 100.00m, CreditLimit = 500m, Status = "نشط" });
                var customerB = customers.Create(new Customer { Code = "QA-B4B", Name = "عميل ب", Phone = "01000000002", Balance = 50.00m, CreditLimit = 500m, Status = "نشط" });
                Assert.True(customerA.Id > 0);
                Assert.True(customerB.Id > 0);

                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // Initial collection for customer A under key K: a fresh transaction.
                var first = accounts.PostTransaction(customerA.Id, "collection", 25.00m, occurredOn, "تحصيل", ConflictKey);
                Assert.False(first.Replayed);
                Assert.Equal(customerA.Id, first.CustomerId);
                Assert.Equal("collection", first.Type);
                Assert.Equal(25.00m, first.Amount);
                fixture.SnapshotAfterInitial(occurredOn);

                // Identical retry: exact replay — same transaction id, still exactly
                // one ledger row for the key, balance exactly 75.00.
                var retry = accounts.PostTransaction(customerA.Id, "collection", 25.00m, occurredOn, "تحصيل", ConflictKey);
                Assert.True(retry.Replayed);
                Assert.Equal(first.Id, retry.Id);
                Assert.Equal(75.00m, repository(fixture).GetById(customerA.Id).Balance);
                Assert.Equal(1L, RowsFor(fixture, ConflictKey));
                fixture.SnapshotAfterInitial(occurredOn);

                // Notes equality normalizes null→empty on both sides without ever
                // trimming: storing a posting with null notes and retrying it with
                // an empty string is an identical retry (the row was stored as ""),
                // while padded whitespace is different content.
                var nullPost = accounts.PostTransaction(customerA.Id, "collection", 5.00m, occurredOn, null, ConflictKey + "-null");
                Assert.False(nullPost.Replayed);
                var emptyRetry = accounts.PostTransaction(customerA.Id, "collection", 5.00m, occurredOn, string.Empty, ConflictKey + "-null");
                Assert.True(emptyRetry.Replayed);
                Assert.Equal(nullPost.Id, emptyRetry.Id);
                Assert.Equal(70.00m, repository(fixture).GetById(customerA.Id).Balance);
                Assert.Equal(1L, RowsFor(fixture, ConflictKey + "-null"));
                ExpectConflict(RunAttempt(accounts, customerA.Id, "collection", 25.00m, occurredOn, " تحصيل ", ConflictKey));
                // SnapshotAfterInitial's exact total (3 rows) predates these
                // additions, so the state is re-verified with targeted counts only:
                // the padded attempt threw before any commit, both keys still hold
                // exactly one row, and the balance is unchanged.
                Assert.Equal(70.00m, repository(fixture).GetById(customerA.Id).Balance);
                Assert.Equal(1L, RowsFor(fixture, ConflictKey));
                Assert.Equal(1L, RowsFor(fixture, ConflictKey + "-null"));
            }
        }

        public static IEnumerable<object[]> MismatchedPayloadCases()
        {
            yield return new object[] { "changed_amount", false, "collection", 50.00m, null, "تحصيل" };
            yield return new object[] { "changed_type", false, "increase", 25.00m, null, "تحصيل" };
            yield return new object[] { "changed_date", false, "collection", 25.00m, "2026-01-01", "تحصيل" };
            yield return new object[] { "changed_notes", false, "collection", 25.00m, null, "تحصيل معدّل" };
            yield return new object[] { "changed_notes_whitespace", false, "collection", 25.00m, null, " تحصيل " };
            yield return new object[] { "other_customer", true, "collection", 25.00m, null, "تحصيل" };
        }

        [Theory]
        [MemberData(nameof(MismatchedPayloadCases))]
        public void Customer_operation_key_reuse_with_mismatched_payload_is_conflicted(string label, bool otherCustomer, string type, decimal amount, string overriddenDate, string notes)
        {
            using (var fixture = QaFixture.Create())
            {
                var customers = fixture.CreateCustomerService();
                var accounts = fixture.CreateAccountService();

                var customerA = customers.Create(new Customer { Code = "QA-B4A", Name = "عميل أ", Phone = "01000000001", Balance = 100.00m, CreditLimit = 500m, Status = "نشط" });
                var customerB = customers.Create(new Customer { Code = "QA-B4B", Name = "عميل ب", Phone = "01000000002", Balance = 50.00m, CreditLimit = 500m, Status = "نشط" });
                Assert.True(customerA.Id > 0);
                Assert.True(customerB.Id > 0);

                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // Initial collection for customer A under key K: a fresh transaction.
                accounts.PostTransaction(customerA.Id, "collection", 25.00m, occurredOn, "تحصيل", ConflictKey);
                fixture.SnapshotAfterInitial(occurredOn);

                // A replay of the same key with a changed amount, changed type,
                // changed date, changed notes or a different customer MUST be an
                // operation-key conflict (HTTP 409 / operation_key_conflict), never
                // a silent replay of the 25.00 operation owned by customer A.
                var target = otherCustomer ? customerB.Id : customerA.Id;
                var mismatch = RunAttempt(accounts, target, type, amount, overriddenDate ?? occurredOn, notes, ConflictKey);
                Assert.False(string.IsNullOrEmpty(label));
                ExpectConflict(mismatch);

                // The attempted conflict leaves the real database state unchanged:
                // both opening rows (A=100.00, B=50.00) plus the one collection row
                // for A, exact balances, and the original row's type/date/amount/
                // owner untouched. Customer B never gains a collection row.
                fixture.SnapshotAfterInitial(occurredOn);
                Assert.Equal(0L, RowsForCustomerAndKey(fixture, customerB.Id, ConflictKey));
                Assert.Equal(1L, RowsForCustomer(fixture, customerB.Id)); // opening row only
                Assert.Equal(1L, RowsForCustomerNonOpening(fixture, customerA.Id)); // only the original collection
                Assert.Equal(0L, RowsForCustomerNonOpening(fixture, customerB.Id)); // zero non-opening rows for B
            }
        }

        [Fact]
        public void Customer_manual_operation_key_never_replays_payment_provenance_rows()
        {
            using (var fixture = QaFixture.Create())
            {
                var customers = fixture.CreateCustomerService();
                var accounts = fixture.CreateAccountService();

                var customerA = customers.Create(new Customer { Code = "QA-PRV", Name = "عميل أ", Phone = "01000000001", Balance = 100.00m, CreditLimit = 500m, Status = "نشط" });
                Assert.True(customerA.Id > 0);

                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // AddPayment writes its own CustomerTransactions row keyed by the
                // payment reference (CP-…). It keeps its original INSERT, which
                // never populates the CustomerTransactions provenance columns —
                // Reference/SalesInvoiceId/SalesReturnId stay NULL and
                // RequestHash equals that same reference — so column inspection
                // alone cannot identify it; ownership is proven by the
                // CustomerPayments row whose Reference equals the operation key.
                // The sales invoice/payment/return paths do populate those
                // columns. A manual PostTransaction whose operation key happens
                // to equal such a key must conflict — an unrelated financial
                // transaction must never replay as a manually posted
                // customer-ledger operation.
                var payment = accounts.AddPayment(customerA.Id, new CustomerPayment { Amount = 10.00m, PaymentDate = occurredOn, Method = "نقدي", Account = "الخزينة", Notes = "تحصيل" });
                Assert.True(payment.Id > 0);
                Assert.False(string.IsNullOrEmpty(payment.Reference));

                // The stored row's payload matches this manual attempt exactly
                // (collection / 10.00 / today / "تحصيل"), so only real ownership
                // metadata can prevent a false replay here.
                var attempt = RunAttempt(accounts, customerA.Id, "collection", 10.00m, occurredOn, "تحصيل", payment.Reference);
                ExpectConflict(attempt);

                // The conflict left the ledger unchanged — verified with scoped
                // assertions, not a bare table-wide row count (the table total
                // depends on unrelated opening rows and pins nothing; the test
                // invariants are about customer A and the payment key). Customer A
                // has exactly one opening row and exactly one collection row (the
                // payment), the payment key still resolves to exactly one ledger
                // row (no false replay inserted a duplicate), no Reference-bearing
                // provenance row exists (this path never writes one and the
                // conflicted attempt wrote nothing), and the balance still
                // reflects only the payment (90.00).
                using (var check = new SQLiteConnection(fixture.ConnectionString))
                {
                    check.Open();
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'", new { Id = customerA.Id }));
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='collection'", new { Id = customerA.Id }));
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE Reference IS NOT NULL"));
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerPayments WHERE Reference=@Ref", new { Ref = payment.Reference }));
                }
                Assert.Equal(1L, RowsFor(fixture, payment.Reference));
                Assert.Equal(90.00m, repository(fixture).GetById(customerA.Id).Balance);
            }
        }

        // QA-101 follow-up: no key prefix is reserved. A manually chosen
        // operation key may legitimately begin with "CP-" or "sale-" (those
        // prefixes exist in other writers' generated keys, but they never decide
        // the replay outcome — only stored ownership metadata does). This
        // regression pins that identical retries of such manual postings still
        // replay identically, while an operation-key collision with an actual
        // legacy customer-payment row is still rejected without a false replay.
        [Fact]
        public void Customer_manual_key_starting_with_cp_or_sale_prefix_replays_and_legacy_payment_row_still_conflicts()
        {
            using (var fixture = QaFixture.Create())
            {
                var customers = fixture.CreateCustomerService();
                var accounts = fixture.CreateAccountService();

                var customerA = customers.Create(new Customer { Code = "QA-CP1", Name = "عميل أ", Phone = "01000000001", Balance = 100.00m, CreditLimit = 500m, Status = "نشط" });
                Assert.True(customerA.Id > 0);

                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");

                // Manual key starting with "CP-": fresh posting, then an identical
                // retry replays the same transaction id with exactly one row.
                const string cpManualKey = "CP-manual-qa101-reg";
                var first = accounts.PostTransaction(customerA.Id, "collection", 7.50m, occurredOn, "تحصيل يدوي", cpManualKey);
                Assert.False(first.Replayed);
                var retry = accounts.PostTransaction(customerA.Id, "collection", 7.50m, occurredOn, "تحصيل يدوي", cpManualKey);
                Assert.True(retry.Replayed);
                Assert.Equal(first.Id, retry.Id);
                Assert.Equal(1L, RowsFor(fixture, cpManualKey));
                Assert.Equal(92.50m, repository(fixture).GetById(customerA.Id).Balance);

                // Manual key starting with "sale-": identical behavior — the
                // writer's own namespace must not reject legitimate manual keys.
                const string saleManualKey = "sale-manual-qa101-reg";
                var saleFirst = accounts.PostTransaction(customerA.Id, "increase", 3.00m, occurredOn, "زيادة يدوية", saleManualKey);
                Assert.False(saleFirst.Replayed);
                var saleRetry = accounts.PostTransaction(customerA.Id, "increase", 3.00m, occurredOn, "زيادة يدوية", saleManualKey);
                Assert.True(saleRetry.Replayed);
                Assert.Equal(saleFirst.Id, saleRetry.Id);
                Assert.Equal(1L, RowsFor(fixture, saleManualKey));
                Assert.Equal(95.50m, repository(fixture).GetById(customerA.Id).Balance);

                // These manual keys never belonged to another financial writer:
                // no CustomerPayments row exists with either reference.
                using (var check = new SQLiteConnection(fixture.ConnectionString))
                {
                    check.Open();
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerPayments WHERE Reference=@Ref", new { Ref = cpManualKey }));
                    Assert.Equal(0L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerPayments WHERE Reference=@Ref", new { Ref = saleManualKey }));
                    Assert.Equal(2L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType IN ('collection','increase')", new { Id = customerA.Id }));
                }

                // A collision with an actual legacy customer-payment row still
                // conflicts, even though the manual attempt matches the stored
                // payload field-for-field — ownership metadata (the
                // CustomerPayments reference equality), not the key's shape,
                // decides the outcome. No false replay, nothing written.
                var payment = accounts.AddPayment(customerA.Id, new CustomerPayment { Amount = 4.00m, PaymentDate = occurredOn, Method = "نقدي", Account = "الخزينة", Notes = "تحصيل" });
                Assert.True(payment.Id > 0);
                var collision = RunAttempt(accounts, customerA.Id, "collection", 4.00m, occurredOn, "تحصيل", payment.Reference);
                ExpectConflict(collision);
                Assert.Equal(1L, RowsFor(fixture, payment.Reference));
                Assert.Equal(91.50m, repository(fixture).GetById(customerA.Id).Balance);
            }
        }

        private static SqliteCustomerRepository repository(QaFixture fixture) { return new SqliteCustomerRepository(fixture.ConnectionString); }

        // ----- New real-OWIN API regression (QA-101) -----
        // Drives the real HTTP boundary (AlKhayalServer) through login/CSRF against
        // a disposable GUID SQLite database explicitly passed via
        // HostOptions.DatabasePath; the operator database at
        // %LOCALAPPDATA%\AlKhayal\alkhayal.db is never opened because the server
        // uses the explicit path and the process ALKHAYAL_DATABASE_PATH points at a
        // fresh temp GUID file outside that directory. The pinned repository-level
        // tests above are unchanged.
        [Fact]
        public async Task Customer_transaction_api_replays_identical_retry_exactly_once()
        {
            using (var harness = await StartQa101ApiServerAsync())
            {
                var login = await LoginAsync(harness.Client);
                var customerA = await CreateCustomerAsync(harness.Client, login, "QA-101-A", "عميل أ", 100.00m, "01011111101");
                var customerB = await CreateCustomerAsync(harness.Client, login, "QA-101-B", "عميل ب", 50.00m, "01011111102");
                Assert.True(customerA.Id > 0);
                Assert.True(customerB.Id > 0);

                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");
                const string key = "qa-101-api-op-key";

                // Initial collection for customer A: HTTP 201, replayed=false.
                var first = await PostTransactionAsync(harness.Client, login, customerA.Id, "collection", 25.00m, occurredOn, "تحصيل", key);
                Assert.Equal(HttpStatusCode.Created, first.StatusCode);
                var firstBody = await first.Content.ReadAsStringAsync();
                Assert.Contains("\"replayed\":false", firstBody);
                var firstId = ReadNumericProperty(firstBody, "id");
                Assert.True(firstId > 0);

                // Identical retry through the API: successful replay, same transaction id.
                var retry = await PostTransactionAsync(harness.Client, login, customerA.Id, "collection", 25.00m, occurredOn, "تحصيل", key);
                Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
                var retryBody = await retry.Content.ReadAsStringAsync();
                Assert.Contains("\"replayed\":true", retryBody);
                Assert.Equal(firstId, ReadNumericProperty(retryBody, "id"));

                ApiStateSnapshot(harness.ConnectionString, customerA.Id, customerB.Id, occurredOn, 250000L);
            }
        }

        [Fact]
        public async Task Customer_transaction_api_returns_409_operation_key_conflict_for_changed_and_cross_customer_payloads()
        {
            using (var harness = await StartQa101ApiServerAsync())
            {
                var login = await LoginAsync(harness.Client);
                var customerA = await CreateCustomerAsync(harness.Client, login, "QA-101-A", "عميل أ", 100.00m, "01011111101");
                var customerB = await CreateCustomerAsync(harness.Client, login, "QA-101-B", "عميل ب", 50.00m, "01011111102");

                var occurredOn = DateTime.UtcNow.ToString("yyyy-MM-dd");
                const string key = "qa-101-api-op-key";

                // Initial collection for customer A under the key.
                var first = await PostTransactionAsync(harness.Client, login, customerA.Id, "collection", 25.00m, occurredOn, "تحصيل", key);
                Assert.Equal(HttpStatusCode.Created, first.StatusCode);

                // Changed amount for the same key → HTTP 409 operation_key_conflict.
                var changedAmount = await PostTransactionAsync(harness.Client, login, customerA.Id, "collection", 50.00m, occurredOn, "تحصيل", key);
                Assert.Equal(HttpStatusCode.Conflict, changedAmount.StatusCode);
                var changedBody = await changedAmount.Content.ReadAsStringAsync();
                Assert.Contains("\"operation_key_conflict\"", changedBody);
                Assert.Contains("\"success\":false", changedBody);

                // The same key used by the other customer → HTTP 409 operation_key_conflict.
                var crossCustomer = await PostTransactionAsync(harness.Client, login, customerB.Id, "collection", 25.00m, occurredOn, "تحصيل", key);
                Assert.Equal(HttpStatusCode.Conflict, crossCustomer.StatusCode);
                Assert.Contains("\"operation_key_conflict\"", await crossCustomer.Content.ReadAsStringAsync());

                // The conflicts changed nothing: one row for the key owned by A,
                // opening rows intact, balances exactly 75.00 / 50.00.
                ApiStateSnapshot(harness.ConnectionString, customerA.Id, customerB.Id, occurredOn, 250000L);
            }
        }

        private sealed class Qa101ApiHarness : IDisposable
        {
            public string Path;
            public string ConnectionString;
            public string Url;
            public HttpClient Client;
            public void Dispose()
            {
                if (Server != null) Server.Dispose();
                if (Client != null) Client.Dispose();
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (Directory.Exists(Path)) Directory.Delete(Path, true);
            }
            internal AlKhayalServer Server;
        }

        private sealed class Qa101Login
        {
            public string Token;
            public string Csrf;
        }

        private static async Task<Qa101ApiHarness> StartQa101ApiServerAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "alkhayal-qa101-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "test.db");
            Assert.NotEqual(Db.DefaultDatabasePath(), path);
            Assert.False(File.Exists(path)); // fresh GUID temp database; never existed before this run.

            Directory.CreateDirectory(root);
            var cs = Db.ConnectionStringFor(path);
            Migrator.Run(cs);
            var users = new SqliteUserStore(cs);
            await users.CreateAsync("qa-admin", "مسؤول الجودة", new PasswordHasher().Hash("qa-secret"), "administrator", CancellationToken.None);

            var options = new HostOptions { DatabasePath = path, Url = "http://127.0.0.1:" + FreePort(), FrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-frontend") };
            var server = new AlKhayalServer();
            server.Start(options);

            return new Qa101ApiHarness
            {
                Path = root,
                ConnectionString = cs,
                Url = options.Url,
                Client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(options.Url) },
                Server = server
            };
        }

        private static async Task<Qa101Login> LoginAsync(HttpClient client)
        {
            var login = await client.PostAsync("api/auth/login", Json("{\"username\":\"qa-admin\",\"password\":\"qa-secret\"}"));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = await login.Content.ReadAsStringAsync();
            return new Qa101Login
            {
                Token = ExtractToken(ReadSetCookieHeader(login)),
                Csrf = Regex.Match(body, "\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value
            };
        }

        private static async Task<SimpleCustomerRef> CreateCustomerAsync(HttpClient client, Qa101Login login, string code, string name, decimal balance, string phone)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/customers")
            {
                Content = Json("{"
                    + "\"code\":" + JsonString(code) + ","
                    + "\"name\":" + JsonString(name) + ","
                    + "\"phone\":" + JsonString(phone) + ","
                    + "\"balance\":" + balance.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"creditLimit\":500.00"
                    + "}")
            };
            request.Headers.TryAddWithoutValidation("Cookie", CookieName + "=" + login.Token);
            request.Headers.Add("X-CSRF-Token", login.Csrf);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            return new SimpleCustomerRef { Id = long.Parse(ReadNumericProperty(body, "id").ToString()), Code = code };
        }

        private static async Task<HttpResponseMessage> PostTransactionAsync(HttpClient client, Qa101Login login, long customerId, string type, decimal amount, string occurredOn, string notes, string operationKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/customers/" + customerId.ToString(CultureInfo.InvariantCulture) + "/transactions")
            {
                Content = Json("{"
                    + "\"type\":" + JsonString(type) + ","
                    + "\"amount\":" + amount.ToString(CultureInfo.InvariantCulture) + ","
                    + "\"occurredOn\":" + JsonString(occurredOn) + ","
                    + "\"notes\":" + JsonString(notes) + ","
                    + "\"operationKey\":" + JsonString(operationKey)
                    + "}")
            };
            request.Headers.TryAddWithoutValidation("Cookie", CookieName + "=" + login.Token);
            request.Headers.Add("X-CSRF-Token", login.Csrf);
            return await client.SendAsync(request);
        }

        private sealed class SimpleCustomerRef { public long Id; public string Code; }

        private static void ApiStateSnapshot(string cs, long customerAId, long customerBId, string occurredOn, long expectedScaled)
        {
            using (var check = new SQLiteConnection(cs))
            {
                check.Open();
                Assert.Equal(3L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));
                var apiKey = "qa-101-api-op-key";
                Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = apiKey }));
                Assert.Equal(expectedScaled, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = apiKey }));
                Assert.Equal("collection", check.ExecuteScalar<string>("SELECT TransactionType FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = apiKey }));
                Assert.Equal(customerAId, check.ExecuteScalar<long>("SELECT CustomerId FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = apiKey }));
                Assert.Equal(occurredOn, check.ExecuteScalar<string>("SELECT OccurredOn FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = apiKey }));
                Assert.Equal(1000000L, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'", new { Id = customerAId }));
                Assert.Equal(500000L, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'", new { Id = customerBId }));
                Assert.Equal(750000L, check.ExecuteScalar<long>("SELECT CAST(Balance*10000 AS INTEGER) FROM Customers WHERE Id=@Id", new { Id = customerAId }));
                Assert.Equal(500000L, check.ExecuteScalar<long>("SELECT CAST(Balance*10000 AS INTEGER) FROM Customers WHERE Id=@Id", new { Id = customerBId }));
            }
        }

        private static long ReadNumericProperty(string body, string propertyName)
        {
            var match = Regex.Match(body, "\"" + Regex.Escape(propertyName) + "\":\\s*(\\d+)");
            Assert.True(match.Success, "expected a numeric property \"" + propertyName + "\" in: " + body);
            return long.Parse(match.Groups[1].Value);
        }

        private static string JsonString(string value) { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        private static StringContent Json(string json) { return new StringContent(json, Encoding.UTF8, "application/json"); }

        private static string ReadSetCookieHeader(HttpResponseMessage response)
        {
            IEnumerable<string> values;
            Assert.True(response.Headers.TryGetValues("Set-Cookie", out values), "the login response must set the session cookie");
            foreach (var value in values)
            {
                if (value.StartsWith(CookieName + "=", StringComparison.Ordinal)) return value;
            }
            throw new Xunit.Sdk.XunitException("no " + CookieName + " cookie in: " + string.Join("; ", values));
        }

        private static string ExtractToken(string cookieHeader)
        {
            var pair = cookieHeader.Substring(CookieName.Length + 1);
            var separator = pair.IndexOf(';');
            var token = separator < 0 ? pair : pair.Substring(0, separator);
            Assert.False(string.IsNullOrWhiteSpace(token));
            return token;
        }

        private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }

        private static PostAttempt RunAttempt(CustomerAccountService accounts, long customerId, string type, decimal amount, string occurredOn, string notes, string operationKey)
        {
            var attempt = new PostAttempt();
            try
            {
                attempt.Result = accounts.PostTransaction(customerId, type, amount, occurredOn, notes, operationKey);
            }
            catch (Exception error)
            {
                attempt.Error = error;
            }
            return attempt;
        }

        private static void ExpectConflict(PostAttempt attempt)
        {
            if (attempt.Error != null)
            {
                if (!attempt.Error.Message.Contains(ExpectedConflictMarker))
                {
                    throw new Xunit.Sdk.XunitException(
                        "Expected an operation-key conflict rejection (API contract: HTTP 409 " + ExpectedConflictCode +
                        "); the repository actually threw a different error: " + attempt.Error.GetType().Name + " — " + attempt.Error.Message);
                }
                return;
            }
            var result = attempt.Result;
            throw new Xunit.Sdk.XunitException(
                "Expected an operation-key conflict rejection (API contract: HTTP 409 " + ExpectedConflictCode + "), but the changed payload was accepted as a silent replay. Actual returned transaction: Id=" +
                (result == null ? "null" : result.Id.ToString()) + ", CustomerId=" + (result == null ? "null" : result.CustomerId.ToString()) +
                ", Type=" + (result == null ? "null" : result.Type) + ", Amount=" + (result == null ? "null" : result.Amount.ToString()) +
                ", Replayed=" + (result == null ? "null" : result.Replayed.ToString()));
        }

        private static long RowsFor(QaFixture fixture, string operationKey)
        {
            using (var connection = new SQLiteConnection(fixture.ConnectionString))
            {
                connection.Open();
                return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = operationKey });
            }
        }

        private static long RowsForCustomer(QaFixture fixture, long customerId)
        {
            using (var connection = new SQLiteConnection(fixture.ConnectionString))
            {
                connection.Open();
                return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id", new { Id = customerId });
            }
        }

        private static long RowsForCustomerNonOpening(QaFixture fixture, long customerId)
        {
            using (var connection = new SQLiteConnection(fixture.ConnectionString))
            {
                connection.Open();
                return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType<>'opening'", new { Id = customerId });
            }
        }

        private static long RowsForCustomerAndKey(QaFixture fixture, long customerId, string operationKey)
        {
            using (var connection = new SQLiteConnection(fixture.ConnectionString))
            {
                connection.Open();
                return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND OperationKey=@Key", new { Id = customerId, Key = operationKey });
            }
        }

        private sealed class QaFixture : IDisposable
        {
            public string Path { get; private set; }
            public string ConnectionString { get; private set; }
            public SqliteCustomerRepository CustomersRepo { get; private set; }

            public static QaFixture Create()
            {
                var fixture = new QaFixture();
                // Guaranteed GUID temp path, never the normal AppData database.
                fixture.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qa-b4a-" + Guid.NewGuid().ToString("N") + ".db");
                fixture.ConnectionString = Db.ConnectionStringFor(fixture.Path);
                Migrator.Run(fixture.ConnectionString);
                fixture.CustomersRepo = new SqliteCustomerRepository(fixture.ConnectionString);
                return fixture;
            }

            public CustomerService CreateCustomerService() { return new CustomerService(CustomersRepo); }
            public CustomerAccountService CreateAccountService() { return new CustomerAccountService(new SqliteCustomerAccountRepository(ConnectionString)); }

            public void SnapshotAfterInitial(string occurredOn)
            {
                // Exact post-state after the initial collection, re-verified after
                // every conflict attempt: both customers were created with a positive
                // balance, so CustomerRepository.Add wrote an opening row for each;
                // together with the collection under key K there are 3 ledger rows.
                // Exact balances and the original row's type/date/amount remain
                // unchanged and untouched.
                var customerA = CustomersRepo.GetAll()[0];
                Customer customerB = null;
                foreach (var c in CustomersRepo.GetAll())
                {
                    if (c.Code == "QA-B4A") customerA = c; else if (c.Code == "QA-B4B") customerB = c;
                }
                using (var check = new SQLiteConnection(ConnectionString))
                {
                    check.Open();
                    Assert.Equal(3L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));
                    Assert.Equal(1L, check.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = ConflictKey }));
                    Assert.Equal(1000000L, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'", new { Id = customerA.Id }));
                    Assert.Equal(500000L, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'", new { Id = customerB.Id }));
                    Assert.Equal(250000L, check.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = ConflictKey }));
                    Assert.Equal(occurredOn, check.ExecuteScalar<string>("SELECT OccurredOn FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = ConflictKey }));
                    Assert.Equal("collection", check.ExecuteScalar<string>("SELECT TransactionType FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = ConflictKey }));
                    Assert.Equal(customerA.Id, check.ExecuteScalar<long>("SELECT CustomerId FROM CustomerTransactions WHERE OperationKey=@Key", new { Key = ConflictKey }));
                }
                Assert.Equal(75.00m, CustomersRepo.GetById(customerA.Id).Balance);
                Assert.Equal(50.00m, CustomersRepo.GetById(customerB.Id).Balance);
            }

            public void Dispose()
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (File.Exists(Path)) File.Delete(Path);
            }
        }

        private sealed class PostAttempt
        {
            public CustomerStatementTransaction Result;
            public Exception Error;
        }
    }
}
