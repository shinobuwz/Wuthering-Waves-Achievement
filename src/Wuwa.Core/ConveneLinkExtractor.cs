using System.Text;
namespace Wuwa.Core;

/// <summary>
/// Extracts the temporary Convene history URL from a Wuthering Waves Client.log.
/// Recent game versions apply a byte LUT to the log body; older versions may be plain text.
/// </summary>
public static partial class ConveneLinkExtractor
{
    private const byte SchemeAFingerprint1 = 0x54;
    private const byte SchemeAFingerprint2 = 0x50;
    private const byte SchemeBHeader1 = 0x4C;
    private const byte SchemeBHeader2 = 0x4F;

    private static readonly string[] PlaintextMarkers =
    [
        "Log file open",
        "[GameThread]",
        "Wuthering Waves",
        "KuroSdk",
        "aki-gm-resources"
    ];

    private static readonly string[] GachaRecordUrlPrefixes =
    [
        "https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record",
        "https://aki-gm-resources.aki-game.net/aki/gacha/index.html#/record",
        "https://aki-gm-resources-oversea.aki-game.com/aki/gacha/index.html#/record",
        "https://aki-gm-resources-oversea.aki-game.net/aki/gacha/index.html#/record"
    ];

    /// <summary>
    /// Returns the most recent gacha record URL in the supplied raw Client.log bytes.
    /// </summary>
    public static string? Extract(ReadOnlySpan<byte> rawLog)
    {
        if (rawLog.IsEmpty)
        {
            return null;
        }

        if (rawLog.Length >= 3 && rawLog[1] == SchemeAFingerprint1 && rawLog[2] == SchemeAFingerprint2)
        {
            return ExtractUrl(DecodeSchemeA(rawLog[3..]));
        }

        if (rawLog.Length >= 3 && rawLog[0] == 0 && rawLog[1] == SchemeBHeader1 && rawLog[2] == SchemeBHeader2)
        {
            return ExtractUrl(DecodeSchemeB(rawLog[3..]));
        }

        // Some current Client.log files have a variant header that does not match
        // the two public fingerprints. Try the known transforms and offsets.
        var candidates = new[]
        {
            ExtractUrl(DecodeSchemeA(rawLog)),
            ExtractUrl(DecodeSchemeA(rawLog[3..])),
            ExtractUrl(DecodeSchemeB(rawLog)),
            ExtractUrl(DecodeSchemeB(rawLog[3..])),
            ExtractUrl(Encoding.UTF8.GetString(rawLog))
        };

        return candidates.FirstOrDefault(result => !string.IsNullOrWhiteSpace(result));
    }

    public static string? ExtractFromText(string text) => ExtractUrl(text);

    private static string? ExtractUrl(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var markerCount = PlaintextMarkers.Count(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        string? latest = null;

        foreach (var prefix in GachaRecordUrlPrefixes)
        {
            var searchStart = 0;
            while (searchStart < text.Length)
            {
                var start = text.IndexOf(prefix, searchStart, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    break;
                }

                var end = text.IndexOfAny(
                    [' ', '\r', '\n', '\t', '"', '\'', ']', '}', '>', ','],
                    start + prefix.Length);
                if (end < 0)
                {
                    end = text.Length;
                }

                latest = text[start..end].TrimEnd(',', '}', ']');
                searchStart = end;
            }
        }

        return markerCount > 0 || latest is not null ? latest : null;
    }

    private static string DecodeSchemeA(ReadOnlySpan<byte> encrypted)
    {
        var decoded = new byte[encrypted.Length];
        for (var i = 0; i < encrypted.Length; i++)
        {
            var value = encrypted[i];
            decoded[i] = (byte)(value ^ ((value & 1) == 1 ? 0xA5 : 0xEF));
        }

        return Encoding.UTF8.GetString(decoded);
    }

    private static string DecodeSchemeB(ReadOnlySpan<byte> encrypted)
    {
        var decoded = new byte[encrypted.Length];
        for (var i = 0; i < encrypted.Length; i++)
        {
            decoded[i] = (byte)(encrypted[i] ^ 0x55);
        }

        return Encoding.UTF8.GetString(decoded);
    }

}
