namespace Omnis.Contracts.Knowledge;

/// <summary>
/// 知识库默认可见性策略。
/// </summary>
public enum KnowledgeBaseVisibility
{
    /// <summary>知识库内所有用户可读。</summary>
    Public = 0,

    /// <summary>仅工作空间成员可读。</summary>
    Members = 1,

    /// <summary>由 ACL 自定义授权决定。</summary>
    Custom = 2
}

/// <summary>
/// 文档级可见性，RAG 检索时需要下推到检索过滤条件。
/// </summary>
public enum DocumentVisibility
{
    /// <summary>公共文档，知识库范围内可读。</summary>
    Public = 0,

    /// <summary>内部文档，通常由角色授权。</summary>
    Internal = 1,

    /// <summary>私有文档，只允许明确授权主体访问。</summary>
    Private = 2
}

/// <summary>
/// 文档来源类型，用于区分上传、网页抓取和外部数据源同步。
/// </summary>
public enum DocumentSourceType
{
    /// <summary>用户上传文件。</summary>
    Upload = 0,

    /// <summary>网页抓取来源。</summary>
    WebPage = 1,

    /// <summary>数据库同步来源。</summary>
    Database = 2,

    /// <summary>REST API 拉取来源。</summary>
    Api = 3
}

/// <summary>
/// 文档处理状态。
/// </summary>
public enum DocumentStatus
{
    /// <summary>正在解析、清洗、分片和向量化。</summary>
    Processing = 0,

    /// <summary>已完成处理，可参与检索。</summary>
    Completed = 1,

    /// <summary>处理失败，保留失败原因供重试或排查。</summary>
    Failed = 2
}

/// <summary>
/// ACL 授权主体类型。
/// </summary>
public enum AclPrincipalType
{
    /// <summary>单个用户。</summary>
    User = 0,

    /// <summary>用户组。</summary>
    UserGroup = 1,

    /// <summary>角色。</summary>
    Role = 2
}

/// <summary>
/// 文档授权权限类型。
/// </summary>
public enum DocumentPermission
{
    /// <summary>读取文档和分片。</summary>
    Read = 0,

    /// <summary>编辑文档内容或元数据。</summary>
    Edit = 1,

    /// <summary>删除文档。</summary>
    Delete = 2,

    /// <summary>共享或授权给其他主体。</summary>
    Share = 3,

    /// <summary>管理文档及其权限。</summary>
    Admin = 4
}
