using System.Text.Json;
using Dapper;
using Goldata.Application.Identity;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Goldata.Infrastructure.Legacy;
using MySqlConnector;

namespace Goldata.Infrastructure.Identity;

public sealed record UserListItem(
    int Id, string Login, string DisplayName, int RoleId, string Role, bool RoleIsReadOnly, string Rank, string Status,
    bool MustChangePassword, DateTime? LastLoginAt, int ActiveTokens, int RowVersion, bool NameLocked);

public sealed record CreateUserRequest(string Login, string DisplayName, int RoleId, string Rank, string? Password,
    bool AddToStaffLookup, string? StaffCode);

public sealed record UpdateUserRequest(int RoleId, string Rank, string? DisplayName, string? Login);

public sealed record DeleteCheck(IReadOnlyList<TodoRef> OpenTodos, int ActiveTokens, DateTime? LastLoginAt, bool InStaffLookup);

public sealed record TodoRef(string Module, string ModuleTitle, long RecordId);

public sealed record RoleMatrix(IReadOnlyDictionary<string, IReadOnlyList<string>> Layers, int RowVersion);

public sealed record MatrixDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> AffectedUsers, bool Sensitive);

public sealed record AdminAuditEntry(long Id, string ActorName, string Action, string TargetType, int? TargetId, string? TargetName,
    string? BeforeJson, string? AfterJson, string? Ip, DateTime At);

/// <summary>
/// 设置 ▸ 用户与权限 (M29, REQ-ADM-1…14). All rules are enforced here, not only in the UI (002-spec §5.4).
/// Every action writes <c>app_admin_audit</c>; staff-lookup writes in <c>goldata</c> also write <c>update_logs</c>.
/// </summary>
public sealed class AdminService(Db db)
{
    // ---------- users ----------

