using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Kroeg.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace Kroeg.Server.Services
{
    public class KroegUserStore : IUserStore<APUser>, IUserPasswordStore<APUser>, IRoleStore<IdentityRole>
    {
        private readonly DbConnection _connection;
        public KroegUserStore(DbConnection connection)
        {
            _connection = connection;
        }

        public async Task<IdentityResult> CreateAsync(APUser user, CancellationToken cancellationToken)
        {
            await _connection.ExecuteAsync("insert into \"Users\" (\"Id\", \"Username\", \"Email\", \"PasswordHash\", \"NormalisedUsername\") values (@Id, @Username, @Email, @PasswordHash, @NormalisedUsername)", user);

            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DeleteAsync(APUser user, CancellationToken cancellationToken)
        {
            await _connection.ExecuteAsync("delete from \"Users\" where \"Id\" = @Id", new { Id = user.Id });
            return IdentityResult.Success;
        }
 
        public async Task<APUser> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            return await _connection.QuerySingleOrDefaultAsync<APUser>("select * from \"Users\" where \"Id\" = @Id limit 1", new  { Id = userId });
        }

        public async Task<APUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            return await _connection.QuerySingleOrDefaultAsync<APUser>("select * from \"Users\" where \"NormalisedUsername\" = @Username limit 1", new  { Username = normalizedUserName });
        }

        public Task<string> GetNormalizedUserNameAsync(APUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.NormalisedUsername);
        }

        public Task<string> GetUserIdAsync(APUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.Id);
        }

        public Task<string> GetUserNameAsync(APUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.Username);
        }

        public async Task SetNormalizedUserNameAsync(APUser user, string normalizedName, CancellationToken cancellationToken)
        {
            await _connection.ExecuteAsync("update \"Users\" set \"NormalisedUsername\" = @Username where \"Id\" = @Id", new { Username = normalizedName, Id = user.Id });
            user.NormalisedUsername = normalizedName;
        }

        public async Task SetUserNameAsync(APUser user, string userName, CancellationToken cancellationToken)
        {
            await _connection.ExecuteAsync("update \"Users\" set \"Username\" = @Username where \"Id\" = @Id", new { Username = userName, Id = user.Id });
            user.Username = userName;
        }

        public Task<IdentityResult> UpdateAsync(APUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public void Dispose()
        { }

        public async Task SetPasswordHashAsync(APUser user, string passwordHash, CancellationToken cancellationToken)
        {
            await _connection.ExecuteAsync("update \"Users\" set \"Username\" = @PasswordHash where \"Id\" = @Id", new { PasswordHash = passwordHash, Id = user.Id });
            user.PasswordHash = passwordHash;
        }

        Task<string> IUserPasswordStore<APUser>.GetPasswordHashAsync(APUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.PasswordHash);
        }

        Task<bool> IUserPasswordStore<APUser>.HasPasswordAsync(APUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.PasswordHash != null);
        }

        public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Failed());
        }

        public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Failed());
        }

        public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Failed());
        }

        public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        public Task<string> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        public Task SetRoleNameAsync(IdentityRole role, string roleName, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        public Task<string> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        public Task SetNormalizedRoleNameAsync(IdentityRole role, string normalizedName, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        Task<IdentityRole> IRoleStore<IdentityRole>.FindByIdAsync(string roleId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IdentityRole>(null);
        }

        Task<IdentityRole> IRoleStore<IdentityRole>.FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
        {
            return Task.FromResult<IdentityRole>(null);
        }
    }
}