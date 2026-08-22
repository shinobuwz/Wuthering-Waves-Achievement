namespace Wuwa.Core;

public enum ExchangeDocumentKind
{
    ProgressJson,
    FullJson,
    Excel
}

public sealed record ExchangeDiagnostic(string Code, string Message, int? Row = null, string? Field = null);

public sealed record ExchangePayload(
    ExchangeDocumentKind Kind,
    IReadOnlyList<Achievement> Achievements,
    IReadOnlyDictionary<string, ProgressStatus> Progress);

public interface IAchievementImportSource
{
    Task<ExchangePayload> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IAchievementExportSink
{
    Task WriteAsync(WorkspaceState state, CancellationToken cancellationToken = default);
}

public sealed record ExchangeImportResult(
    bool IsSuccess,
    WorkspaceSnapshot Snapshot,
    ExchangeDocumentKind? Kind = null,
    IReadOnlyList<ExchangeDiagnostic>? Diagnostics = null,
    WorkspaceError? Error = null)
{
    public IReadOnlyList<ExchangeDiagnostic> EffectiveDiagnostics => Diagnostics ?? Array.Empty<ExchangeDiagnostic>();
}
