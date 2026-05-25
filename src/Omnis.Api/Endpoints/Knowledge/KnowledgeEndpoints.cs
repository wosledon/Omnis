using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Omnis.Contracts.Knowledge;
using Omnis.DocumentX.Knowledge;

namespace Omnis.Api.Endpoints;

/// <summary>
/// 知识管理模块的最小 API 路由集合。
/// </summary>
public static class KnowledgeEndpoints
{
    /// <summary>
    /// 注册知识库、文档上传、分片预览、ACL 和审计日志接口。
    /// </summary>
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        // 统一挂在 /api 下，便于后续接入 API Gateway 或版本前缀。
        var group = app.MapGroup("/api")
            .WithTags("Knowledge");

        // 创建知识库，要求调用方显式传入 tenant/workspace。
        group.MapPost("/knowledge-bases", async (
            CreateKnowledgeBaseRequest request,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var created = await knowledge.CreateKnowledgeBaseAsync(request, cancellationToken);
            return Results.Created($"/api/knowledge-bases/{created.Id}", created);
        });

        // 按租户和可选工作空间列出知识库。
        group.MapGet("/knowledge-bases", async (
            [FromQuery] string tenantId,
            [FromQuery] string? workspaceId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.ListKnowledgeBasesAsync(tenantId, workspaceId, cancellationToken);
            return Results.Ok(result);
        });

        // 查询单个知识库详情。
        group.MapGet("/knowledge-bases/{knowledgeBaseId:guid}", async (
            Guid knowledgeBaseId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.GetKnowledgeBaseAsync(knowledgeBaseId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 查询某个知识库下的文档，支持标签和目录过滤。
        group.MapGet("/knowledge-bases/{knowledgeBaseId:guid}/documents", async (
            Guid knowledgeBaseId,
            [FromQuery] string tenantId,
            [FromQuery] string workspaceId,
            [FromQuery] string? tag,
            [FromQuery] string? directoryPath,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.ListDocumentsAsync(knowledgeBaseId, tenantId, workspaceId, tag, directoryPath, cancellationToken);
            return Results.Ok(result);
        });

        // 上传 MVP 支持的 PDF/TXT/Markdown 文档，并同步完成解析、分片和向量写入。
        group.MapPost("/knowledge-bases/{knowledgeBaseId:guid}/documents", async (
            Guid knowledgeBaseId,
            [FromForm] IFormFile file,
            [FromForm] string tenantId,
            [FromForm] string workspaceId,
            [FromForm] DocumentVisibility visibility,
            [FromForm] string? tags,
            [FromForm] string? directoryPath,
            [FromForm] string? acl,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { error = "Uploaded file is empty." });
            }

            // 表单字段中的 tags 使用逗号分隔；ACL 使用 JSON 数组，方便前端一次提交。
            var options = new UploadDocumentOptions(
                tenantId,
                workspaceId,
                visibility,
                SplitCsv(tags),
                directoryPath,
                ParseAcl(acl));

            try
            {
                await using var stream = file.OpenReadStream();
                var document = await knowledge.UploadDocumentAsync(
                    knowledgeBaseId,
                    file.FileName,
                    file.ContentType,
                    stream,
                    options,
                    cancellationToken);

                return Results.Created($"/api/documents/{document.Id}", document);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        // 当前 API 没有启用浏览器表单防伪令牌，文件上传接口需要关闭 antiforgery。
        .DisableAntiforgery();

        // 查询文档详情。
        group.MapGet("/documents/{documentId:guid}", async (
            Guid documentId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.GetDocumentAsync(documentId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 查询文档处理状态，供管理后台轮询显示。
        group.MapGet("/documents/{documentId:guid}/processing-status", async (
            Guid documentId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.GetProcessingStatusAsync(documentId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 分片预览接口，知识管理员可查看解析后的 chunk。
        group.MapGet("/documents/{documentId:guid}/chunks", async (
            Guid documentId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.GetChunksAsync(documentId, cancellationToken);
            return Results.Ok(result);
        });

        // 查询文档 ACL。
        group.MapGet("/documents/{documentId:guid}/acl", async (
            Guid documentId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.GetAclAsync(documentId, cancellationToken);
            return Results.Ok(result);
        });

        // 替换文档 ACL，并同步更新 chunk/vector 的 acl_hash。
        group.MapPut("/documents/{documentId:guid}/acl", async (
            Guid documentId,
            UpdateDocumentAclRequest request,
            [FromHeader(Name = "X-Actor-Id")] string? actorId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.UpdateAclAsync(documentId, request, actorId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 查询知识模块审计日志，默认由 tenant_id 做安全边界。
        group.MapGet("/audit-logs", async (
            [FromQuery] string tenantId,
            [FromQuery] Guid? entityId,
            IKnowledgeService knowledge,
            CancellationToken cancellationToken) =>
        {
            var result = await knowledge.GetAuditLogsAsync(tenantId, entityId, cancellationToken);
            return Results.Ok(result);
        });

        return app;
    }

    /// <summary>
    /// 将表单中的逗号分隔值转换为数组。
    /// </summary>
    static string[] SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 将表单中的 ACL JSON 字符串解析为强类型授权条目。
    /// </summary>
    static IReadOnlyCollection<UpsertDocumentAclEntry> ParseAcl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<UpsertDocumentAclEntry>();
        }

        return JsonSerializer.Deserialize<UpsertDocumentAclEntry[]>(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }
}
