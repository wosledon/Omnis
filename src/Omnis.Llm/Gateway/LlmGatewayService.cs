using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Omnis.Contracts.Llm;

namespace Omnis.Llm;

/// <summary>
/// LLM 网关默认实现，负责模型路由、熔断判断、备用模型降级和调用审计。
/// </summary>
internal sealed class LlmGatewayService(
    ILlmGatewayStore store,
    ILlmProviderClient providerClient
) : ILlmGateway
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<LlmModelConfigDto> CreateModelConfigAsync(
        CreateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        return store.CreateModelConfigAsync(request, cancellationToken);
    }

    public Task<IReadOnlyCollection<LlmModelConfigDto>> ListModelConfigsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        LlmModelStatus? status,
        CancellationToken cancellationToken = default)
    {
        return store.ListModelConfigsAsync(tenantId, workspaceId, applicationId, status, cancellationToken);
    }

    public Task<LlmModelConfigDto?> GetModelConfigAsync(Guid modelConfigId, CancellationToken cancellationToken = default)
    {
        return store.GetModelConfigAsync(modelConfigId, cancellationToken);
    }

    public Task<LlmModelConfigDto?> UpdateModelConfigAsync(
        Guid modelConfigId,
        UpdateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        return store.UpdateModelConfigAsync(modelConfigId, request, cancellationToken);
    }

    public Task<LlmModelConfigDto?> DisableModelConfigAsync(Guid modelConfigId, CancellationToken cancellationToken = default)
    {
        return store.DisableModelConfigAsync(modelConfigId, cancellationToken);
    }

    public async Task<LlmCompletionResponse> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCompletionRequest(request);
        var candidates = await ResolveRouteAsync(request, cancellationToken);
        Exception? lastError = null;
        var failureSummaries = new List<string>();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(await BuildNoCandidateMessageAsync(request, cancellationToken));
        }

        // 候选模型已经按应用优先级和 fallback 链路排好序，这里顺序尝试直到成功或全部失败。
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (await IsCircuitOpenAsync(candidate, cancellationToken))
            {
                failureSummaries.Add(FormatCircuitOpenMessage(candidate));
                continue;
            }

            var usedFallback = index > 0 || (request.ModelConfigId.HasValue && candidate.Id != request.ModelConfigId.Value);
            try
            {
                return await InvokeCandidateAsync(candidate, request, usedFallback, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                failureSummaries.Add(FormatFailureMessage(candidate, ex));
                await store.RecordCircuitFailureAsync(
                    candidate.Id,
                    candidate.FailureThreshold,
                    candidate.CircuitBreakSeconds,
                    cancellationToken);

                await SaveFailureLogAsync(candidate, request, usedFallback, ex, cancellationToken);
            }
        }

        var message = failureSummaries.Count == 0
            ? "No available LLM model configuration could complete the request."
            : $"No available LLM model configuration could complete the request. Failures: {string.Join(" | ", failureSummaries)}";

        throw new InvalidOperationException(message, lastError);
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        LlmCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 当前对外提供稳定的 SSE 协议形态；底层 Provider 原生流式可在后续替换到这里。
        var response = await CompleteAsync(request, cancellationToken);
        foreach (var token in SplitForCompatStream(response.Content))
        {
            yield return new LlmStreamChunk(response.InvocationId, response.ModelConfigId, token, false);
        }

        yield return new LlmStreamChunk(response.InvocationId, response.ModelConfigId, string.Empty, true, response.FinishReason);
    }

    public Task<IReadOnlyCollection<LlmInvocationLogDto>> ListInvocationLogsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        Guid? modelConfigId,
        CancellationToken cancellationToken = default)
    {
        return store.ListInvocationLogsAsync(tenantId, workspaceId, applicationId, modelConfigId, cancellationToken);
    }

    async Task<LlmCompletionResponse> InvokeCandidateAsync(
        LlmModelConfigRecord candidate,
        LlmCompletionRequest request,
        bool usedFallback,
        CancellationToken cancellationToken)
    {
        var invocationId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var providerRequest = new LlmProviderRequest(
            candidate,
            request.Messages,
            request.Temperature,
            request.MaxTokens,
            MergeParameters(candidate.Parameters, request.Parameters));

        var result = await providerClient.CompleteAsync(providerRequest, cancellationToken);
        stopwatch.Stop();

        await store.RecordCircuitSuccessAsync(candidate.Id, cancellationToken);

        var status = usedFallback ? LlmInvocationStatus.FallbackSucceeded : LlmInvocationStatus.Succeeded;
        var log = await store.SaveInvocationLogAsync(
            new LlmInvocationLogRecord(
                invocationId,
                request.TenantId.Trim(),
                request.WorkspaceId.Trim(),
                NormalizeOptional(request.ApplicationId),
                candidate.Id,
                candidate.Name,
                candidate.Provider,
                candidate.Model,
                ToJson(new { request.Messages, request.Temperature, request.MaxTokens, request.Metadata }),
                result.RawJson,
                status,
                usedFallback,
                result.PromptTokens,
                result.CompletionTokens,
                result.TotalTokens,
                stopwatch.ElapsedMilliseconds,
                null,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return new LlmCompletionResponse(
            log.Id,
            candidate.Id,
            candidate.Name,
            candidate.Provider,
            candidate.Model,
            result.Content,
            result.FinishReason,
            result.PromptTokens,
            result.CompletionTokens,
            result.TotalTokens,
            log.DurationMs,
            status,
            usedFallback,
            null,
            log.CreatedAt);
    }

    async Task SaveFailureLogAsync(
        LlmModelConfigRecord candidate,
        LlmCompletionRequest request,
        bool usedFallback,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await store.SaveInvocationLogAsync(
            new LlmInvocationLogRecord(
                Guid.NewGuid(),
                request.TenantId.Trim(),
                request.WorkspaceId.Trim(),
                NormalizeOptional(request.ApplicationId),
                candidate.Id,
                candidate.Name,
                candidate.Provider,
                candidate.Model,
                ToJson(new { request.Messages, request.Temperature, request.MaxTokens, request.Metadata }),
                ToJson(new { error = exception.Message, exceptionType = exception.GetType().Name }),
                LlmInvocationStatus.Failed,
                usedFallback,
                0,
                0,
                0,
                0,
                exception.Message,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    async Task<IReadOnlyList<LlmModelConfigRecord>> ResolveRouteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = await store.ListRouteCandidatesAsync(
            request.TenantId.Trim(),
            request.WorkspaceId.Trim(),
            NormalizeOptional(request.ApplicationId),
            request.ModelConfigId,
            cancellationToken);

        if (candidates.Count == 0)
        {
            return Array.Empty<LlmModelConfigRecord>();
        }

        var result = new List<LlmModelConfigRecord>();
        var seen = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            // 先加入主候选，再展开 fallback 链，并用 seen 防止误配置造成环路。
            AddUnique(result, seen, candidate);
            await AddFallbackChainAsync(result, seen, candidate, cancellationToken);
        }

        return result;
    }

    async Task AddFallbackChainAsync(
        List<LlmModelConfigRecord> result,
        HashSet<Guid> seen,
        LlmModelConfigRecord candidate,
        CancellationToken cancellationToken)
    {
        var nextId = candidate.FallbackModelConfigId;
        while (nextId.HasValue && !seen.Contains(nextId.Value))
        {
            var fallback = await store.GetModelConfigRecordAsync(nextId.Value, cancellationToken);
            if (fallback is null || fallback.Status != LlmModelStatus.Active)
            {
                break;
            }

            AddUnique(result, seen, fallback);
            nextId = fallback.FallbackModelConfigId;
        }
    }

    async Task<bool> IsCircuitOpenAsync(LlmModelConfigRecord candidate, CancellationToken cancellationToken)
    {
        var circuit = await store.GetCircuitAsync(candidate.Id, cancellationToken);
        return circuit.State == LlmCircuitState.Open
            && circuit.OpenedUntil.HasValue
            && circuit.OpenedUntil.Value > DateTimeOffset.UtcNow;
    }

    static void AddUnique(
        List<LlmModelConfigRecord> result,
        HashSet<Guid> seen,
        LlmModelConfigRecord candidate)
    {
        if (seen.Add(candidate.Id))
        {
            result.Add(candidate);
        }
    }

    static IReadOnlyDictionary<string, string> MergeParameters(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var result = new Dictionary<string, string>(baseline, StringComparer.OrdinalIgnoreCase);
        if (overrides is null)
        {
            return result;
        }

        foreach (var item in overrides)
        {
            if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            {
                result[item.Key.Trim()] = item.Value.Trim();
            }
        }

        return result;
    }

    static void ValidateCompletionRequest(LlmCompletionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        if (request.Messages.Count == 0 || request.Messages.All(message => string.IsNullOrWhiteSpace(message.Content)))
        {
            throw new ArgumentException("At least one message with content is required.");
        }
    }

    static IEnumerable<string> SplitForCompatStream(string value)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token + " ";
        }
    }

    static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    async Task<string> BuildNoCandidateMessageAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = BuildScopeDescription(request);
        if (!request.ModelConfigId.HasValue)
        {
            return $"No active LLM model configuration matched the request scope. {scope}";
        }

        var selected = await store.GetModelConfigAsync(request.ModelConfigId.Value, cancellationToken);
        if (selected is null)
        {
            return $"No active LLM model configuration matched the request scope. {scope} Selected modelConfigId={request.ModelConfigId.Value} was not found.";
        }

        if (selected.Status != LlmModelStatus.Active)
        {
            return $"No active LLM model configuration matched the request scope. {scope} Selected model '{selected.Name}' is {selected.Status}.";
        }

        return $"No active LLM model configuration matched the request scope. {scope} Selected model '{selected.Name}' is active but was excluded by route filters.";
    }

    static string BuildScopeDescription(LlmCompletionRequest request)
    {
        var applicationId = string.IsNullOrWhiteSpace(request.ApplicationId) ? "<null>" : request.ApplicationId.Trim();
        return $"tenantId='{request.TenantId.Trim()}', workspaceId='{request.WorkspaceId.Trim()}', applicationId='{applicationId}'.";
    }

    static string FormatFailureMessage(LlmModelConfigRecord candidate, Exception exception)
    {
        var detail = exception.Message.ReplaceLineEndings(" ").Trim();
        if (detail.Length > 180)
        {
            detail = detail[..180] + "...";
        }

        return $"{candidate.Name} [{candidate.Provider}/{candidate.Model}] -> {exception.GetType().Name}: {detail}";
    }

    static string FormatCircuitOpenMessage(LlmModelConfigRecord candidate)
    {
        return $"{candidate.Name} [{candidate.Provider}/{candidate.Model}] -> circuit open";
    }

    static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
