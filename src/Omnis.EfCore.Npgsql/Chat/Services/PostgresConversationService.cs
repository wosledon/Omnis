using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Omnis.Contracts.Chat;
using Omnis.EfCore.Npgsql.Chat.Entities;
using Omnis.Retrieval.Rag;
using Omnis.Workflow.Chat;

namespace Omnis.EfCore.Npgsql.Chat.Services;

/// <summary>
/// PostgreSQL 版对话引擎服务，负责把会话消息落库，并把用户问题编排到 RAG 服务。
/// </summary>
internal sealed class PostgresConversationService(
    OmnisNpgsqlDbContext dbContext,
    IRagService ragService
) : IConversationService
{
    // 对话消息中的 citations 和转人工 summary 都以 jsonb 保存，序列化风格与 API 保持一致。
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 创建会话并保存用户身份快照。身份快照后续会传给 RAG 检索层做 ACL 过滤。
    /// </summary>
    public async Task<ConversationCreatedResponse> CreateConversationAsync(
        CreateConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.User.Id);

        var now = DateTime.UtcNow;
        var entity = new ConversationEntity
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            WorkspaceId = request.WorkspaceId.Trim(),
            ApplicationId = NormalizeOptional(request.ApplicationId),
            UserId = request.User.Id.Trim(),
            UserName = NormalizeOptional(request.User.Name),
            UserGroups = NormalizeValues(request.User.Groups),
            UserRoles = NormalizeValues(request.User.Roles),
            Channel = request.Channel.Trim(),
            Status = ConversationStatus.Active,
            KnowledgeBaseIds = request.KnowledgeBaseIds.Distinct().ToArray(),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Conversations.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConversationCreatedResponse(entity.Id, entity.Status);
    }

    /// <summary>
    /// 查询单个会话，不跟踪 EF 状态，避免只读接口产生额外变更。
    /// </summary>
    public async Task<ConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(conversation => conversation.Id == conversationId, cancellationToken);

        return entity is null ? null : ToConversationDto(entity);
    }

    /// <summary>
    /// 查询最近会话列表，MVP 阶段限制返回 100 条，避免后台列表一次拉取过多历史数据。
    /// </summary>
    public async Task<IReadOnlyCollection<ConversationDto>> ListConversationsAsync(
        string tenantId,
        string? workspaceId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var normalizedWorkspaceId = NormalizeOptional(workspaceId);
        var normalizedUserId = NormalizeOptional(userId);
        var query = dbContext.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId.Trim());

        if (normalizedWorkspaceId is not null)
        {
            query = query.Where(conversation => conversation.WorkspaceId == normalizedWorkspaceId);
        }

        if (normalizedUserId is not null)
        {
            query = query.Where(conversation => conversation.UserId == normalizedUserId);
        }

        var conversations = await query
            .OrderByDescending(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        return conversations.Select(ToConversationDto).ToArray();
    }

    /// <summary>
    /// 关闭会话并记录关闭时间。关闭后的会话不再接受 SendMessageAsync 生成 AI 回复。
    /// </summary>
    public async Task<ConversationDto?> CloseConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(entity => entity.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        conversation.Status = ConversationStatus.Closed;
        conversation.ClosedAt = now;
        conversation.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToConversationDto(conversation);
    }

    /// <summary>
    /// 查询会话消息历史，保持创建时间正序，便于前端直接渲染。
    /// </summary>
    public async Task<IReadOnlyCollection<ConversationMessageDto>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .ToArrayAsync(cancellationToken);

        return messages.Select(ToMessageDto).ToArray();
    }

    /// <summary>
    /// 发送消息主流程：先写用户消息，再带历史上下文调用 RAG，最后写 AI 回复。
    /// </summary>
    public async Task<SendConversationMessageResponse> SendMessageAsync(
        Guid conversationId,
        SendConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(entity => entity.Id == conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation was not found.");

        if (conversation.Status != ConversationStatus.Active)
        {
            throw new InvalidOperationException("Conversation is not active.");
        }

        var now = DateTime.UtcNow;
        var userMessage = new ConversationMessageEntity
        {
            Id = Guid.NewGuid(),
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = request.Content.Trim(),
            CitationsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        };

        var assistantMessageId = Guid.NewGuid();
        dbContext.ConversationMessages.Add(userMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        // 预生成 assistantMessageId 后传给 RAG，使 rag_inference_logs.message_id 能与最终 AI 消息对齐。
        var history = await LoadRagHistoryAsync(conversation.Id, userMessage.Id, request.Options, cancellationToken);
        var ragResponse = await ragService.AnswerAsync(CreateRagRequest(conversation, userMessage, assistantMessageId, request, history), cancellationToken);
        var citations = ragResponse.Citations.Select(citation => new ChatCitationDto(
            citation.Id,
            citation.DocumentId,
            citation.ChunkId,
            citation.Title,
            citation.Preview,
            citation.Url)).ToArray();

        // RAG 观测写入由检索层完成，这里回查日志 ID，便于消息详情和反馈闭环跳转调试链路。
        var ragLogId = await FindRagLogIdAsync(conversation.TenantId, assistantMessageId, cancellationToken);
        var assistantMessage = new ConversationMessageEntity
        {
            Id = assistantMessageId,
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = ragResponse.Answer,
            CitationsJson = ToJson(citations),
            ConfidenceScore = ragResponse.ConfidenceScore,
            RagInferenceLogId = ragLogId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        conversation.UpdatedAt = DateTime.UtcNow;
        dbContext.ConversationMessages.Add(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SendConversationMessageResponse(
            userMessage.Id,
            assistantMessage.Id,
            ragResponse.Answer,
            ragResponse.ConfidenceScore,
            citations,
            ragResponse.HandoffSuggested,
            ragResponse.KnowledgeBoundaryTriggered);
    }

    /// <summary>
    /// 写入用户反馈，并冗余关联 conversation_id 与 rag_inference_log_id，方便运营后台低分筛选。
    /// </summary>
    public async Task<MessageFeedbackDto?> AddFeedbackAsync(
        Guid messageId,
        MessageFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.ConversationMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == messageId, cancellationToken);
        if (message is null)
        {
            return null;
        }

        var userId = NormalizeOptional(request.UserId) ?? "anonymous";
        var now = DateTime.UtcNow;
        var feedback = new MessageFeedbackEntity
        {
            Id = Guid.NewGuid(),
            TenantId = message.TenantId,
            MessageId = message.Id,
            ConversationId = message.ConversationId,
            UserId = userId,
            Rating = request.Rating,
            Reason = NormalizeOptional(request.Reason),
            RagInferenceLogId = message.RagInferenceLogId,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.MessageFeedback.Add(feedback);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToFeedbackDto(feedback);
    }

    /// <summary>
    /// 创建人工转接记录，并将会话状态切换为 Handoff，使 AI 自动外发默认暂停。
    /// </summary>
    public async Task<HumanHandoffDto?> CreateHandoffAsync(
        Guid conversationId,
        CreateHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(entity => entity.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var recentMessages = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderByDescending(message => message.CreatedAt)
            .Take(10)
            .OrderBy(message => message.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var summary = CreateHandoffSummary(recentMessages);
        var now = DateTime.UtcNow;
        var entity = new HumanHandoffEntity
        {
            Id = Guid.NewGuid(),
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id,
            TriggerType = request.TriggerType,
            SummaryJson = ToJson(summary),
            LastAiMessageId = request.LastAiMessageId,
            Status = HandoffStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };

        conversation.Status = ConversationStatus.Handoff;
        conversation.UpdatedAt = now;
        dbContext.HumanHandoffs.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToHandoffDto(entity);
    }

    /// <summary>
    /// 加载最近 N 轮历史消息，排除当前刚写入的用户消息，避免重复注入 Prompt。
    /// </summary>
    async Task<RagMessage[]> LoadRagHistoryAsync(
        Guid conversationId,
        Guid currentUserMessageId,
        ChatRagOptions? options,
        CancellationToken cancellationToken)
    {
        var maxTurns = Math.Max(1, options?.MaxHistoryTurns ?? 10);
        var limit = maxTurns * 2;

        var messages = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.Id != currentUserMessageId)
            .OrderByDescending(message => message.CreatedAt)
            .Take(limit)
            .OrderBy(message => message.CreatedAt)
            .ToArrayAsync(cancellationToken);

        return messages
            .Select(message => new RagMessage(ToRagRole(message.Role), message.Content, ToDateTimeOffset(message.CreatedAt)))
            .ToArray();
    }

    /// <summary>
    /// 将对话请求转换为 RAG 请求，集中注入租户、权限上下文、知识库范围和历史消息。
    /// </summary>
    static RagAnswerRequest CreateRagRequest(
        ConversationEntity conversation,
        ConversationMessageEntity userMessage,
        Guid assistantMessageId,
        SendConversationMessageRequest request,
        RagMessage[] history)
    {
        var options = request.Options ?? new ChatRagOptions();
        var knowledgeBaseIds = request.KnowledgeBaseIds is { Length: > 0 }
            ? request.KnowledgeBaseIds.Distinct().ToArray()
            : conversation.KnowledgeBaseIds;

        return new RagAnswerRequest
        {
            TenantId = conversation.TenantId,
            WorkspaceId = conversation.WorkspaceId,
            ApplicationId = conversation.ApplicationId,
            ConversationId = conversation.Id.ToString("D"),
            MessageId = assistantMessageId.ToString("D"),
            UserId = conversation.UserId,
            UserGroups = conversation.UserGroups,
            UserRoles = conversation.UserRoles,
            KnowledgeBaseIds = knowledgeBaseIds,
            Question = userMessage.Content,
            ConversationHistory = history,
            Options = new RagOptions
            {
                RetrievalTopK = options.RetrievalTopK,
                ContextTopN = options.ContextTopN,
                MaxHistoryTurns = options.MaxHistoryTurns,
                MinRelevanceScore = options.MinRelevanceScore,
                HandoffConfidenceThreshold = options.HandoffConfidenceThreshold,
                StrictKnowledgeBoundary = options.StrictKnowledgeBoundary
            }
        };
    }

    /// <summary>
    /// 根据预生成的 AI 消息 ID 回查 RAG 观测日志 ID。
    /// </summary>
    async Task<Guid?> FindRagLogIdAsync(string tenantId, Guid assistantMessageId, CancellationToken cancellationToken)
    {
        var messageId = assistantMessageId.ToString("D");
        return await dbContext.RagInferenceLogs
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.MessageId == messageId)
            .OrderByDescending(log => log.CreatedAt)
            .Select(log => (Guid?)log.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 基于最近 10 条消息生成轻量转人工摘要。后续可替换为 LLM 摘要器或专用规则引擎。
    /// </summary>
    static HandoffSummaryDto CreateHandoffSummary(IReadOnlyCollection<ConversationMessageEntity> messages)
    {
        var lastUserMessage = messages.LastOrDefault(message => message.Role == MessageRole.User)?.Content ?? string.Empty;
        var lastAssistantMessage = messages.LastOrDefault(message => message.Role == MessageRole.Assistant)?.Content ?? string.Empty;

        return new HandoffSummaryDto(
            string.IsNullOrWhiteSpace(lastUserMessage) ? "User requested help." : TrimForSummary(lastUserMessage),
            messages
                .Where(message => message.Role == MessageRole.User)
                .Select(message => TrimForSummary(message.Content))
                .Where(value => value.Length > 0)
                .TakeLast(3)
                .ToArray(),
            string.IsNullOrWhiteSpace(lastUserMessage) ? [] : [TrimForSummary(lastUserMessage)],
            string.IsNullOrWhiteSpace(lastAssistantMessage)
                ? "I have reviewed the conversation and will help you continue."
                : TrimForSummary(lastAssistantMessage));
    }

    /// <summary>
    /// 将会话实体转换为 API DTO。
    /// </summary>
    static ConversationDto ToConversationDto(ConversationEntity entity)
    {
        return new ConversationDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.UserId,
            entity.UserName,
            entity.UserGroups,
            entity.UserRoles,
            entity.Channel,
            entity.Status,
            entity.KnowledgeBaseIds,
            ToDateTimeOffset(entity.CreatedAt),
            entity.ClosedAt is null ? null : ToDateTimeOffset(entity.ClosedAt));
    }

    /// <summary>
    /// 将消息实体转换为 API DTO，并反序列化引用来源。
    /// </summary>
    static ConversationMessageDto ToMessageDto(ConversationMessageEntity entity)
    {
        return new ConversationMessageDto(
            entity.Id,
            entity.ConversationId,
            entity.Role,
            entity.Content,
            FromJson<ChatCitationDto[]>(entity.CitationsJson) ?? [],
            entity.ConfidenceScore,
            ToDateTimeOffset(entity.CreatedAt));
    }

    /// <summary>
    /// 将反馈实体转换为 API DTO。
    /// </summary>
    static MessageFeedbackDto ToFeedbackDto(MessageFeedbackEntity entity)
    {
        return new MessageFeedbackDto(
            entity.Id,
            entity.MessageId,
            entity.UserId,
            entity.Rating,
            entity.Reason,
            ToDateTimeOffset(entity.CreatedAt));
    }

    /// <summary>
    /// 将人工转接实体转换为 API DTO，并反序列化摘要 JSON。
    /// </summary>
    static HumanHandoffDto ToHandoffDto(HumanHandoffEntity entity)
    {
        return new HumanHandoffDto(
            entity.Id,
            entity.ConversationId,
            entity.TriggerType,
            FromJson<HandoffSummaryDto>(entity.SummaryJson) ?? new HandoffSummaryDto(string.Empty, [], [], string.Empty),
            entity.LastAiMessageId,
            entity.Status,
            entity.AssignedAgentId,
            ToDateTimeOffset(entity.CreatedAt));
    }

    /// <summary>
    /// 将对话层角色映射为 RAG 层使用的 role 字符串。
    /// </summary>
    static string ToRagRole(MessageRole role)
    {
        return role switch
        {
            MessageRole.Assistant => "assistant",
            MessageRole.Agent => "agent",
            MessageRole.System => "system",
            _ => "user"
        };
    }

    /// <summary>
    /// 规范化用户组、角色等字符串数组，去空白并按大小写不敏感去重。
    /// </summary>
    static string[] NormalizeValues(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 规范化可选字符串，空白值统一转为 null。
    /// </summary>
    static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// 摘要字段长度保护，避免坐席卡片被超长消息撑开。
    /// </summary>
    static string TrimForSummary(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..160] + "...";
    }

    /// <summary>
    /// 使用统一 JSON 配置序列化 jsonb 字段。
    /// </summary>
    static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// 使用统一 JSON 配置反序列化 jsonb 字段。
    /// </summary>
    static T? FromJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// 将 EF 实体里的 UTC DateTime 转为 API 使用的 DateTimeOffset。
    /// </summary>
    static DateTimeOffset ToDateTimeOffset(DateTime? value)
    {
        var dateTime = value ?? DateTime.UtcNow;
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return new DateTimeOffset(dateTime.ToUniversalTime());
    }
}
