namespace Goldata.Domain;

/// <summary>
/// A business error that maps to an RFC 9457 Problem Details response (<c>code</c> + zh-CN <c>title</c>).
/// </summary>
public sealed class AppError(int status, string code, string title, object? detail = null) : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public object? Detail { get; } = detail;

    public static AppError NotFound(string what = "记录") => new(404, "NOT_FOUND", $"{what}不存在");
    public static AppError Forbidden() => new(403, "FORBIDDEN", "权限不足");
    public static AppError Validation(string title, object? detail = null) => new(400, "VALIDATION", title, detail);
    public static AppError Conflict(string code, string title, object? detail = null) => new(409, code, title, detail);
}
