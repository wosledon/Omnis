using Omnis.Contracts.Knowledge;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Entities;

/// <summary>
/// 知识库持久化实体。
/// </summary>
public sealed class KnowledgeBaseEntity : EntityBase
{
    /// <summary>租户标识，业务数据隔离的第一层边界。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识，业务线或部门级隔离边界。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>知识库名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>知识库描述。</summary>
    public string? Description { get; set; }

    /// <summary>知识库默认可见性策略。</summary>
    public KnowledgeBaseVisibility DefaultVisibility { get; set; } = KnowledgeBaseVisibility.Members;
}
