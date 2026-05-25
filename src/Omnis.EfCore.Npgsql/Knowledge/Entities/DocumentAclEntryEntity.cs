using Omnis.Contracts.Knowledge;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Knowledge.Entities;

/// <summary>
/// 文档 ACL 持久化实体。
/// </summary>
public sealed class DocumentAclEntryEntity : EntityBase
{
    public Guid DocumentId { get; set; }
    public AclPrincipalType PrincipalType { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public DocumentPermission Permission { get; set; } = DocumentPermission.Read;
}
