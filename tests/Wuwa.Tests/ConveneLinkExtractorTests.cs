using System.Text;
using Wuwa.Core;

namespace Wuwa.Tests;

[TestClass]
public sealed class ConveneLinkExtractorTests
{
    private const string Link = "https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record?svr_id=test&record_id=abc";

    [TestMethod]
    public void ExtractsUrlFromCurrentSchemeAWithoutStandardHeader()
    {
        const string prefix = "\uFEFFLog file open, [2026.08.22-10.41.13]\r\n[GameThread] OpenWebView ";
        var plain = Encoding.UTF8.GetBytes(prefix + "{\"url\":\"" + Link + "\"}");
        var encrypted = plain.Select(EncodeSchemeA).ToArray();

        Assert.AreEqual(Link, ConveneLinkExtractor.ExtractFromText(Link));
        Assert.AreEqual(Link, ConveneLinkExtractor.ExtractFromText(Encoding.UTF8.GetString(plain)));
        Assert.AreEqual(Link, ConveneLinkExtractor.Extract(encrypted));
    }

    private static byte EncodeSchemeA(byte plain)
    {
        for (var candidate = 0; candidate <= byte.MaxValue; candidate++)
        {
            var decoded = (byte)(candidate ^ ((candidate & 1) == 1 ? 0xA5 : 0xEF));
            if (decoded == plain)
            {
                return (byte)candidate;
            }
        }

        throw new InvalidOperationException("Scheme A LUT has no inverse for the test byte.");
    }

    [TestMethod]
    public void ExtractsUrlFromSchemeBWithHeader()
    {
        var plain = Encoding.UTF8.GetBytes($"Log file open\n{Link}");
        var encrypted = new byte[plain.Length + 3];
        encrypted[0] = 0;
        encrypted[1] = 0x4C;
        encrypted[2] = 0x4F;
        for (var i = 0; i < plain.Length; i++)
        {
            encrypted[i + 3] = (byte)(plain[i] ^ 0x55);
        }

        Assert.AreEqual(Link, ConveneLinkExtractor.Extract(encrypted));
    }

    [TestMethod]
    public void ReturnsLastUrlWhenTheLogContainsMultipleHistoryPages()
    {
        var text = $"{Link}&page=old\n{Link}&page=new";

        Assert.AreEqual($"{Link}&page=new", ConveneLinkExtractor.ExtractFromText(text));
    }
}
