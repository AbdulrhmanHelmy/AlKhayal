using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Authentication
{
    public sealed class AuthService
    {
        private readonly IUserStore _users; private readonly ISessionStore _sessions; private readonly IPasswordHasher _hasher; private readonly ITokenGenerator _tokens; private readonly IClock _clock;
        public AuthService(IUserStore users, ISessionStore sessions, IPasswordHasher hasher, ITokenGenerator tokens, IClock clock) { _users = users; _sessions = sessions; _hasher = hasher; _tokens = tokens; _clock = clock; }
        public async Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken ct)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrEmpty(command.Password)) return Fail("validation", "اسم المستخدم وكلمة المرور مطلوبان.");
            var user = await _users.FindByUsernameAsync(command.Username.Trim(), ct).ConfigureAwait(false);
            if (user == null || !_hasher.Verify(command.Password, user.PasswordHash)) return Fail("invalid_credentials", "بيانات الدخول غير صحيحة.");
            if (!user.IsActive) return Fail("disabled_account", "هذا الحساب موقوف. راجع مسؤول النظام.");
            await _users.UpdateLastLoginAsync(user.Id, _clock.UtcNow, ct).ConfigureAwait(false);
            var identity = ToIdentity(user);
            var session = new SessionRecord { Token = _tokens.CreateToken(), CsrfToken = _tokens.CreateToken(), UserId = user.Id, Username = user.Username, DisplayName = user.DisplayName, Role = user.Role, ExpiresUtc = _clock.UtcNow.AddHours(8) };
            await _sessions.SaveAsync(session, ct).ConfigureAwait(false);
            return new AuthResult { Succeeded = true, SessionToken = session.Token, CsrfToken = session.CsrfToken, User = identity };
        }
        public async Task<UserIdentity> GetCurrentUserAsync(string token, CancellationToken ct)
        { var session = await GetSessionAsync(token, ct).ConfigureAwait(false); return session == null ? null : ToIdentity(session); }
        public async Task<string> GetCsrfTokenAsync(string token, CancellationToken ct)
        { var session = await GetSessionAsync(token, ct).ConfigureAwait(false); return session == null ? null : session.CsrfToken; }
        public async Task<bool> ValidateCsrfAsync(string token, string csrfToken, CancellationToken ct)
        { var session = await GetSessionAsync(token, ct).ConfigureAwait(false); return session != null && !string.IsNullOrWhiteSpace(csrfToken) && FixedTimeEquals(session.CsrfToken, csrfToken); }
        public async Task<bool> LogoutAsync(string token, CancellationToken ct) { if (string.IsNullOrWhiteSpace(token)) return false; await _sessions.DeleteAsync(token, ct).ConfigureAwait(false); return true; }
        private async Task<SessionRecord> GetSessionAsync(string token, CancellationToken ct) { if (string.IsNullOrWhiteSpace(token)) return null; var s = await _sessions.FindAsync(token, ct).ConfigureAwait(false); if (s == null) return null; if (s.ExpiresUtc > _clock.UtcNow) return s; await _sessions.DeleteAsync(token, ct).ConfigureAwait(false); return null; }
        private static UserIdentity ToIdentity(UserRecord user) { return new UserIdentity { Id=user.Id, Username=user.Username, DisplayName=user.DisplayName, Role=user.Role }; }
        private static UserIdentity ToIdentity(SessionRecord session) { return new UserIdentity { Id=session.UserId, Username=session.Username, DisplayName=session.DisplayName, Role=session.Role }; }
        private static bool FixedTimeEquals(string left, string right) { if (left == null || right == null || left.Length != right.Length) return false; var difference=0; for(var index=0; index<left.Length; index++) difference |= left[index] ^ right[index]; return difference == 0; }
        private static AuthResult Fail(string code, string message) { return new AuthResult { Succeeded = false, ErrorCode = code, ErrorMessage = message }; }
    }
}
