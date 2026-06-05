namespace Omnis.EfCore.Npgsql.Vector;

/// <summary>
/// 文本向量化接口；后续可替换为 OpenAI、Azure OpenAI 或本地 embedding 模型。
/// </summary>
public interface IKnowledgeVectorizer
{
    /// <summary>把文本内容转换为向量。</summary>
    double[] Vectorize(string content);
}
