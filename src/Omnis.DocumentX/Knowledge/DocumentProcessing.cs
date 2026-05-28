using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;
using UglyToad.PdfPig;

namespace Omnis.DocumentX.Knowledge;

/// <summary>
/// 文档文本抽取接口，后续可替换为更完整的 PDF/OCR/Office 解析器。
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>从上传文件流中抽取纯文本。</summary>
    Task<string> ExtractAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken);
}

/// <summary>
/// 文本分片接口，负责把长文本切成可检索的 chunk。
/// </summary>
public interface ITextChunker
{
    /// <summary>按最大长度和重叠长度生成分片。</summary>
    IReadOnlyCollection<string> Chunk(string text, int maxChunkLength = 1200, int overlapLength = 120);
}

/// <summary>
/// Embedding 标识生成接口；当前生成稳定 ID，真实向量由存储层向量化器负责。
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>根据内容生成稳定 embedding id，便于去重和追踪。</summary>
    string GenerateEmbeddingId(string content);
}

/// <summary>
/// MVP 文档文本抽取器，支持 TXT、Markdown 和轻量 PDF 文本抽取。
/// </summary>
internal sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

    static readonly Encoding[] LegacyChineseEncodings = CreateLegacyChineseEncodings();

    // MVP 阶段仅开放 PRD 要求的三类文件；Office/OCR 后续接入。
    static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown",
        ".pdf"
    };

    public async Task<string> ExtractAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException("Only PDF, TXT and Markdown uploads are supported in the MVP knowledge module.");
        }

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        // PDF 先做无外部依赖的启发式抽取，正式生产可换成专门 PDF 解析库。
        var text = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? ExtractPdfText(bytes)
            : DecodeText(bytes);

        return Normalize(text);
    }

    /// <summary>
    /// 解码 UTF-8 文本，并兼容 UTF-8 BOM。
    /// </summary>
    static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Utf8Strict.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
        }

        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeLegacyChineseText(bytes);
        }
    }

    static string DecodePdfLiteralText(string value)
    {
        var unescaped = UnescapePdfText(value);
        var bytes = Encoding.Latin1.GetBytes(unescaped);
        return DecodeText(bytes);
    }

    static string DecodeLegacyChineseText(byte[] bytes)
    {
        return LegacyChineseEncodings
            .Select(encoding => encoding.GetString(bytes))
            .OrderBy(ScoreDecodedText)
            .First();
    }

    static Encoding[] CreateLegacyChineseEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return
        [
            Encoding.GetEncoding(54936), // GB18030
            Encoding.GetEncoding(936) // GBK/GB2312
        ];
    }

    static int ScoreDecodedText(string value)
    {
        var score = 0;
        foreach (var current in value)
        {
            score += current switch
            {
                '\uFFFD' => 50,
                >= '\u0080' and <= '\u009F' => 10,
                _ => 0
            };
        }

        return score;
    }

    /// <summary>
    /// 从 PDF 内容流中抽取常见 Tj/TJ 文本片段；这是 MVP 轻量方案。
    /// </summary>
    static string ExtractPdfText(byte[] bytes)
    {
        var parsedText = ExtractPdfTextWithPdfPig(bytes);
        if (LooksLikeExtractedText(parsedText))
        {
            return parsedText;
        }

        var raw = Encoding.Latin1.GetString(bytes);
        var parts = new List<string>();
        AddPdfTextParts(raw, parts);

        foreach (var streamText in ExtractFlateDecodedPdfStreams(raw))
        {
            AddPdfTextParts(streamText, parts);
        }

        var text = string.Join(' ', parts.Where(LooksLikeExtractedText));
        if (LooksLikeExtractedText(text))
        {
            return text;
        }

        throw new InvalidOperationException("The PDF did not contain extractable text. Please upload a text-based PDF or convert it to TXT/Markdown first.");
    }

    static string ExtractPdfTextWithPdfPig(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var document = PdfDocument.Open(stream);
            var builder = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine().AppendLine();
                }

                builder.Append(page.Text);
            }

            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    static void AddPdfTextParts(string content, List<string> parts)
    {
        var matches = Regex.Matches(content, @"\((?<text>(?:\\.|[^\\)])*)\)\s*Tj|\[(?<array>[^\]]+)\]\s*TJ");

        foreach (Match match in matches)
        {
            if (match.Groups["text"].Success)
            {
                parts.Add(DecodePdfLiteralText(match.Groups["text"].Value));
                continue;
            }

            if (match.Groups["array"].Success)
            {
                foreach (Match item in Regex.Matches(match.Groups["array"].Value, @"\((?<text>(?:\\.|[^\\)])*)\)"))
                {
                    parts.Add(DecodePdfLiteralText(item.Groups["text"].Value));
                }
            }
        }
    }

    static IEnumerable<string> ExtractFlateDecodedPdfStreams(string raw)
    {
        var matches = Regex.Matches(raw, @"(?<dict><<[\s\S]*?>>)\s*stream\r?\n(?<stream>[\s\S]*?)\r?\nendstream");
        foreach (Match match in matches)
        {
            if (!match.Groups["dict"].Value.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                continue;
            }

            var compressedBytes = Encoding.Latin1.GetBytes(match.Groups["stream"].Value);
            using var input = new MemoryStream(compressedBytes);
            using var deflate = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            try
            {
                deflate.CopyTo(output);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            yield return Encoding.Latin1.GetString(output.ToArray());
        }
    }

    static bool LooksLikeExtractedText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lettersOrDigits = 0;
        var suspicious = 0;
        foreach (var current in value)
        {
            if (char.IsLetterOrDigit(current) || IsCjk(current))
            {
                lettersOrDigits++;
            }
            else if (char.IsControl(current) && !char.IsWhiteSpace(current))
            {
                suspicious++;
            }
            else if (current == '\uFFFD')
            {
                suspicious += 5;
            }
        }

        return lettersOrDigits >= 4 && suspicious * 4 < value.Length;
    }

    static bool IsCjk(char value)
    {
        return value is >= '\u3400' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
    }

    /// <summary>
    /// 还原 PDF 字符串里的基础转义字符。
    /// </summary>
    static string UnescapePdfText(string value)
    {
        return value
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);
    }

    /// <summary>
    /// 统一换行和空白，降低分片时的格式噪声。
    /// </summary>
    static string Normalize(string text)
    {
        text = RemovePostgresInvalidText(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    /// <summary>
    /// PostgreSQL text/json fields cannot store NUL bytes or invalid Unicode surrogate pairs.
    /// </summary>
    static string RemovePostgresInvalidText(string value)
    {
        var hasInvalid = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '\0' || char.IsLowSurrogate(current))
            {
                hasInvalid = true;
                break;
            }

            if (char.IsHighSurrogate(current)
                && (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])))
            {
                hasInvalid = true;
                break;
            }
        }

        if (!hasInvalid)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '\0' || char.IsLowSurrogate(current))
            {
                continue;
            }

            if (char.IsHighSurrogate(current))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    builder.Append(current);
                    builder.Append(value[index + 1]);
                    index++;
                }

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}

