using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Omnis.Contracts.Llm;
using Omnis.EfCore.Npgsql.Llm.Entities;
using Omnis.Llm;

namespace Omnis.EfCore.Npgsql.Llm.Services;

/// <summary>
/// PostgreSQL 版 LLM 网关持久化实现，负责模型配置、熔断状态和调用审计日志。
/// </summary>
internal sealed class PostgresLlmGatewayStore(OmnisNpgsqlDbContext dbContext) : ILlmGatewayStore
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LlmModelConfigDto> CreateModelConfigAsync(
        CreateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Endpoint);
        ValidateEndpoint(request.Endpoint);

        var now = DateTime.UtcNow;
        // 新配置创建时同步初始化熔断状态，避免首次调用时缺少健康快照。
        var entity = new LlmModelConfigEntity
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            WorkspaceId = request.WorkspaceId.Trim(),
            ApplicationId = NormalizeOptional(request.ApplicationId),
            Name = request.Name.Trim(),
            Provider = request.Provider,
            Model = request.Model.Trim(),
            Endpoint = request.Endpoint.Trim().TrimEnd('/'),
            DeploymentName = NormalizeOptional(request.DeploymentName),
            Status = request.Status,
            Priority = Math.Max(0, request.Priority),
            FallbackModelConfigId = request.FallbackModelConfigId,
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 600),
            FailureThreshold = Math.Clamp(request.FailureThreshold, 1, 100),
            CircuitBreakSeconds = Math.Clamp(request.CircuitBreakSeconds, 1, 86400),
            PromptTokenPricePer1K = request.PromptTokenPricePer1K,
            CompletionTokenPricePer1K = request.CompletionTokenPricePer1K,
            ParametersJson = ToJson(NormalizeDictionary(request.Parameters)),
            CredentialsJson = ToJson(NormalizeDictionary(request.Credentials)),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.LlmModelConfigs.Add(entity);
        dbContext.LlmCircuitBreakers.Add(DefaultCircuit(entity.Id, now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(entity, DefaultCircuit(entity.Id, now));
    }

    public async Task<IReadOnlyCollection<LlmModelConfigDto>> ListModelConfigsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        LlmModelStatus? status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var normalizedWorkspaceId = NormalizeOptional(workspaceId);
        var normalizedApplicationId = NormalizeOptional(applicationId);
        var query = dbContext.LlmModelConfigs
            .AsNoTracking()
            .Where(config => config.TenantId == tenantId.Trim());

        if (normalizedWorkspaceId is not null)
        {
            query = query.Where(config => config.WorkspaceId == normalizedWorkspaceId);
        }

        if (normalizedApplicationId is not null)
        {
            query = query.Where(config => config.ApplicationId == normalizedApplicationId);
        }

        if (status.HasValue)
        {
            query = query.Where(config => config.Status == status.Value);
        }

        var entities = await query
            .OrderBy(config => config.Priority)
            .ThenByDescending(config => config.UpdatedAt ?? config.CreatedAt)
            .Take(200)
            .ToArrayAsync(cancellationToken);

        // 列表接口一次性加载熔断状态，避免逐条查询造成 N+1。
        var circuits = await LoadCircuitsAsync(entities.Select(entity => entity.Id).ToArray(), cancellationToken);
        return entities.Select(entity => ToDto(entity, circuits.GetValueOrDefault(entity.Id))).ToArray();
    }

    public async Task<LlmModelConfigDto?> GetModelConfigAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.LlmModelConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(config => config.Id == modelConfigId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var circuit = await dbContext.LlmCircuitBreakers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ModelConfigId == modelConfigId, cancellationToken);

        return ToDto(entity, circuit);
    }

    public async Task<LlmModelConfigRecord?> GetModelConfigRecordAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.LlmModelConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(config => config.Id == modelConfigId, cancellationToken);

        return entity is null ? null : ToRecord(entity);
    }

    public async Task<LlmModelConfigDto?> UpdateModelConfigAsync(
        Guid modelConfigId,
        UpdateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Endpoint);
        ValidateEndpoint(request.Endpoint);

        var entity = await dbContext.LlmModelConfigs
            .FirstOrDefaultAsync(config => config.Id == modelConfigId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Name = request.Name.Trim();
        entity.Provider = request.Provider;
        entity.Model = request.Model.Trim();
        entity.Endpoint = request.Endpoint.Trim().TrimEnd('/');
        entity.DeploymentName = NormalizeOptional(request.DeploymentName);
        entity.Status = request.Status;
        entity.Priority = Math.Max(0, request.Priority);
        entity.FallbackModelConfigId = request.FallbackModelConfigId;
        entity.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 600);
        entity.FailureThreshold = Math.Clamp(request.FailureThreshold, 1, 100);
        entity.CircuitBreakSeconds = Math.Clamp(request.CircuitBreakSeconds, 1, 86400);
        entity.PromptTokenPricePer1K = request.PromptTokenPricePer1K;
        entity.CompletionTokenPricePer1K = request.CompletionTokenPricePer1K;
        entity.ParametersJson = ToJson(NormalizeDictionary(request.Parameters));
        if (request.Credentials is not null)
        {
            entity.CredentialsJson = ToJson(NormalizeDictionary(request.Credentials));
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var circuit = await EnsureCircuitAsync(entity.Id, cancellationToken);
        return ToDto(entity, circuit);
    }

    public async Task<LlmModelConfigDto?> DisableModelConfigAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.LlmModelConfigs
            .FirstOrDefaultAsync(config => config.Id == modelConfigId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Status = LlmModelStatus.Disabled;
        entity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var circuit = await EnsureCircuitAsync(entity.Id, cancellationToken);
        return ToDto(entity, circuit);
    }

    public async Task<IReadOnlyList<LlmModelConfigRecord>> ListRouteCandidatesAsync(
        string tenantId,
        string workspaceId,
        string? applicationId,
        Guid? modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var normalizedApplicationId = NormalizeOptional(applicationId);
        var query = dbContext.LlmModelConfigs
            .AsNoTracking()
            .Where(config =>
                config.TenantId == tenantId &&
                config.WorkspaceId == workspaceId &&
                config.Status == LlmModelStatus.Active);

        if (modelConfigId.HasValue)
        {
            // 调试或显式调用时，指定模型配置 ID 会绕过默认路由筛选。
            query = query.Where(config => config.Id == modelConfigId.Value);
        }
        else if (normalizedApplicationId is not null)
        {
            // 应用级配置优先于工作空间默认配置。
            query = query.Where(config => config.ApplicationId == normalizedApplicationId || config.ApplicationId == null);
        }
        else
        {
            query = query.Where(config => config.ApplicationId == null);
        }

        var entities = await query
            .OrderByDescending(config => normalizedApplicationId != null && config.ApplicationId == normalizedApplicationId)
            .ThenBy(config => config.Priority)
            .ThenByDescending(config => config.UpdatedAt ?? config.CreatedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToRecord).ToArray();
    }

    public async Task<LlmCircuitSnapshot> GetCircuitAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var entity = await EnsureCircuitAsync(modelConfigId, cancellationToken);
        if (entity.State == LlmCircuitState.Open &&
            entity.OpenedUntil.HasValue &&
            entity.OpenedUntil.Value <= DateTime.UtcNow)
        {
            // 熔断窗口到期后进入半开，允许网关做一次试探调用。
            entity.State = LlmCircuitState.HalfOpen;
            entity.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new LlmCircuitSnapshot(entity.State, entity.FailureCount, ToDateTimeOffset(entity.OpenedUntil));
    }

    public async Task RecordCircuitSuccessAsync(Guid modelConfigId, CancellationToken cancellationToken = default)
    {
        var entity = await EnsureCircuitAsync(modelConfigId, cancellationToken);
        entity.State = LlmCircuitState.Closed;
        entity.FailureCount = 0;
        entity.OpenedUntil = null;
        entity.LastSuccessAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordCircuitFailureAsync(
        Guid modelConfigId,
        int failureThreshold,
        int circuitBreakSeconds,
        CancellationToken cancellationToken = default)
    {
        var entity = await EnsureCircuitAsync(modelConfigId, cancellationToken);
        entity.FailureCount++;
        entity.LastFailureAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        if (entity.FailureCount >= Math.Max(1, failureThreshold))
        {
            // 连续失败达到阈值后打开熔断，后续路由会跳过该模型直到窗口结束。
            entity.State = LlmCircuitState.Open;
            entity.OpenedUntil = DateTime.UtcNow.AddSeconds(Math.Max(1, circuitBreakSeconds));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<LlmInvocationLogDto> SaveInvocationLogAsync(
        LlmInvocationLogRecord record,
        CancellationToken cancellationToken = default)
    {
        var entity = new LlmInvocationLogEntity
        {
            Id = record.Id,
            TenantId = record.TenantId,
            WorkspaceId = record.WorkspaceId,
            ApplicationId = record.ApplicationId,
            ModelConfigId = record.ModelConfigId,
            ModelConfigName = record.ModelConfigName,
            Provider = record.Provider,
            Model = record.Model,
            RequestJson = record.RequestJson,
            ResponseJson = record.ResponseJson,
            Status = record.Status,
            UsedFallback = record.UsedFallback,
            PromptTokens = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            TotalTokens = record.TotalTokens,
            DurationMs = record.DurationMs,
            ErrorMessage = record.ErrorMessage,
            CreatedAt = record.CreatedAt.UtcDateTime
        };

        dbContext.LlmInvocationLogs.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToLogDto(entity);
    }

    public async Task<IReadOnlyCollection<LlmInvocationLogDto>> ListInvocationLogsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        Guid? modelConfigId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var normalizedWorkspaceId = NormalizeOptional(workspaceId);
        var normalizedApplicationId = NormalizeOptional(applicationId);
        var query = dbContext.LlmInvocationLogs
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId.Trim());

        if (normalizedWorkspaceId is not null)
        {
            query = query.Where(log => log.WorkspaceId == normalizedWorkspaceId);
        }

        if (normalizedApplicationId is not null)
        {
            query = query.Where(log => log.ApplicationId == normalizedApplicationId);
        }

        if (modelConfigId.HasValue)
        {
            query = query.Where(log => log.ModelConfigId == modelConfigId.Value);
        }

        var logs = await query
            .OrderByDescending(log => log.CreatedAt)
            .Take(500)
            .ToArrayAsync(cancellationToken);

        return logs.Select(ToLogDto).ToArray();
    }

    async Task<Dictionary<Guid, LlmCircuitBreakerEntity>> LoadCircuitsAsync(
        Guid[] modelConfigIds,
        CancellationToken cancellationToken)
    {
        return await dbContext.LlmCircuitBreakers
            .AsNoTracking()
            .Where(circuit => modelConfigIds.Contains(circuit.ModelConfigId))
            .ToDictionaryAsync(circuit => circuit.ModelConfigId, cancellationToken);
    }

    async Task<LlmCircuitBreakerEntity> EnsureCircuitAsync(Guid modelConfigId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.LlmCircuitBreakers
            .FirstOrDefaultAsync(circuit => circuit.ModelConfigId == modelConfigId, cancellationToken);
        if (entity is not null)
        {
            return entity;
        }

        entity = DefaultCircuit(modelConfigId, DateTime.UtcNow);
        dbContext.LlmCircuitBreakers.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    static LlmModelConfigDto ToDto(LlmModelConfigEntity entity, LlmCircuitBreakerEntity? circuit)
    {
        var parameters = FromJson<Dictionary<string, string>>(entity.ParametersJson) ?? new Dictionary<string, string>();
        var credentials = FromJson<Dictionary<string, string>>(entity.CredentialsJson) ?? new Dictionary<string, string>();
        var safeCircuit = circuit ?? DefaultCircuit(entity.Id, DateTime.UtcNow);

        return new LlmModelConfigDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.Name,
            entity.Provider,
            entity.Model,
            entity.Endpoint,
            entity.DeploymentName,
            entity.Status,
            entity.Priority,
            entity.FallbackModelConfigId,
            entity.TimeoutSeconds,
            entity.FailureThreshold,
            entity.CircuitBreakSeconds,
            entity.PromptTokenPricePer1K,
            entity.CompletionTokenPricePer1K,
            parameters,
            credentials.Count > 0,
            new LlmCircuitStateDto(
                safeCircuit.State,
                safeCircuit.FailureCount,
                ToDateTimeOffset(safeCircuit.OpenedUntil),
                ToDateTimeOffset(safeCircuit.LastFailureAt),
                ToDateTimeOffset(safeCircuit.LastSuccessAt)),
            ToDateTimeOffset(entity.CreatedAt) ?? DateTimeOffset.UtcNow,
            ToDateTimeOffset(entity.UpdatedAt ?? entity.CreatedAt) ?? DateTimeOffset.UtcNow);
    }

    static LlmModelConfigRecord ToRecord(LlmModelConfigEntity entity)
    {
        return new LlmModelConfigRecord(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.Name,
            entity.Provider,
            entity.Model,
            entity.Endpoint,
            entity.DeploymentName,
            entity.Status,
            entity.Priority,
            entity.FallbackModelConfigId,
            entity.TimeoutSeconds,
            entity.FailureThreshold,
            entity.CircuitBreakSeconds,
            FromJson<Dictionary<string, string>>(entity.ParametersJson) ?? new Dictionary<string, string>(),
            FromJson<Dictionary<string, string>>(entity.CredentialsJson) ?? new Dictionary<string, string>());
    }

    static LlmInvocationLogDto ToLogDto(LlmInvocationLogEntity entity)
    {
        return new LlmInvocationLogDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.ModelConfigId,
            entity.ModelConfigName,
            entity.Provider,
            entity.Model,
            entity.Status,
            entity.UsedFallback,
            entity.PromptTokens,
            entity.CompletionTokens,
            entity.TotalTokens,
            entity.DurationMs,
            entity.ErrorMessage,
            ToDateTimeOffset(entity.CreatedAt) ?? DateTimeOffset.UtcNow);
    }

    static LlmCircuitBreakerEntity DefaultCircuit(Guid modelConfigId, DateTime now)
    {
        return new LlmCircuitBreakerEntity
        {
            ModelConfigId = modelConfigId,
            State = LlmCircuitState.Closed,
            FailureCount = 0,
            UpdatedAt = now
        };
    }

    static Dictionary<string, string> NormalizeDictionary(IReadOnlyDictionary<string, string>? values)
    {
        return values is null
            ? new Dictionary<string, string>()
            : values
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(item => item.Key.Trim(), item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    static void ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("LLM endpoint must be an absolute HTTP or HTTPS URL.");
        }
    }

    static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    static T? FromJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var dateTime = value.Value;
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return new DateTimeOffset(dateTime.ToUniversalTime());
    }
}
