using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 从 PDF 内容流中抽取常见 Tj/TJ 文本片段；这是 MVP 轻量方案。
    /// </summary>
    static string ExtractPdfText(byte[] bytes)
    {
        var raw = Encoding.Latin1.GetString(bytes);
        var matches = Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)])*)\)\s*Tj|\[(?<array>[^\]]+)\]\s*TJ");
        var parts = new List<string>();

        foreach (Match match in matches)
        {
            if (match.Groups["text"].Success)
            {
                parts.Add(UnescapePdfText(match.Groups["text"].Value));
                continue;
            }

            if (match.Groups["array"].Success)
            {
                foreach (Match item in Regex.Matches(match.Groups["array"].Value, @"\((?<text>(?:\\.|[^\\)])*)\)"))
                {
                    parts.Add(UnescapePdfText(item.Groups["text"].Value));
                }
            }
        }

        return parts.Count > 0
            ? string.Join(' ', parts)
            // 如果不是常见文本型 PDF，退回字节文本解码，避免直接失败。
            : DecodeText(bytes);
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