/// <summary>
/// 段落优先的分片器，超长段落再使用滑动窗口切分。
/// </summary>
internal sealed class ParagraphTextChunker : ITextChunker
{
    public IReadOnlyCollection<string> Chunk(string text, int maxChunkLength = 1200, int overlapLength = 120)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var chunks = new List<string>();
        // 先按空行识别段落，尽量保留语义完整性。
        var paragraphs = Regex.Split(text.Trim(), @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > maxChunkLength)
            {
                Flush();
                // 单段过长时使用带重叠的滑动窗口，减少边界信息丢失。
                AddSlidingChunks(paragraph);
                continue;
            }

            if (builder.Length > 0 && builder.Length + paragraph.Length + 2 > maxChunkLength)
            {
                Flush();
            }

            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            builder.Append(paragraph);
        }

        Flush();
        return chunks;

        // 把当前累积段落落成一个 chunk。
        void Flush()
        {
            if (builder.Length == 0)
            {
                return;
            }

            chunks.Add(builder.ToString());
            builder.Clear();
        }

        // 对超长段落做滑动窗口分片。
        void AddSlidingChunks(string value)
        {
            var step = Math.Max(1, maxChunkLength - overlapLength);
            for (var start = 0; start < value.Length; start += step)
            {
                var length = Math.Min(maxChunkLength, value.Length - start);
                chunks.Add(value.Substring(start, length));
                if (start + length >= value.Length)
                {
                    break;
                }
            }
        }
    }
}

/// <summary>
/// 确定性 embedding id 生成器，当前用内容 SHA-256 作为稳定标识。
/// </summary>
internal sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator
{
    /// <summary>生成内容哈希形式的 embedding id。</summary>
    public string GenerateEmbeddingId(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
