using System.Security.Cryptography;
using System.Text;

using Omnis.EfCore.Npgsql.Options;
using Omnis.EfCore.Npgsql.Vector;

namespace Omnis.EfCore.Npgsql.Knowledge.Services;

/// <summary>
/// 确定性占位向量器，用哈希技巧生成可重复的稀疏语义近似向量。
/// </summary>
internal sealed class DeterministicVectorizer(OmnisNpgsqlOptions options) : IKnowledgeVectorizer
{
    /// <summary>
    /// 生成归一化向量，确保相同文本在不同进程中得到相同结果。
    /// </summary>
    public double[] Vectorize(string content)
    {
        var dimensions = Math.Max(8, options.EmbeddingDimensions);
        var vector = new double[dimensions];
        // 简单分词后把 token 哈希到固定桶位，为后续真实 embedding 留出接口。
        foreach (var token in Tokenize(content))
        {
            // 使用符号哈希减少所有 token 都正向累加造成的偏置。
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.ToLowerInvariant()));
            var bucket = BitConverter.ToUInt32(hash, 0) % dimensions;
            var sign = (hash[4] & 1) == 0 ? 1d : -1d;
            vector[bucket] += sign;
        }

        // L2 归一化，便于后续按余弦相似度或内积进行排序。
        var norm = Math.Sqrt(vector.Sum(value => value * value));
        if (norm == 0)
        {
            return vector;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= norm;
        }

        return vector;
    }

    static IEnumerable<string> Tokenize(string value)
    {
        var normalized = value.ToLowerInvariant();
        var tokens = normalized
            .Split([' ', '\n', '\t', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '|', '-', '_', '+', '=', '*', '&', '^', '%', '$', '#', '@', '，', '。', '；', '：', '！', '？', '（', '）', '【', '】', '、'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1);

        foreach (var token in tokens)
        {
            yield return token;
        }

        var cjk = normalized.Where(ch => ch is >= '\u4e00' and <= '\u9fff').ToArray();
        for (var index = 0; index < cjk.Length - 1; index++)
        {
            yield return new string([cjk[index], cjk[index + 1]]);
        }
    }
}
