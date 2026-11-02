using Dapper;
using Goldata.Application.Documents;
using Goldata.Application.Modules;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Goldata.Infrastructure.Legacy;

public sealed record FileEntry(long Id, string Tag, string SlotLabel, string Name, string Path, string Timestamp, bool Exists, long? Size);

public sealed record OpenedFile(string FullPath, string Name, string ContentType);

public enum ConflictMode { Reject, Rename }

/// <summary>
/// Attachments in the shared <c>pdf_folder</c> + <c>file_path</c> table (002-spec §4, REQ-DOC-*).
/// Paths are stored exactly like legacy: <c>pdf_folder/{folder}/{yyyyMM}/{name}</c>.
/// </summary>
public sealed class FileService(Db db, RecordService records, IOptions<DocumentOptions> options, TimeProvider clock, ILogger<FileService> log)
{
    private readonly DocumentOptions _o = options.Value;

    public async Task<IReadOnlyList<FileEntry>> ListAsync(string moduleKey, long recordId, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(moduleKey, user, ct: ct);
        if (m.Files.Length == 0) return [];
        await using var c = await db.LegacyAsync(ct);
        var rows = await c.QueryAsync<(long Id, string Tag, string Path, DateTime Timestamp)>(new CommandDefinition(
            "SELECT CAST(id AS SIGNED) AS id, table_name, file_path, timestamp FROM file_path WHERE table_name IN @tags AND table_id = @recordId ORDER BY id",
            new { tags = m.Files.Select(f => f.Tag).ToArray(), recordId }, cancellationToken: ct));
        return rows.Select(r =>
        {
            var full = DocumentPaths.ResolveUnderRoot(_o.RootPath, r.Path);
            var info = full is null ? null : new FileInfo(full);
            var slot = m.Files.FirstOrDefault(f => f.Tag == r.Tag);
            return new FileEntry(r.Id, r.Tag, slot?.Label ?? "附件", Path.GetFileName(r.Path), r.Path,
                r.Timestamp.ToString("yyyy-MM-dd HH:mm"), info?.Exists ?? false, info is { Exists: true } ? info.Length : null);
        }).ToList();
    }

    public async Task<OpenedFile> OpenAsync(long fileId, ICurrentUser user, CancellationToken ct)
    {
        var (tag, _, path) = await LoadRowAsync(fileId, ct);
        var (module, _) = ModuleRegistry.FindByFileTag(tag) ?? throw AppError.NotFound("附件");
        await records.AuthorizeAsync(module.Key, user, ct: ct);
        var full = DocumentPaths.ResolveUnderRoot(_o.RootPath, path) ?? throw new AppError(404, "FILE_MISSING", "附件路径无效");
        if (!File.Exists(full)) throw new AppError(404, "FILE_MISSING", "附件文件不存在");
        var name = Path.GetFileName(full);
        return new OpenedFile(full, name, DocumentPaths.ContentType(name));
    }

