using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Omnis.Contracts.Llm;
using Omnis.Llm;

namespace Omnis.Api.Endpoints;

/// <summary>
/// LLM 网关 HTTP 路由，提供模型配置、调用、流式调用和审计日志 API。
/// </summary>
public static class LlmEndpoints
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 注册 /api/llm 下的 LLM 网关接口。
    /// </summary>
    public static IEndpointRouteBuilder MapLlmEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/llm")
            .WithTags("LLM Gateway");

        // 创建模型配置：凭据只写入后端存储，返回 DTO 仅展示是否已配置。
        group.MapPost("/model-configs", async (
            CreateLlmModelConfigRequest request,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await gateway.CreateModelConfigAsync(request, cancellationToken);
                return Results.Created($"/api/llm/model-configs/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 列出模型配置：后台可按租户、工作空间、应用和状态筛选。
        group.MapGet("/model-configs", async (
            [FromQuery] string tenantId,
            [FromQuery] string? workspaceId,
            [FromQuery] string? applicationId,
            [FromQuery] LlmModelStatus? status,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await gateway.ListModelConfigsAsync(tenantId, workspaceId, applicationId, status, cancellationToken);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 单个模型配置详情，包含熔断状态快照。
        group.MapGet("/model-configs/{modelConfigId:guid}", async (
            Guid modelConfigId,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var result = await gateway.GetModelConfigAsync(modelConfigId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 更新模型配置。Credentials 为 null 时由服务层保留原凭据。
        group.MapPut("/model-configs/{modelConfigId:guid}", async (
            Guid modelConfigId,
            UpdateLlmModelConfigRequest request,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await gateway.UpdateModelConfigAsync(modelConfigId, request, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 停用模型配置，使其立即退出路由候选。
        group.MapPost("/model-configs/{modelConfigId:guid}/disable", async (
            Guid modelConfigId,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var result = await gateway.DisableModelConfigAsync(modelConfigId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 非流式调用：适合管理后台调试和后端同步调用。
        group.MapPost("/chat/completions", async (
            LlmCompletionRequest request,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await gateway.CompleteAsync(request, cancellationToken);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // SSE 流式调用：对话层和 Widget 可直接消费 delta/completed 事件。
        group.MapPost("/chat/completions/stream", async (
            LlmCompletionRequest request,
            ILlmGateway gateway,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await WriteSseAsync(httpContext, gateway.StreamAsync(request, cancellationToken), cancellationToken);
                return Results.Empty;
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 调用日志查询：用于审计、排障和 Token 成本统计。
        group.MapGet("/invocation-logs", async (
            [FromQuery] string tenantId,
            [FromQuery] string? workspaceId,
            [FromQuery] string? applicationId,
            [FromQuery] Guid? modelConfigId,
            ILlmGateway gateway,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await gateway.ListInvocationLogsAsync(tenantId, workspaceId, applicationId, modelConfigId, cancellationToken);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    static async Task WriteSseAsync(
        HttpContext httpContext,
        IAsyncEnumerable<LlmStreamChunk> chunks,
        CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            await WriteEventAsync(httpContext, chunk.IsCompleted ? "completed" : "delta", chunk, cancellationToken);
        }
    }

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
}
