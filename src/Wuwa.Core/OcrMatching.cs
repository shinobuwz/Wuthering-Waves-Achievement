using System.Text.RegularExpressions;

namespace Wuwa.Core;

public sealed record OcrAchievementCandidate(
    AchievementId AchievementId,
    string LegacyCode,
    string MatchedName,
    string OcrText,
    double MatchConfidence,
    ProgressStatus? ProposedStatus,
    string? StatusText,
    bool IsAmbiguous = false);

public sealed record OcrUnmatchedText(string Text, string Reason, float OcrScore);

public sealed record OcrScanPreview(
    IReadOnlyList<OcrAchievementCandidate> Candidates,
    IReadOnlyList<OcrUnmatchedText> Unmatched,
    int CompletedCount,
    int IncompleteCount,
    int UnknownStatusCount);

public sealed record OcrApplyResult(
    bool IsSuccess,
    WorkspaceSnapshot Snapshot,
    int Updated,
    int Unchanged,
    int PreventedDowngrades,
    WorkspaceError? Error = null);

public static partial class AchievementOcrMatcher
{
    private const double MaximumDistanceRatio = 0.4;

    public static OcrScanPreview CreatePreview(
        IReadOnlyList<OcrTextLine> lines,
        IReadOnlyList<AchievementRow> achievements)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(achievements);
        var statusLines = lines.Select(line => (Line: line, Status: ParseStatus(line.Text)))
            .Where(item => item.Status is not null)
            .ToArray();
        var candidates = new List<OcrAchievementCandidate>();
        var unmatched = new List<OcrUnmatchedText>();
        var consumedLineIndexes = new HashSet<int>();
        var wangRiJinzhouRows = achievements
            .Where(BuiltInAchievementRules.IsWangRiJinzhou)
            .OrderBy(row => row.AbsoluteOrder)
            .ToArray();

        // The game displays one shared title for these three progression rows.
        // Resolve only this built-in exception from its nearby description; all
        // other OCR matching continues through the existing name-only path.
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (ParseStatus(line.Text) is not null || string.IsNullOrWhiteSpace(line.Text) ||
                !BuiltInAchievementRules.IsWangRiJinzhouOcrName(line.Text)) continue;

            consumedLineIndexes.Add(index);
            var descriptionMatch = FindWangRiJinzhouDescription(index, line, lines, wangRiJinzhouRows, consumedLineIndexes);
            if (descriptionMatch is null)
            {
                unmatched.Add(new OcrUnmatchedText(line.Text, "往日之音·今州未匹配到成就描述", line.Score));
                continue;
            }