    public async Task<FileEntry> UploadAsync(string moduleKey, long recordId, string? slotTag, string fileName, long length,
        Stream content, ConflictMode mode, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(moduleKey, user, "upload", ct);
        var slot = (slotTag is null ? m.Files.FirstOrDefault() : m.Files.FirstOrDefault(f => f.Tag == slotTag))
                   ?? throw AppError.Validation("该模块没有此附件类别");
        if (length <= 0) throw AppError.Validation("文件为空");
        if (length > _o.MaxBytes) throw AppError.Validation($"文件不能超过 {_o.MaxBytes / 1024 / 1024} MB");
        var name = DocumentPaths.NormalizeFileName(fileName);
        await EnsureRecordExistsAsync(m, recordId, ct);

        var relative = DocumentPaths.BuildRelativePath(slot.Folder, name, clock.GetUtcNow());
        var full = DocumentPaths.ResolveUnderRoot(_o.RootPath, relative) ?? throw AppError.Validation("文件路径不合法");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        if (File.Exists(full) || await PathInUseAsync(relative, ct))
        {
            var n = 2;
            string Candidate() => DocumentPaths.BuildRelativePath(slot.Folder, DocumentPaths.Suggest(name, n), clock.GetUtcNow());
            while (File.Exists(DocumentPaths.ResolveUnderRoot(_o.RootPath, Candidate())!) || await PathInUseAsync(Candidate(), ct)) n++;
            if (mode == ConflictMode.Reject)
                throw AppError.Conflict("FILE_EXISTS", "同名文件已存在", new { suggestedName = DocumentPaths.Suggest(name, n) });
            name = DocumentPaths.Suggest(name, n);
            relative = Candidate();
            full = DocumentPaths.ResolveUnderRoot(_o.RootPath, relative)!;
        }

        // Write to a temp file in the same folder, check the signature, then rename (atomic on one volume).
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $".upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await content.CopyToAsync(fs, ct);
            var head = new byte[8];
            await using (var check = File.OpenRead(temp)) _ = await check.ReadAsync(head, ct);
            if (!DocumentPaths.LooksLike(name, head)) throw AppError.Validation("文件内容与扩展名不符");
            if (new FileInfo(temp).Length > _o.MaxBytes) throw AppError.Validation($"文件不能超过 {_o.MaxBytes / 1024 / 1024} MB");
            File.Move(temp, full, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        try
        {
            await using var c = await db.LegacyAsync(ct);
            await using var tx = await c.BeginAsync(ct);
            var values = new Dictionary<string, object?> { ["table_name"] = slot.Tag, ["table_id"] = recordId, ["file_path"] = relative };
            var id = await c.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO file_path (table_name, table_id, file_path) VALUES (@table_name, @table_id, @file_path);
                SELECT LAST_INSERT_ID();
                """, values, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, "file_path", id, LegacyAuditWriter.Render("INSERT", "file_path", values), user,
                LegacyAuditWriter.Added, ct);
            await tx.CommitAsync(ct);
            var info = new FileInfo(full);
            return new FileEntry(id, slot.Tag, slot.Label, name, relative, clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm"), true, info.Length);
        }
        catch
        {
            File.Delete(full);   // DB failed: don't leave an orphan file (002-spec §4.3 step 6)
            throw;
        }
    }

    /// <summary>Moves the file to <c>pdf_folder/_recycle/{yyyyMMdd}/</c> and removes its <c>file_path</c> row (Q9: recycle by default).</summary>
    public async Task DeleteAsync(long fileId, ICurrentUser user, CancellationToken ct)
    {
        var (tag, tableId, path) = await LoadRowAsync(fileId, ct);
        var (module, _) = ModuleRegistry.FindByFileTag(tag) ?? throw AppError.NotFound("附件");
        await records.AuthorizeAsync(module.Key, user, "file_delete", ct);

        var full = DocumentPaths.ResolveUnderRoot(_o.RootPath, path);
        string? recycled = null;
        if (full is not null && File.Exists(full))
        {
            var day = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), DocumentPaths.ChinaTime).ToString("yyyyMMdd");
            var dir = Path.Combine(Path.GetFullPath(_o.RootPath), DocumentPaths.Root, DocumentPaths.RecycleFolder, day);
            Directory.CreateDirectory(dir);
            recycled = Path.Combine(dir, $"{fileId}_{Path.GetFileName(full)}");
            File.Move(full, recycled, overwrite: true);
        }

        try
        {
            await using var c = await db.LegacyAsync(ct);
            await using var tx = await c.BeginAsync(ct);
            await c.ExecuteAsync(new CommandDefinition("DELETE FROM file_path WHERE id = @fileId", new { fileId }, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, "file_path", fileId, LegacyAuditWriter.Render("DELETE", "file_path", [], $"id = {fileId}") +
                $" /* {tag} #{tableId}: {path} */", user, LegacyAuditWriter.Deleted, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (recycled is not null && full is not null) File.Move(recycled, full, overwrite: false);
            throw;
        }
        log.LogInformation("File {FileId} recycled by {User}", fileId, user.Login);
    }

    private async Task<(string Tag, long TableId, string Path)> LoadRowAsync(long fileId, CancellationToken ct)
    {
        await using var c = await db.LegacyAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<(string Tag, long? TableId, string Path)>(new CommandDefinition(
            "SELECT table_name, CAST(table_id AS SIGNED) AS table_id, file_path FROM file_path WHERE id = @fileId", new { fileId }, cancellationToken: ct));
        if (row.Tag is null) throw AppError.NotFound("附件");
        return (row.Tag, row.TableId ?? 0, row.Path ?? "");
    }

    private async Task<bool> PathInUseAsync(string relative, CancellationToken ct)
    {
        await using var c = await db.LegacyAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM file_path WHERE file_path = @relative", new { relative }, cancellationToken: ct)) > 0;
    }

    private async Task EnsureRecordExistsAsync(ModuleDefinition m, long id, CancellationToken ct)
    {
        await using var c = await db.LegacyAsync(ct);
        var n = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM {Application.Records.ListQueryBuilder.Q(m.Table)} WHERE `id` = @id", new { id }, cancellationToken: ct));
        if (n == 0) throw AppError.NotFound();
    }
}
