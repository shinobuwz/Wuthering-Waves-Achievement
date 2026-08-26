using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>Reads and writes the native 12-column shape and imports the v1 7-column crawler .xlsx shape without mutating legacy files.</summary>
public sealed class ExcelAchievementExchange : IAchievementImportSource, IAchievementExportSink
{
    private static readonly string[] Columns = ["绝对编号", "版本", "第一分类", "第二分类", "编号", "名称", "描述", "奖励", "是否隐藏", "获取状态", "成就组ID", "互斥成就"];
    private static readonly string[] LegacyCrawlerColumns = ["名称", "描述", "奖励", "版本", "是否隐藏", "第一分类", "第二分类"];
    private readonly string _path;

    public ExcelAchievementExchange(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("An Excel path is required.", nameof(path))
            : Path.GetFullPath(path);
    }

    public async Task<ExchangePayload> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (Path.GetExtension(_path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadXlsxAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReadTsvAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        if (Path.GetExtension(_path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            await WriteXlsxAsync(state, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteTsvAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExchangePayload> ReadTsvAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        var cellsByLine = lines.Select(line => line.Split('\t')).ToArray();
        var headerIndex = FindHeaderIndex(cellsByLine, Columns);
        var isLegacyCrawler = false;
        if (headerIndex < 0)
        {
            headerIndex = FindHeaderIndex(cellsByLine, LegacyCrawlerColumns);
            isLegacyCrawler = headerIndex >= 0;
        }

        if (headerIndex < 0)
        {
            throw new InvalidDataException("TSV exchange is missing a supported 12-column or legacy 7-column header.");
        }

        var schema = isLegacyCrawler ? LegacyCrawlerColumns : Columns;
        var headers = cellsByLine[headerIndex];
        var indices = schema.ToDictionary(column => column, column => Array.IndexOf(headers, column), StringComparer.Ordinal);
        var rows = new List<Achievement>();
        var statuses = new Dictionary<string, ProgressStatus>(StringComparer.Ordinal);
        for (var index = headerIndex + 1; index < cellsByLine.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            var cells = cellsByLine[index];
            string Get(string name) => indices[name] < cells.Length ? cells[indices[name]].Trim() : string.Empty;
            if (isLegacyCrawler) AddLegacyCrawlerRow(rows, statuses, index + 1, Get);
            else AddRow(rows, statuses, index + 1, Get);
        }
        return new ExchangePayload(ExchangeDocumentKind.Excel, rows, statuses);
    }

    private async Task<ExchangePayload> ReadXlsxAsync(CancellationToken cancellationToken)
    {
        await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException("XLSX workbook has no first worksheet.");
        var sharedStrings = ReadSharedStrings(archive);
        XDocument document;
        await using (var stream = sheetEntry.Open())
        {
            document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = document.Descendants(ns + "row")
            .Select(row => row.Elements(ns + "c").ToDictionary(cell => ColumnIndex((string?)cell.Attribute("r") ?? ""), cell => CellValue(cell, ns, sharedStrings)))
            .ToArray();
        var headerIndex = Array.FindIndex(rows, row => HasHeader(row.Values, Columns));
        var isLegacyCrawler = false;
        if (headerIndex < 0)
        {
            headerIndex = Array.FindIndex(rows, row => HasHeader(row.Values, LegacyCrawlerColumns));
            isLegacyCrawler = headerIndex >= 0;
        }

        if (headerIndex < 0)
        {
            throw new InvalidDataException("XLSX worksheet is missing a supported 12-column or legacy 7-column header.");
        }

        var schema = isLegacyCrawler ? LegacyCrawlerColumns : Columns;
        var headers = rows[headerIndex];
        var indices = schema.ToDictionary(column => column, column => headers.First(pair => pair.Value == column).Key, StringComparer.Ordinal);
        var achievements = new List<Achievement>();
        var statuses = new Dictionary<string, ProgressStatus>(StringComparer.Ordinal);
        for (var rowIndex = headerIndex + 1; rowIndex < rows.Length; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[rowIndex];
            string Get(string name) => row.TryGetValue(indices[name], out var value) ? value.Trim() : string.Empty;
            if (row.Count == 0 || row.Values.All(string.IsNullOrWhiteSpace)) continue;
            if (isLegacyCrawler) AddLegacyCrawlerRow(achievements, statuses, rowIndex + 1, Get);
            else AddRow(achievements, statuses, rowIndex + 1, Get);
        }
        return new ExchangePayload(ExchangeDocumentKind.Excel, achievements, statuses);
    }

    private async Task WriteTsvAsync(WorkspaceState state, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(_path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync(string.Join('\t', Columns)).ConfigureAwait(false);
        foreach (var item in state.Achievements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = Values(item, state.Statuses[item.Id]);
            await writer.WriteLineAsync(string.Join('\t', values.Select(Escape))).ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteXlsxAsync(WorkspaceState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
        WriteEntry(archive, "_rels/.rels", RootRelationshipsXml);
        WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
        WriteEntry(archive, "xl/styles.xml", StylesXml);
        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildSheetXml(state));
        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddRow(List<Achievement> rows, Dictionary<string, ProgressStatus> statuses, int rowNumber, Func<string, string> get)
    {
        var code = get("编号");
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidDataException($"Row {rowNumber} has no 编号.");
        foreach (var required in new[] { "版本", "第一分类", "第二分类", "名称", "描述" })
        {
            if (string.IsNullOrWhiteSpace(get(required))) throw new InvalidDataException($"Row {rowNumber} has no required field {required}.");
        }
        var statusText = get("获取状态");
        if (!ProgressStatusText.TryParseChinese(statusText, out var status)) throw new InvalidDataException($"Row {rowNumber} has an invalid 获取状态.");
        var achievement = new Achievement(
            AchievementId.FromLegacyCode(code),
            code,
            int.TryParse(get("绝对编号"), out var order) ? order : rows.Count + 1,
            get("版本"),
            get("第一分类"),
            get("第二分类"),
            get("名称"),
            get("描述"),
            get("奖励"),
            get("是否隐藏") is "隐藏" or "是" or "true",
            NullIfBlank(get("成就组ID")),
            MutualExclusionCodes: ParseMutualCodes(get("互斥成就")));
        rows.Add(achievement);
        statuses[code] = status;
    }

    private static void AddLegacyCrawlerRow(List<Achievement> rows, Dictionary<string, ProgressStatus> statuses, int rowNumber, Func<string, string> get)
    {
        var rawName = get("名称");
        var name = rawName.Replace("「隐藏成就」", string.Empty, StringComparison.Ordinal).Trim();
        var description = get("描述");
        var version = NormalizeLegacyVersion(get("版本"));
        var firstCategory = get("第一分类");
        var secondCategory = get("第二分类");
        var reward = NormalizeLegacyReward(get("奖励"));
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Row {rowNumber} has no required field 名称.");
        foreach (var (value, field) in new[]
        {
            (version, "版本"),
            (firstCategory, "第一分类"),
            (secondCategory, "第二分类"),
            (description, "描述")
        })
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Row {rowNumber} has no required field {field}.");
        }

        var isHidden = rawName.Contains("隐藏成就", StringComparison.Ordinal) || IsLegacyHidden(get("是否隐藏"));
        var code = CreateLegacyCrawlerCode(version, firstCategory, secondCategory, name, description, reward, isHidden);
        var achievement = new Achievement(
            AchievementId.FromLegacyCode(code),
            code,
            rows.Count + 1,
            version,
            firstCategory,
            secondCategory,
            name,
            description,
            reward,
            isHidden);
        rows.Add(achievement);
        statuses[code] = ProgressStatus.Incomplete;
    }

    private static string NormalizeLegacyVersion(string value)
    {
        var trimmed = value.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return trimmed;
        return parsed == decimal.Truncate(parsed)
            ? parsed.ToString("0.0", CultureInfo.InvariantCulture)
            : parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeLegacyReward(string value)
    {
        var trimmed = value.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return trimmed;
        return parsed == decimal.Truncate(parsed)
            ? $"星声*{parsed.ToString("0", CultureInfo.InvariantCulture)}"
            : $"星声*{parsed.ToString(CultureInfo.InvariantCulture)}";
    }

    private static bool IsLegacyHidden(string value) =>
        value.Trim() is "隐藏" or "是" or "true" or "True" or "TRUE";

    private static string CreateLegacyCrawlerCode(
        string version,
        string firstCategory,
        string secondCategory,
        string name,
        string description,
        string reward,
        bool isHidden)
    {
        var identity = string.Join("\u001f", version, firstCategory, secondCategory, name, description, reward, isHidden ? "1" : "0");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"legacy-{hash[..16]}";
    }

    private static string[] Values(Achievement item, ProgressStatus status) => [
        item.AbsoluteOrder.ToString(CultureInfo.InvariantCulture), item.Version, item.FirstCategory, item.SecondCategory, item.LegacyCode,
        item.Name, item.Description, item.Reward, item.IsHidden ? "隐藏" : "", status.ToChinese(), item.GroupId ?? "", string.Join(",", item.EffectiveMutualExclusionCodes)];

    private static string Escape(string value) => value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static IReadOnlyList<string> ParseMutualCodes(string value) => value.Split([',', ';', '，', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int FindHeaderIndex(IReadOnlyList<string[]> rows, IReadOnlyList<string> columns)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (HasHeader(rows[index], columns)) return index;
        }

        return -1;
    }

    private static bool HasHeader(IEnumerable<string> values, IReadOnlyList<string> columns) =>
        columns.All(column => values.Contains(column, StringComparer.Ordinal));

    private static Dictionary<int, string> ReadSharedStrings(ZipArchive archive)
    {
        var result = new Dictionary<int, string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return result;
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var index = 0;
        foreach (var item in document.Descendants(ns + "si"))
        {
            result[index++] = string.Concat(item.Descendants(ns + "t").Select(text => text.Value));
        }
        return result;
    }

    private static string CellValue(XElement cell, XNamespace ns, IReadOnlyDictionary<int, string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return string.Concat(cell.Descendants(ns + "t").Select(item => item.Value));
        var value = cell.Element(ns + "v")?.Value ?? string.Empty;
        return type == "s" && int.TryParse(value, out var index) && sharedStrings.TryGetValue(index, out var shared) ? shared : value;
    }

    private static int ColumnIndex(string cellReference)
    {
        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
        var value = 0;
        foreach (var letter in letters) value = value * 26 + char.ToUpperInvariant(letter) - 'A' + 1;
        return value - 1;
    }

    private static string BuildSheetXml(WorkspaceState state)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetData = new XElement(ns + "sheetData");
        var allRows = new List<string[]> { Columns };
        allRows.AddRange(state.Achievements.Select(item => Values(item, state.Statuses[item.Id])));
        for (var rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
        {
            var row = new XElement(ns + "row", new XAttribute("r", rowIndex + 1));
            for (var columnIndex = 0; columnIndex < allRows[rowIndex].Length; columnIndex++)
            {
                var cellRef = ColumnName(columnIndex) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
                row.Add(new XElement(ns + "c", new XAttribute("r", cellRef), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", allRows[rowIndex][columnIndex]))));
            }
            sheetData.Add(row);
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(ns + "worksheet", sheetData)).ToString(SaveOptions.DisableFormatting);
    }

    private static string ColumnName(int index)
    {
        var result = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26) result = (char)('A' + (value - 1) % 26) + result;
        return result;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ContentTypesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>";
    private const string RootRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
    private const string WorkbookXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Achievements\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
    private const string WorkbookRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    private const string StylesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"1\"><xf/></cellXfs></styleSheet>";
}
