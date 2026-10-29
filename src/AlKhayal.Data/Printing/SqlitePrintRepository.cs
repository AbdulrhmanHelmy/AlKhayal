using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AlKhayal.Application.Printing;
using Dapper;

namespace AlKhayal.Data.Printing
{
    public sealed class SqlitePrintRepository : IPrintTemplateRepository
    {
        private readonly string _connectionString;
        public SqlitePrintRepository(string connectionString) { _connectionString = connectionString; }
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 8000000, RecursionLimit = 20 };
        public async Task<PrintingSettings> ReadAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var json = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT Json FROM PrintingConfiguration WHERE Id=1", cancellationToken: ct)).ConfigureAwait(false);
                return json == null ? null : Serializer().Deserialize<PrintingSettings>(json);
            }
        }
        public async Task<PrintingSettings> SaveAsync(PrintingSettings s, long actorId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var version = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE((SELECT Version FROM PrintingConfiguration WHERE Id=1),0)", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (version != s.Version) throw new PrintException("conflict", "تغيرت إعدادات الطباعة. أعد تحميلها قبل الحفظ.");
                    s.Version = checked(version + 1);
                    var now = DateTime.UtcNow.ToString("o"); var json = Serializer().Serialize(s);
                    await c.ExecuteAsync(new CommandDefinition("INSERT OR REPLACE INTO PrintingConfiguration(Id,Version,Json,ChangedByUserId,UpdatedUtc) VALUES(1,@Version,@json,@actorId,@now); INSERT INTO PrintingAudit(Version,ActorId,CreatedUtc) VALUES(@Version,@actorId,@now)", new { s.Version, json, actorId, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    tx.Commit(); return s;
                }
            }
        }
        public async Task<PrintJob> ClaimAsync(PrintJob job, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var existing = await c.QuerySingleOrDefaultAsync<PrintJob>(new CommandDefinition("SELECT * FROM PrintJobs WHERE RequestKey=@RequestKey", job, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (existing != null)
                    {
                        if (existing.DocumentType != job.DocumentType || existing.DocumentId != job.DocumentId || existing.Reprint != job.Reprint || existing.RequestHash != job.RequestHash) throw new PrintException("key_conflict", "مفتاح الطباعة مستخدم لمستند أو دفعة مختلفة.");
                        existing.Replayed = true; return existing;
                    }
                    if (await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM PrintJobs WHERE DocumentType=@DocumentType AND DocumentId=@DocumentId AND Status='accepted'", job, tx, cancellationToken: ct)).ConfigureAwait(false) > 0)
                        throw new PrintException("print_busy", "يوجد طلب جارٍ لهذا المستند. تحقق من سجل الطباعة قبل الإعادة؛ الطلب غير المكتمل بعد إعادة التشغيل يحتاج مراجعة المسؤول.");
                    job.Id = await c.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO PrintJobs(RequestKey,DocumentType,DocumentId,Reference,ActorId,PrinterName,TemplateId,Copies,Reprint,CreatedUtc,Status,Message,RequestHash,BatchSummaryJson) VALUES(@RequestKey,@DocumentType,@DocumentId,@Reference,@ActorId,@PrinterName,@TemplateId,@Copies,@Reprint,@CreatedUtc,@Status,@Message,@RequestHash,@BatchSummaryJson); SELECT last_insert_rowid();", job, tx, cancellationToken: ct)).ConfigureAwait(false);
                    tx.Commit(); return job;
                }
            }
        }
        public async Task CompleteAsync(PrintJob job, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            { await c.OpenAsync(ct).ConfigureAwait(false); await c.ExecuteAsync(new CommandDefinition("UPDATE PrintJobs SET Status=@Status,ErrorCode=@ErrorCode,Message=@Message WHERE Id=@Id AND Status='accepted'", job, cancellationToken: ct)).ConfigureAwait(false); }
        }
        public async Task<PrintJob> FindAsync(string requestKey, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            { await c.OpenAsync(ct).ConfigureAwait(false); return await c.QuerySingleOrDefaultAsync<PrintJob>(new CommandDefinition("SELECT * FROM PrintJobs WHERE RequestKey=@requestKey", new { requestKey }, cancellationToken: ct)).ConfigureAwait(false); }
        }
        public async Task<IReadOnlyList<PrintJob>> JobsAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            { await c.OpenAsync(ct).ConfigureAwait(false); return (await c.QueryAsync<PrintJob>(new CommandDefinition("SELECT * FROM PrintJobs ORDER BY Id DESC LIMIT 100", cancellationToken: ct)).ConfigureAwait(false)).ToList(); }
        }
        public async Task ReviewAsync(long jobId, long actorId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var count = await c.ExecuteAsync(new CommandDefinition("UPDATE PrintJobs SET Status='uncertain',ErrorCode='reviewed_unknown',Message='راجع المسؤول قائمة Windows؛ النتيجة الفعلية غير مؤكدة. الإعادة يدوية فقط.',ReviewedByUserId=@actorId,ReviewedUtc=@now WHERE Id=@jobId AND Status='accepted' AND CreatedUtc<@cutoff", new { jobId, actorId, now = DateTime.UtcNow.ToString("o"), cutoff = DateTime.UtcNow.AddMinutes(-10).ToString("o") }, cancellationToken: ct)).ConfigureAwait(false);
                if (count != 1) throw new PrintException("review_not_allowed", "تُراجع فقط المهام المقبولة غير المكتملة الأقدم من عشر دقائق، بعد التحقق من قائمة Windows.");
            }
        }
    }
}
