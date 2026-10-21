using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Users
{
    public interface IUserManagementStore
    {
        Task<IReadOnlyList<ManagedUser>> ListAsync(CancellationToken ct);
        Task<ManagedUser> FindAsync(long userId, CancellationToken ct);
        Task<ManagedUser> CreateAsync(CreateUserCommand command, string passwordHash, CancellationToken ct);
        Task<UserManagementResult<ManagedUser>> UpdateAsync(UpdateUserCommand command, CancellationToken ct);
        Task<UserManagementResult<ManagedUser>> SetStatusAsync(SetUserStatusCommand command, CancellationToken ct);
        Task<UserManagementResult<bool>> ResetPasswordAsync(ResetUserPasswordCommand command, string passwordHash, CancellationToken ct);
        Task<IReadOnlyList<string>> GetScreenIdsAsync(long userId, CancellationToken ct);
        Task<UserManagementResult<bool>> SetScreenIdsAsync(SetScreenPermissionsCommand command, CancellationToken ct);
    }

    public interface IScreenCatalog
    {
        IReadOnlyList<ScreenDefinition> Screens { get; }
        ScreenDefinition Find(string screenId);
    }
}
