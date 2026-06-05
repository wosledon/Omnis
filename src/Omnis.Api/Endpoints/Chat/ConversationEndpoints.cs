using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Omnis.Contracts.Chat;
using Omnis.Workflow.Chat;

namespace Omnis.Api.Endpoints;

/// <summary>
/// 对话引擎 HTTP 路由映射，覆盖会话、消息、反馈和人工转接的 MVP API。
/// </summary>
public static class ConversationEndpoints
{
    /// <summary>
    /// 注册对话相关接口，统一挂载在 /api 下以匹配 Specs 中的 REST API 草案。
    /// </summary>
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Conversations");

        // 创建会话：保存租户/工作空间/用户/渠道快照，返回 conversation_id 和初始状态。
        group.MapPost("/conversations", async (
            CreateConversationRequest request,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await conversations.CreateConversationAsync(request, cancellationToken);
                return Results.Created($"/api/conversations/{created.ConversationId}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 查询单个会话详情，用于 Widget 恢复会话或管理后台打开会话详情。
        group.MapGet("/conversations/{conversationId:guid}", async (
            Guid conversationId,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            var result = await conversations.GetConversationAsync(conversationId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 查询会话列表：tenantId 必填，workspaceId/userId 可选，满足后台按范围筛选的需要。
        group.MapGet("/conversations", async (
            [FromQuery] string tenantId,
            [FromQuery] string? workspaceId,
            [FromQuery] string? userId,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await conversations.ListConversationsAsync(tenantId, workspaceId, userId, cancellationToken);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 关闭会话：关闭后服务层会拒绝继续生成 AI 回复。
        group.MapPost("/conversations/{conversationId:guid}/close", async (
            Guid conversationId,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            var result = await conversations.CloseConversationAsync(conversationId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 查询会话消息历史，前端按时间正序渲染即可。
        group.MapGet("/conversations/{conversationId:guid}/messages", async (
            Guid conversationId,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            var result = await conversations.GetMessagesAsync(conversationId, cancellationToken);
            return Results.Ok(result);
        });

        // 发送用户消息：服务层会写入用户消息、调用 RAG、保存 AI 消息并返回引用和置信度。
        group.MapPost("/conversations/{conversationId:guid}/messages", async (
            Guid conversationId,
            SendConversationMessageRequest request,
            IConversationService conversations,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!request.Stream)
                {
                    var result = await conversations.SendMessageAsync(conversationId, request, cancellationToken);
                    return Results.Ok(result);
                }

                await WriteSseAsync(httpContext, conversations.StreamMessageAsync(conversationId, request, cancellationToken), cancellationToken);
                return Results.Empty;
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 提交消息反馈：点赞/点踩会关联消息和 RAG 观测日志，供后续低分问题闭环使用。
        group.MapPost("/messages/{messageId:guid}/feedback", async (
            Guid messageId,
            MessageFeedbackRequest request,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            var result = await conversations.AddFeedbackAsync(messageId, request, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 主动创建人工转接：生成摘要并将会话状态切换到 Handoff。
        group.MapPost("/conversations/{conversationId:guid}/handoff", async (
            Guid conversationId,
            CreateHandoffRequest request,
            IConversationService conversations,
            CancellationToken cancellationToken) =>
        {
            var result = await conversations.CreateHandoffAsync(conversationId, request, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }

    /// <summary>
    /// 以 SSE 协议写出对话流式结果。
    /// </summary>
    static async Task WriteSseAsync(
        HttpContext httpContext,
        IAsyncEnumerable<ConversationStreamChunk> chunks,
        CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (chunk.IsCompleted)
            {
                await WriteEventAsync(httpContext, "completed", chunk.Completed, cancellationToken);
            }
            else
            {
                await WriteEventAsync(httpContext, "delta", new { content = chunk.ContentDelta }, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 写出单个 SSE 事件。
    /// </summary>
    static async Task WriteEventAsync<T>(
        HttpContext httpContext,
        string eventName,
        T data,
        CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await httpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(data, JsonOptions)}\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// SSE data 使用 Web 默认 JSON 风格，保持 camelCase 输出。
    /// </summary>
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
