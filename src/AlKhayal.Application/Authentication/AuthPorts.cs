using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Authentication
{
    public interface IUserStore
    {
        Task<UserRecord> FindByUsernameAsync(string username, CancellationToken cancellationToken);
        Task<bool> IsEmptyAsync(CancellationToken cancellationToken);
        Task<bool> TryCreateFirstAdminAsync(string username, string displayName, string passwordHash, CancellationToken cancellationToken);
        Task<long> CreateAsync(string username, string displayName, string passwordHash, string role, CancellationToken cancellationToken);
        Task UpdatePasswordHashAsync(long userId, string passwordHash, CancellationToken cancellationToken);
        Task UpdateLastLoginAsync(long userId, DateTime utc, CancellationToken cancellationToken);
    }
    public interface ISessionStore { Task SaveAsync(SessionRecord session, CancellationToken cancellationToken); Task<SessionRecord> FindAsync(string token, CancellationToken cancellationToken); Task DeleteAsync(string token, CancellationToken cancellationToken); }
    public interface IPasswordHasher { string Hash(string password); bool Verify(string password, string encodedHash); }
    public interface ITokenGenerator { string CreateToken(); }
    public interface IClock { DateTime UtcNow { get; } }
}
