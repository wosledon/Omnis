using Omnis.Contracts.Knowledge;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Entities;

/// <summary>
/// 文档 ACL 持久化实体。
/// </summary>
public sealed class DocumentAclEntryEntity : EntityBase
{
    /// <summary>所属文档 ID。</summary>
    public Guid DocumentId { get; set; }

    /// <summary>授权主体类型。</summary>
    public AclPrincipalType PrincipalType { get; set; }

    /// <summary>授权主体 ID。</summary>
    public string PrincipalId { get; set; } = string.Empty;

    /// <summary>授予的文档权限。</summary>
    public DocumentPermission Permission { get; set; } = DocumentPermission.Read;
}