    public async Task<IReadOnlyList<UserListItem>> ListUsersAsync(string? q, int? roleId, string? rank, string? status, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        var users = (await c.QueryAsync<AppUser>(new CommandDefinition(AppUser.SelectSql + " ORDER BY r.sort, u.login", cancellationToken: ct))).ToList();
        var tokens = (await c.QueryAsync<(int UserId, long N)>(new CommandDefinition(
            "SELECT user_id, COUNT(*) FROM app_api_tokens WHERE revoked_at IS NULL AND expires_at > NOW() GROUP BY user_id",
            cancellationToken: ct))).ToDictionary(x => x.UserId, x => (int)x.N);
        var now = DateTime.Now;
        return users.Select(u => ToItem(u, tokens.GetValueOrDefault(u.Id), now))
            .Where(u => string.IsNullOrWhiteSpace(q) || u.Login.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase) || u.DisplayName.Contains(q.Trim()))
            .Where(u => roleId is null || u.RoleId == roleId)
            .Where(u => string.IsNullOrEmpty(rank) || u.Rank == rank)
            .Where(u => string.IsNullOrEmpty(status) || u.Status == status)
            .ToList();
    }

    private static UserListItem ToItem(AppUser u, int tokens, DateTime now) => new(
        u.Id, u.Login, u.DisplayName, u.RoleId, u.RoleName, u.RoleIsReadOnly, u.Rank,
        !u.IsActive ? "disabled" : u.IsLocked(now) ? "locked" : "active",
        u.MustChangePassword, u.LastLoginAt, tokens, u.RowVersion, u.LastLoginAt is not null);

    public async Task<UserListItem> GetUserAsync(int id, CancellationToken ct) =>
        (await ListUsersAsync(null, null, null, null, ct)).FirstOrDefault(u => u.Id == id) ?? throw AppError.NotFound("用户");

    public async Task<(UserListItem User, string InitialPassword)> CreateUserAsync(CreateUserRequest r, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        var login = (r.Login ?? "").Trim().ToLowerInvariant();
        AdminRules.ValidateLogin(login);
        var name = AdminRules.NormalizeDisplayName(r.DisplayName);
        var password = string.IsNullOrEmpty(r.Password) ? AdminRules.GeneratePassword() : r.Password;
        AdminRules.ValidateNewPassword(password, login);

        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var role = await RoleAsync(c, tx, r.RoleId, ct);
        await RequireRankAsync(c, tx, r.Rank, ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_users WHERE login = @login", new { login }, tx, cancellationToken: ct)) > 0)
            throw AppError.Conflict("LOGIN_EXISTS", "登入名已存在");
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_users WHERE display_name = @name", new { name }, tx, cancellationToken: ct)) > 0)
            throw AppError.Conflict("NAME_EXISTS", "姓名已被其他用户使用（姓名必须唯一，用于待办与修改记录）");

        var id = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO app_users (login, display_name, role_id, `rank`, password_hash, must_change_password, is_active, security_stamp, created_by)
            VALUES (@login, @name, @roleId, @rank, @hash, 1, 1, @stamp, @actor);
            SELECT LAST_INSERT_ID();
            """, new { login, name, roleId = role.Id, rank = r.Rank, hash = PasswordHasher.Hash(password), stamp = Stamps.New(), actor = actor.UserId },
            tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "user.create", "user", id, name, null,
            new { login, displayName = name, role = role.Name, rank = r.Rank, r.AddToStaffLookup, r.StaffCode }, ct);

        if (r.AddToStaffLookup)
        {
            // goldata write (lookup_tables + update_logs) in its own transaction; the user row commits only if it succeeds.
            await using var lc = await db.LegacyAsync(ct);
            await using var ltx = await lc.BeginAsync(ct);
            await LookupService.EnsureStaffAsync(lc, ltx, name, string.IsNullOrWhiteSpace(r.StaffCode) ? LookupService.SuggestStaffCode(role.Name, r.Rank) : r.StaffCode.Trim(), actor, ct);
            await tx.CommitAsync(ct);
            await ltx.CommitAsync(ct);
        }
        else await tx.CommitAsync(ct);

        UserContextService.InvalidateAll();
        return (await GetUserAsync(id, ct), password);
    }

    public async Task<UserListItem> UpdateUserAsync(int id, UpdateUserRequest r, int rowVersion, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, id, rowVersion, ct);
        var role = await RoleAsync(c, tx, r.RoleId, ct);
        await RequireRankAsync(c, tx, r.Rank, ct);

        var name = r.DisplayName is null ? u.DisplayName : AdminRules.NormalizeDisplayName(r.DisplayName);
        var login = r.Login is null ? u.Login : r.Login.Trim().ToLowerInvariant();
        if (name != u.DisplayName || login != u.Login)
        {
            if (await NameLockedAsync(u, ct))
                throw AppError.Conflict("NAME_LOCKED", "该用户已登入过或已出现在业务记录中，姓名和登入名不能再修改（请新建用户并停用旧用户）");
            AdminRules.ValidateLogin(login);
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_users WHERE (login = @login OR display_name = @name) AND id <> @id",
                    new { login, name, id }, tx, cancellationToken: ct)) > 0)
                throw AppError.Conflict("LOGIN_EXISTS", "登入名或姓名已被使用");
        }

        var roleChanged = role.Id != u.RoleId || r.Rank != u.Rank;
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE app_users SET role_id = @roleId, `rank` = @rank, display_name = @name, login = @login,
                   security_stamp = IF(@roleChanged, @stamp, security_stamp), row_version = row_version + 1
            WHERE id = @id
            """, new { roleId = role.Id, rank = r.Rank, name, login, roleChanged, stamp = Stamps.New(), id }, tx, cancellationToken: ct));

        await GuardAdminsAsync(c, tx, actor, ct);
        await AuditAsync(c, tx, actor, ip, "user.update", "user", id, name,
            new { u.Login, u.DisplayName, role = u.RoleName, u.Rank }, new { login, displayName = name, role = role.Name, rank = r.Rank }, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
        return await GetUserAsync(id, ct);
    }

    public Task<UserListItem> DisableAsync(int id, ICurrentUser actor, string? ip, CancellationToken ct) =>
        SetActiveAsync(id, false, actor, ip, ct);

    public Task<UserListItem> EnableAsync(int id, ICurrentUser actor, string? ip, CancellationToken ct) =>
        SetActiveAsync(id, true, actor, ip, ct);

    private async Task<UserListItem> SetActiveAsync(int id, bool active, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        if (!active && id == actor.UserId) throw AppError.Conflict("SELF_CHANGE", "不能停用自己的账户");
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, id, null, ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE app_users SET is_active = @active, security_stamp = @stamp, row_version = row_version + 1 WHERE id = @id",
            new { active, stamp = Stamps.New(), id }, tx, cancellationToken: ct));
        if (!active)
            await c.ExecuteAsync(new CommandDefinition("UPDATE app_api_tokens SET revoked_at = NOW() WHERE user_id = @id AND revoked_at IS NULL",
                new { id }, tx, cancellationToken: ct));
        await GuardAdminsAsync(c, tx, actor, ct);
        await AuditAsync(c, tx, actor, ip, active ? "user.enable" : "user.disable", "user", id, u.DisplayName, null, null, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
        return await GetUserAsync(id, ct);
    }

    public async Task<UserListItem> UnlockAsync(int id, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, id, null, ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE app_users SET failed_logins = 0, locked_until = NULL WHERE id = @id",
            new { id }, tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "user.unlock", "user", id, u.DisplayName, null, null, ct);
        await tx.CommitAsync(ct);
        return await GetUserAsync(id, ct);
    }

    public async Task<string> ResetPasswordAsync(int id, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        var password = AdminRules.GeneratePassword();
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, id, null, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE app_users SET password_hash = @hash, must_change_password = 1, failed_logins = 0, locked_until = NULL,
                   security_stamp = @stamp, row_version = row_version + 1 WHERE id = @id
            """, new { hash = PasswordHasher.Hash(password), stamp = Stamps.New(), id }, tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "user.reset_password", "user", id, u.DisplayName, null, null, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
        return password;
    }

    public async Task<DeleteCheck> DeleteCheckAsync(int id, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        var u = await c.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(AppUser.SelectSql + " WHERE u.id = @id", new { id }, cancellationToken: ct))
                ?? throw AppError.NotFound("用户");
        var tokens = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM app_api_tokens WHERE user_id = @id AND revoked_at IS NULL AND expires_at > NOW()", new { id }, cancellationToken: ct));
        await using var lc = await db.LegacyAsync(ct);
        var open = (await TodoService.OpenFor(lc, u.DisplayName, ct))
            .Select(t => ModuleRegistry.FindByTable(t.Table) is { } m ? new TodoRef(m.Key, m.Title, t.Id) : new TodoRef(t.Table, t.Table, t.Id))
            .ToList();
        var staff = (await LookupService.ItemsAsync(lc, null, LookupService.StaffCategory, ct)).Any(i => i.Item == u.DisplayName && i.Value != "");
        return new DeleteCheck(open, tokens, u.LastLoginAt, staff);
    }

    public async Task DeleteUserAsync(int id, bool removeFromStaffLookup, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        if (id == actor.UserId) throw AppError.Conflict("SELF_CHANGE", "不能删除自己的账户");
        var check = await DeleteCheckAsync(id, ct);
        if (check.OpenTodos.Count > 0)
            throw AppError.Conflict("HAS_OPEN_TODOS", "该用户仍有待办事项，请先转交给其他人（或改为停用）", check.OpenTodos);

        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, id, null, ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_users WHERE id = @id", new { id }, tx, cancellationToken: ct));   // tokens + overrides cascade
        await GuardAdminsAsync(c, tx, actor, ct);
        await AuditAsync(c, tx, actor, ip, "user.delete", "user", id, u.DisplayName,
            new { u.Login, u.DisplayName, role = u.RoleName, u.Rank }, new { removeFromStaffLookup }, ct);

        if (removeFromStaffLookup)
        {
            await using var lc = await db.LegacyAsync(ct);
            await using var ltx = await lc.BeginAsync(ct);
            await LookupService.DeactivateStaffAsync(lc, ltx, u.DisplayName, actor, ct);
            await tx.CommitAsync(ct);
            await ltx.CommitAsync(ct);
        }
        else await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
    }

    public async Task<IReadOnlyList<UserOverride>> GetOverridesAsync(int id, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        return await UserContextService.OverridesAsync(c, null, id, ct);
    }

    public async Task PutOverridesAsync(int id, IReadOnlyList<UserOverride> overrides, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        foreach (var o in overrides)
            if (!PermissionCatalog.IsValid(o.Permission)) throw AppError.Validation($"未知权限：{o.Permission}");
        if (overrides.GroupBy(o => o.Permission).Any(g => g.Count() > 1)) throw AppError.Validation("权限重复");

        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, id, null, ct);
        var before = await UserContextService.OverridesAsync(c, tx, id, ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_user_permission_overrides WHERE user_id = @id", new { id }, tx, cancellationToken: ct));
        foreach (var o in overrides)
            await c.ExecuteAsync(new CommandDefinition(
                "INSERT INTO app_user_permission_overrides (user_id, permission, allow) VALUES (@id, @p, @a)",
                new { id, p = o.Permission, a = o.Allow }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("UPDATE app_users SET row_version = row_version + 1 WHERE id = @id", new { id }, tx, cancellationToken: ct));
        await GuardAdminsAsync(c, tx, actor, ct);
        await AuditAsync(c, tx, actor, ip, "user.overrides", "user", id, u.DisplayName, before, overrides, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
    }

    public async Task<IReadOnlyList<PermissionSource>> EffectiveAsync(int id, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        var u = await c.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(AppUser.SelectSql + " WHERE u.id = @id", new { id }, cancellationToken: ct))
                ?? throw AppError.NotFound("用户");
        return EffectivePermissions.Explain(u.Rank, u.RoleIsReadOnly,
            await UserContextService.RoleGrantsAsync(c, null, u.RoleId, ct), await UserContextService.OverridesAsync(c, null, id, ct));
    }

    public async Task RevokeUserTokenAsync(int userId, int? tokenId, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var u = await LockUserAsync(c, tx, userId, null, ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE app_api_tokens SET revoked_at = NOW() WHERE user_id = @userId AND revoked_at IS NULL AND (@tokenId IS NULL OR id = @tokenId)",
            new { userId, tokenId }, tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "token.revoke", "token", tokenId, u.DisplayName, null, new { userId, tokenId }, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
    }

    public async Task<IReadOnlyList<ApiTokenInfo>> UserTokensAsync(int userId, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        return (await c.QueryAsync<ApiTokenInfo>(new CommandDefinition("""
            SELECT id, name, scopes, expires_at AS ExpiresAt, last_used_at AS LastUsedAt, revoked_at AS RevokedAt, created_at AS CreatedAt
            FROM app_api_tokens WHERE user_id = @userId ORDER BY created_at DESC
            """, new { userId }, cancellationToken: ct))).ToList();
    }

    // ---------- roles ----------

    public async Task<IReadOnlyList<AppRole>> ListRolesAsync(CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        return (await c.QueryAsync<AppRole>(new CommandDefinition("""
            SELECT r.id, r.name, r.description, r.is_read_only AS IsReadOnly, r.is_seeded AS IsSeeded, r.sort, r.row_version AS RowVersion,
                   (SELECT COUNT(*) FROM app_users u WHERE u.role_id = r.id) AS UserCount
            FROM app_roles r ORDER BY r.sort, r.id
            """, cancellationToken: ct))).ToList();
    }

    public async Task<AppRole> CreateRoleAsync(string name, string? description, bool isReadOnly, int? copyFromRoleId, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        name = AdminRules.NormalizeRoleName(name);
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_roles WHERE name = @name", new { name }, tx, cancellationToken: ct)) > 0)
            throw AppError.Conflict("ROLE_EXISTS", "角色名称已存在");
        var sort = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(MAX(sort), 0) + 1 FROM app_roles", transaction: tx, cancellationToken: ct));
        var id = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO app_roles (name, description, is_read_only, is_seeded, sort) VALUES (@name, @description, @isReadOnly, 0, @sort);
            SELECT LAST_INSERT_ID();
            """, new { name, description = description?.Trim(), isReadOnly, sort }, tx, cancellationToken: ct));
        if (copyFromRoleId is { } src)
        {
            var grants = (await UserContextService.RoleGrantsAsync(c, tx, src, ct))
                .Where(g => !isReadOnly || PermissionCatalog.Find(g.Permission) is not { IsWrite: true }).ToList();
            foreach (var g in grants)
                await c.ExecuteAsync(new CommandDefinition("INSERT INTO app_role_permissions (role_id, `rank`, permission) VALUES (@id, @Rank, @Permission)",
                    new { id, g.Rank, g.Permission }, tx, cancellationToken: ct));
        }
        await AuditAsync(c, tx, actor, ip, "role.create", "role", id, name, null, new { name, description, isReadOnly, copyFromRoleId }, ct);
        await tx.CommitAsync(ct);
        return (await ListRolesAsync(ct)).Single(r => r.Id == id);
    }

    public async Task<AppRole> UpdateRoleAsync(int id, string? description, bool isReadOnly, int rowVersion, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var role = await LockRoleAsync(c, tx, id, rowVersion, ct);
        if (isReadOnly)   // a read-only role keeps no write grants (REQ-ADM-11)
        {
            var writes = (await UserContextService.RoleGrantsAsync(c, tx, id, ct)).Where(g => PermissionCatalog.Find(g.Permission) is { IsWrite: true });
            foreach (var g in writes)
                await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_role_permissions WHERE role_id = @id AND `rank` = @Rank AND permission = @Permission",
                    new { id, g.Rank, g.Permission }, tx, cancellationToken: ct));
        }
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE app_roles SET description = @description, is_read_only = @isReadOnly, row_version = row_version + 1 WHERE id = @id",
            new { id, description = description?.Trim(), isReadOnly }, tx, cancellationToken: ct));
        await GuardAdminsAsync(c, tx, actor, ct);
        await AuditAsync(c, tx, actor, ip, "role.update", "role", id, role.Name,
            new { role.Description, role.IsReadOnly }, new { description, isReadOnly }, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
        return (await ListRolesAsync(ct)).Single(r => r.Id == id);
    }

    public async Task DeleteRoleAsync(int id, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var role = await LockRoleAsync(c, tx, id, null, ct);
        var users = (await c.QueryAsync<string>(new CommandDefinition("SELECT display_name FROM app_users WHERE role_id = @id", new { id }, tx, cancellationToken: ct))).ToList();
        if (users.Count > 0) throw AppError.Conflict("ROLE_IN_USE", "仍有用户属于该角色，不能删除", users);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_roles WHERE id = @id", new { id }, tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "role.delete", "role", id, role.Name, new { role.Name, role.Description }, null, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<RoleMatrix> GetMatrixAsync(int roleId, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        var role = await LockRoleAsync(c, null, roleId, null, ct);
        var grants = await UserContextService.RoleGrantsAsync(c, null, roleId, ct);
        return new RoleMatrix(Layers(grants), role.RowVersion);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Layers(IEnumerable<RoleGrant> grants) =>
        grants.GroupBy(g => g.Rank).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Permission).Order(StringComparer.Ordinal).ToList());

    /// <summary>Replaces the role's grants. With <paramref name="dryRun"/> only the diff and affected users are returned (REQ-ADM-13).</summary>
    public async Task<MatrixDiff> PutMatrixAsync(int roleId, IReadOnlyDictionary<string, IReadOnlyList<string>> layers, int rowVersion, bool dryRun,
        ICurrentUser actor, string? ip, CancellationToken ct)
    {
        var grants = layers.SelectMany(kv => kv.Value.Select(p => new RoleGrant(kv.Key, p))).Distinct().ToList();
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var role = await LockRoleAsync(c, tx, roleId, rowVersion, ct);
        var ranks = (await c.QueryAsync<string>(new CommandDefinition("SELECT name FROM app_ranks", transaction: tx, cancellationToken: ct))).ToHashSet();
        AdminRules.ValidateGrants(grants, role.IsReadOnly, ranks);

        var before = await UserContextService.RoleGrantsAsync(c, tx, roleId, ct);
        static string Key(RoleGrant g) => g.Rank == EffectivePermissions.AllRanks ? g.Permission : $"{g.Permission}@{g.Rank}";
        var beforeKeys = before.Select(Key).ToHashSet();
        var afterKeys = grants.Select(Key).ToHashSet();
        var added = afterKeys.Except(beforeKeys).Order(StringComparer.Ordinal).ToList();
        var removed = beforeKeys.Except(afterKeys).Order(StringComparer.Ordinal).ToList();
        var affected = (await c.QueryAsync<string>(new CommandDefinition("SELECT display_name FROM app_users WHERE role_id = @roleId ORDER BY display_name",
            new { roleId }, tx, cancellationToken: ct))).ToList();
        var sensitive = added.Select(a => PermissionCatalog.Find(a.Split('@')[0])).Any(d => d is { IsSensitive: true });
        var diff = new MatrixDiff(added, removed, affected, sensitive);
        if (dryRun) return diff;

        await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_role_permissions WHERE role_id = @roleId", new { roleId }, tx, cancellationToken: ct));
        foreach (var g in grants)
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO app_role_permissions (role_id, `rank`, permission) VALUES (@roleId, @Rank, @Permission)",
                new { roleId, g.Rank, g.Permission }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("UPDATE app_roles SET row_version = row_version + 1 WHERE id = @roleId", new { roleId }, tx, cancellationToken: ct));
        await GuardAdminsAsync(c, tx, actor, ct);
        await AuditAsync(c, tx, actor, ip, "role.permissions", "role", roleId, role.Name, new { removed }, new { added }, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
        return diff;
    }

    public async Task<MatrixDiff> ResetDefaultAsync(int roleId, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        RoleMatrix current;
        AppRole role;
        await using (var c = await db.AppAsync(ct))
        {
            role = await LockRoleAsync(c, null, roleId, null, ct);
            current = new RoleMatrix(Layers(await UserContextService.RoleGrantsAsync(c, null, roleId, ct)), role.RowVersion);
        }
        if (!role.IsSeeded || !DefaultPermissionMatrix.Build().TryGetValue(role.Name, out var defaults))
            throw AppError.Validation("只有系统预设角色可以恢复默认");
        return await PutMatrixAsync(roleId, Layers(defaults), current.RowVersion, false, actor, ip, ct);
    }

    // ---------- ranks ----------

    public async Task<IReadOnlyList<(string Name, int Sort, long UserCount)>> ListRanksAsync(CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        return (await c.QueryAsync<(string, int, long)>(new CommandDefinition(
            "SELECT k.name, k.sort, (SELECT COUNT(*) FROM app_users u WHERE u.`rank` = k.name) FROM app_ranks k ORDER BY k.sort",
            cancellationToken: ct))).ToList();
    }

    public async Task CreateRankAsync(string name, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        name = AdminRules.NormalizeRoleName(name);
        if (name == EffectivePermissions.AllRanks) throw AppError.Validation("职级名称不合法");
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_ranks WHERE name = @name", new { name }, tx, cancellationToken: ct)) > 0)
            throw AppError.Conflict("RANK_EXISTS", "职级已存在");
        var sort = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(MAX(sort), 0) + 1 FROM app_ranks", transaction: tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO app_ranks (name, sort) VALUES (@name, @sort)", new { name, sort }, tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "rank.create", "rank", null, name, null, new { name }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteRankAsync(string name, ICurrentUser actor, string? ip, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_users WHERE `rank` = @name", new { name }, tx, cancellationToken: ct)) > 0)
            throw AppError.Conflict("RANK_IN_USE", "仍有用户属于该职级，不能删除");
        var n = await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_ranks WHERE name = @name", new { name }, tx, cancellationToken: ct));
        if (n == 0) throw AppError.NotFound("职级");
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM app_role_permissions WHERE `rank` = @name", new { name }, tx, cancellationToken: ct));
        await AuditAsync(c, tx, actor, ip, "rank.delete", "rank", null, name, new { name }, null, ct);
        await tx.CommitAsync(ct);
        UserContextService.InvalidateAll();
    }

    // ---------- audit ----------

    public async Task<IReadOnlyList<AdminAuditEntry>> AuditLogAsync(string? actor, string? target, string? action, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        return (await c.QueryAsync<AdminAuditEntry>(new CommandDefinition("""
            SELECT id, actor_name AS ActorName, action, target_type AS TargetType, target_id AS TargetId, target_name AS TargetName,
                   before_json AS BeforeJson, after_json AS AfterJson, ip, at
            FROM app_admin_audit
            WHERE (@actor IS NULL OR actor_name LIKE CONCAT('%', @actor, '%'))
              AND (@target IS NULL OR target_name LIKE CONCAT('%', @target, '%'))
              AND (@action IS NULL OR action = @action)
              AND (@from IS NULL OR at >= @from) AND (@to IS NULL OR at < DATE_ADD(@to, INTERVAL 1 DAY))
            ORDER BY id DESC LIMIT 1000
            """, new { actor = Blank(actor), target = Blank(target), action = Blank(action), from, to }, cancellationToken: ct))).ToList();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---------- helpers ----------

    private static async Task<AppUser> LockUserAsync(MySqlConnection c, MySqlTransaction tx, int id, int? rowVersion, CancellationToken ct)
    {
        var u = await c.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(AppUser.SelectSql + " WHERE u.id = @id FOR UPDATE",
            new { id }, tx, cancellationToken: ct)) ?? throw AppError.NotFound("用户");
        if (rowVersion is { } v && v != u.RowVersion)
            throw AppError.Conflict("CONCURRENT_EDIT", "该用户已被其他管理员修改，请刷新后重试");
        return u;
    }

    private static async Task<AppRole> LockRoleAsync(MySqlConnection c, MySqlTransaction? tx, int id, int? rowVersion, CancellationToken ct)
    {
        var r = await c.QuerySingleOrDefaultAsync<AppRole>(new CommandDefinition(
            $"SELECT id, name, description, is_read_only AS IsReadOnly, is_seeded AS IsSeeded, sort, row_version AS RowVersion, CAST(0 AS SIGNED) AS UserCount FROM app_roles WHERE id = @id{(tx is null ? "" : " FOR UPDATE")}",
            new { id }, tx, cancellationToken: ct)) ?? throw AppError.NotFound("角色");
        if (rowVersion is { } v && v != r.RowVersion)
            throw AppError.Conflict("CONCURRENT_EDIT", "该角色已被其他管理员修改，请刷新后重试");
        return r;
    }

    private static Task<AppRole> RoleAsync(MySqlConnection c, MySqlTransaction tx, int id, CancellationToken ct) => LockRoleAsync(c, tx, id, null, ct);

    private static async Task RequireRankAsync(MySqlConnection c, MySqlTransaction tx, string rank, CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM app_ranks WHERE name = @rank", new { rank }, tx, cancellationToken: ct)) == 0)
            throw AppError.Validation($"未知职级：{rank}");
    }

    /// <summary>
    /// Last-admin and self-protection guards (002-spec §5.4), checked inside the transaction after the change:
    /// at least one active user must keep users:admin + roles:admin, and the acting admin must keep them too.
    /// </summary>
    private static async Task GuardAdminsAsync(MySqlConnection c, MySqlTransaction tx, ICurrentUser actor, CancellationToken ct)
    {
        var active = (await c.QueryAsync<AppUser>(new CommandDefinition(AppUser.SelectSql + " WHERE u.is_active = 1", transaction: tx, cancellationToken: ct))).ToList();
        var anyAdmin = false;
        var actorStillAdmin = false;
        foreach (var u in active)
        {
            var perms = await UserContextService.PermissionsForAsync(c, tx, u, ct);
            if (!AdminRules.IsAdmin(perms)) continue;
            anyAdmin = true;
            if (u.Id == actor.UserId) actorStillAdmin = true;
        }
        if (!anyAdmin) throw AppError.Conflict("LAST_ADMIN", "此操作会导致没有任何可用的管理员账户，已拒绝");
        if (actor.UserId > 0 && AdminRules.IsAdmin(actor.Permissions) && !actorStillAdmin)
            throw AppError.Conflict("SELF_CHANGE", "不能移除自己的用户管理/角色管理权限");
    }

    private async Task<bool> NameLockedAsync(AppUser u, CancellationToken ct)
    {
        if (u.LastLoginAt is not null) return true;
        await using var lc = await db.LegacyAsync(ct);
        var name = LegacyText.EncodeCompat(u.DisplayName);
        return await lc.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT (EXISTS(SELECT 1 FROM historys WHERE person_to = @name OR person_from = @name)
                 OR EXISTS(SELECT 1 FROM update_logs WHERE user LIKE CONCAT('%-', @name)))
            """, new { name }, cancellationToken: ct)) > 0;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static Task AuditAsync(MySqlConnection c, MySqlTransaction tx, ICurrentUser actor, string? ip, string action, string targetType,
        int? targetId, string? targetName, object? before, object? after, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO app_admin_audit (actor_user_id, actor_name, action, target_type, target_id, target_name, before_json, after_json, ip)
            VALUES (@actorId, @actorName, @action, @targetType, @targetId, @targetName, @before, @after, @ip)
            """, new
        {
            actorId = actor.UserId > 0 ? actor.UserId : (int?)null, actorName = actor.DisplayName, action, targetType, targetId, targetName,
            before = before is null ? null : JsonSerializer.Serialize(before, Json),
            after = after is null ? null : JsonSerializer.Serialize(after, Json), ip,
        }, tx, cancellationToken: ct));
}