            consumedLineIndexes.Add(descriptionMatch.Value.Index);
            var status = FindNearestStatus(line, statusLines);
            candidates.Add(new OcrAchievementCandidate(
                descriptionMatch.Value.Row.Id,
                descriptionMatch.Value.Row.LegacyCode,
                descriptionMatch.Value.Row.Name,
                line.Text,
                descriptionMatch.Value.Confidence,
                status.Status,
                status.Text));
        }

        foreach (var (index, line) in lines.Select((line, index) => (index, line)))
        {
            if (consumedLineIndexes.Contains(index)) continue;
            if (ParseStatus(line.Text) is not null || string.IsNullOrWhiteSpace(line.Text)) continue;
            var match = Match(line.Text, achievements);
            if (match.Row is null)
            {
                unmatched.Add(new OcrUnmatchedText(line.Text, match.IsAmbiguous ? "存在多个同等候选" : "未达到名称匹配阈值", line.Score));
                continue;
            }
            var status = FindNearestStatus(line, statusLines);
            candidates.Add(new OcrAchievementCandidate(
                match.Row.Id,
                match.Row.LegacyCode,
                match.Row.Name,
                line.Text,
                match.Confidence,
                status.Status,
                status.Text,
                match.IsAmbiguous));
        }

        var deduplicated = candidates
            .GroupBy(candidate => candidate.AchievementId)
            .Select(group => group
                .OrderByDescending(candidate => candidate.ProposedStatus == ProgressStatus.Completed)
                .ThenByDescending(candidate => candidate.MatchConfidence)
                .First())
            .OrderBy(candidate => achievements.First(row => row.Id == candidate.AchievementId).AbsoluteOrder)
            .ToArray();
        return new OcrScanPreview(
            Array.AsReadOnly(deduplicated),
            Array.AsReadOnly(unmatched.ToArray()),
            deduplicated.Count(candidate => candidate.ProposedStatus == ProgressStatus.Completed),
            deduplicated.Count(candidate => candidate.ProposedStatus == ProgressStatus.Incomplete),
            deduplicated.Count(candidate => candidate.ProposedStatus is null));
    }

    public static OcrAchievementCandidate CreateTargetedSearchCandidate(
        IReadOnlyList<OcrTextLine> lines,
        AchievementRow achievement)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(achievement);
        var nameLines = lines
            .Where(line => line.Kind == OcrTextKind.AchievementName)
            .ToArray();
        if (nameLines.Length == 0)
        {
            nameLines = lines
                .Where(line => ParseStatus(line.Text) is null && line.Kind != OcrTextKind.AchievementDescription)
                .ToArray();
        }

        var bestName = nameLines
            .Select(line =>
            {
                MatchKnownText(line.Text, [achievement.Name], out var confidence);
                return (Line: line, Confidence: confidence);
            })
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.Line.Score)
            .FirstOrDefault();
        var status = lines
            .Select(line => (Line: line, Status: ParseStatus(line.Text)))
            .Where(item => item.Status is not null)
            .OrderByDescending(item => item.Line.Kind == OcrTextKind.AchievementStatus)
            .ThenByDescending(item => item.Line.Score)
            .FirstOrDefault();
        var ocrText = bestName.Line?.Text;
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            ocrText = string.Join("；", lines.Select(line => line.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        return new OcrAchievementCandidate(
            achievement.Id,
            achievement.LegacyCode,
            achievement.Name,
            string.IsNullOrWhiteSpace(ocrText) ? "未识别到有效文字" : ocrText,
            bestName.Confidence,
            status.Status,
            status.Line?.Text,
            IsAmbiguous: false);
    }

    public static ProgressStatus? ParseStatus(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = text.Trim();
        if (CompletedDateRegex().IsMatch(normalized) || normalized.Contains("已完成", StringComparison.Ordinal)) return ProgressStatus.Completed;
        if (normalized.Contains("进行中", StringComparison.Ordinal) || normalized.Contains("进行", StringComparison.Ordinal) || normalized.Contains("未完成", StringComparison.Ordinal)) return ProgressStatus.Incomplete;
        return null;
    }

    public static string NormalizeName(string value) =>
        new(value.Trim().Select(character => character switch
        {
            '・' or '•' or '∙' or '･' => '·',
            _ => character
        }).Where(character => !char.IsWhiteSpace(character)).ToArray());

    public static string? MatchKnownText(
        string text,
        IReadOnlyCollection<string> knownNames,
        out double confidence)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(knownNames);
        confidence = 0;
        var normalized = NormalizeName(text);
        if (normalized.Length == 0 || knownNames.Count == 0) return null;
        var scored = knownNames.Select(name =>
        {
            var target = NormalizeName(name);
            var distance = EditDistance(normalized, target);
            var length = Math.Max(normalized.Length, target.Length);
            var score = length == 0 ? 1d : 1d - distance / (double)length;
            return (Name: name, Distance: distance, Length: length, Confidence: score);
        }).OrderBy(item => item.Distance).ThenByDescending(item => item.Confidence).ToArray();
        var best = scored[0];
        confidence = best.Confidence;
        return best.Distance <= Math.Max(best.Length * MaximumDistanceRatio, 0)
            ? best.Name
            : null;
    }

    private static (AchievementRow Row, double Confidence, int Index)? FindWangRiJinzhouDescription(
        int titleIndex,
        OcrTextLine titleLine,
        IReadOnlyList<OcrTextLine> lines,
        IReadOnlyList<AchievementRow> rows,
        IReadOnlySet<int> consumedLineIndexes)
    {
        if (rows.Count == 0 || titleLine.Points.Count == 0) return null;
        MatchKnownText(titleLine.Text, [BuiltInAchievementRules.GetOcrName(rows[0])], out var titleConfidence);
        var titleCenter = titleLine.Points.Average(point => point.Y);
        (AchievementRow Row, double Confidence, int Index, double Distance)? best = null;

        for (var index = 0; index < lines.Count; index++)
        {
            if (index == titleIndex || consumedLineIndexes.Contains(index)) continue;
            var line = lines[index];
            if (line.Points.Count == 0 || string.IsNullOrWhiteSpace(line.Text) || ParseStatus(line.Text) is not null) continue;
            if (line.Kind is not OcrTextKind.Unknown and not OcrTextKind.AchievementDescription) continue;

            var distance = Math.Abs(line.Points.Average(point => point.Y) - titleCenter);
            if (distance > 90) continue;
            foreach (var row in rows)
            {
                if (MatchKnownText(line.Text, [row.Description], out var descriptionConfidence) is null) continue;
                var confidence = titleConfidence * 0.35 + descriptionConfidence * 0.65;
                if (best is null || confidence > best.Value.Confidence ||
                    confidence == best.Value.Confidence && distance < best.Value.Distance)
                {
                    best = (row, confidence, index, distance);
                }
            }
        }

        return best is null ? null : (best.Value.Row, best.Value.Confidence, best.Value.Index);
    }

    private static (AchievementRow? Row, double Confidence, bool IsAmbiguous) Match(
        string text,
        IReadOnlyList<AchievementRow> achievements)
    {
        var normalized = NormalizeName(text);
        if (normalized.Length == 0) return (null, 0, false);
        var scored = achievements.Select(row =>
        {
            var target = NormalizeName(row.Name);
            var distance = EditDistance(normalized, target);
            var length = Math.Max(normalized.Length, target.Length);
            var confidence = length == 0 ? 1.0 : 1.0 - distance / (double)length;
            return (Row: row, Distance: distance, Length: length, Confidence: confidence);
        }).OrderBy(item => item.Distance).ThenByDescending(item => item.Confidence).ToArray();
        if (scored.Length == 0) return (null, 0, false);
        var best = scored[0];
        var ambiguous = scored.Skip(1).Any(item => item.Distance == best.Distance && item.Confidence == best.Confidence);
        if (ambiguous || best.Distance > Math.Max(best.Length * MaximumDistanceRatio, 0)) return (null, best.Confidence, ambiguous);
        return (best.Row, best.Confidence, false);
    }

    private static (ProgressStatus? Status, string? Text) FindNearestStatus(
        OcrTextLine nameLine,
        IReadOnlyList<(OcrTextLine Line, ProgressStatus? Status)> statusLines)
    {
        if (statusLines.Count == 0 || nameLine.Points.Count == 0) return (null, null);
        var nameCenter = nameLine.Points.Average(point => point.Y);
        var nameHeight = nameLine.Points.Max(point => point.Y) - nameLine.Points.Min(point => point.Y);
        var nearest = statusLines.Select(item =>
            {
                var statusCenter = item.Line.Points.Count == 0 ? float.MaxValue : item.Line.Points.Average(point => point.Y);
                return (item.Line, item.Status, Distance: Math.Abs(statusCenter - nameCenter));
            })
            .Where(item => item.Distance <= Math.Max(24.0, nameHeight * 2.0))
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        return nearest.Line is null ? (null, null) : (nearest.Status, nearest.Line.Text);
    }

    private static int EditDistance(string left, string right)
    {
        if (left.Length < right.Length) return EditDistance(right, left);
        if (right.Length == 0) return left.Length;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            var current = new int[right.Length + 1];
            current[0] = leftIndex + 1;
            for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
            {
                current[rightIndex + 1] = Math.Min(
                    Math.Min(previous[rightIndex + 1] + 1, current[rightIndex] + 1),
                    previous[rightIndex] + (left[leftIndex] == right[rightIndex] ? 0 : 1));
            }
            previous = current;
        }
        return previous[^1];
    }

    [GeneratedRegex(@"\d{4}[/\-.]\d{1,2}[/\-.]\d{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex CompletedDateRegex();
}
