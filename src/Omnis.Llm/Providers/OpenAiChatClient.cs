using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Omnis.Contracts.Llm;

namespace Omnis.Llm.Providers;

/// <summary>
/// OpenAI Chat Completions 兼容客户端，覆盖 OpenAI、Azure OpenAI、豆包火山方舟和兼容协议网关。
/// </summary>
internal sealed class OpenAiChatClient(IHttpClientFactory httpClientFactory) : ILlmProviderClient
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 执行一次非流式 Chat Completions 调用，并解析内容、finish reason 与 token 用量。
    /// </summary>
    public async Task<LlmProviderResult> CompleteAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateHttpRequest(request, stream: false);
        using var timeout = CreateTimeoutToken(request.Config, cancellationToken);
        var client = httpClientFactory.CreateClient(nameof(OpenAiChatClient));
        using var response = await client.SendAsync(message, timeout.Token);
        var payload = await response.Content.ReadAsStringAsync(timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LLM provider returned {(int)response.StatusCode}: {Trim(payload, 500)}");
        }

        return ParseCompletion(payload, request);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        LlmProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 兼容 OpenAI SSE 格式：只消费 data 行，忽略心跳和空行。
        using var message = CreateHttpRequest(request, stream: true);
        using var timeout = CreateTimeoutToken(request.Config, cancellationToken);
        var client = httpClientFactory.CreateClient(nameof(OpenAiChatClient));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            throw new InvalidOperationException($"LLM provider returned {(int)response.StatusCode}: {Trim(payload, 500)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                yield break;
            }

            var delta = TryReadStreamDelta(data);
            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    static HttpRequestMessage CreateHttpRequest(LlmProviderRequest request, bool stream)
    {
        var uri = BuildUri(request.Config);
        var message = new HttpRequestMessage(HttpMethod.Post, uri);
        var apiKey = GetCredential(request.Config, "apiKey");
        if (RequiresApiKey(request.Config.Provider) && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"LLM provider {request.Config.Provider} requires credentials.apiKey.");
        }

        if (request.Config.Provider == LlmProviderType.AzureOpenAI)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                message.Headers.TryAddWithoutValidation("api-key", apiKey);
            }
        }
        else if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var organization = GetCredential(request.Config, "organization");
        if (!string.IsNullOrWhiteSpace(organization))
        {
            message.Headers.TryAddWithoutValidation("OpenAI-Organization", organization);
        }

        var body = BuildBody(request, stream);
        message.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return message;
    }

    static Uri BuildUri(LlmModelConfigRecord config)
    {
        var endpoint = NormalizeEndpoint(config.Endpoint);
        if (config.Provider == LlmProviderType.AzureOpenAI)
        {
            // Azure OpenAI 的模型名对应 deployment，api-version 可通过 parameters.apiVersion 覆盖。
            var deployment = string.IsNullOrWhiteSpace(config.DeploymentName)
                ? config.Model
                : config.DeploymentName;
            var apiVersion = GetParameter(config, "apiVersion") ?? "2024-10-21";
            return new Uri($"{endpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}");
        }

        return new Uri($"{endpoint}/chat/completions");
    }

    static Dictionary<string, object?> BuildBody(LlmProviderRequest request, bool stream)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = ResolveProviderModel(request.Config),
            ["messages"] = request.Messages.Select(ToProviderMessage).ToArray(),
            ["stream"] = stream
        };

        if (request.Temperature.HasValue)
        {
            body["temperature"] = request.Temperature.Value;
        }

        if (request.MaxTokens.HasValue)
        {
            body["max_tokens"] = request.MaxTokens.Value;
        }

        foreach (var parameter in request.Parameters)
        {
            // 运行参数允许透传给 Provider，但避免覆盖协议关键字段。
            if (IsReservedParameter(parameter.Key))
            {
                continue;
            }

            body[parameter.Key] = CoerceParameter(parameter.Value);
        }

        return body;
    }

    static object ToProviderMessage(LlmChatMessage message)
    {
        var result = new Dictionary<string, object?>
        {
            ["role"] = ToProviderRole(message.Role),
            ["content"] = message.Content
        };

        if (!string.IsNullOrWhiteSpace(message.Name))
        {
            result["name"] = message.Name;
        }

        return result;
    }

    static LlmProviderResult ParseCompletion(string payload, LlmProviderRequest request)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var choice = root.GetProperty("choices")[0];
        var content = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var finishReason = choice.TryGetProperty("finish_reason", out var finish)
            ? finish.GetString()
            : null;

        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            promptTokens = ReadInt(usage, "prompt_tokens");
            completionTokens = ReadInt(usage, "completion_tokens");
            totalTokens = ReadInt(usage, "total_tokens");
        }

        if (totalTokens == 0)
        {
            // 部分本地或兼容网关不返回 usage，这里给出粗略估算，保证审计字段可用。
            promptTokens = EstimateTokens(string.Join(' ', request.Messages.Select(message => message.Content)));
            completionTokens = EstimateTokens(content);
            totalTokens = promptTokens + completionTokens;
        }

        return new LlmProviderResult(content, finishReason, promptTokens, completionTokens, totalTokens, payload);
    }

    static string? TryReadStreamDelta(string data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choices[0];
        if (!choice.TryGetProperty("delta", out var delta) ||
            !delta.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.GetString();
    }

    static CancellationTokenSource CreateTimeoutToken(
        LlmModelConfigRecord config,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 600)));
        return source;
    }

    static string ToProviderRole(LlmMessageRole role)
    {
        return role switch
        {
            LlmMessageRole.System => "system",
            LlmMessageRole.Assistant => "assistant",
            LlmMessageRole.Tool => "tool",
            _ => "user"
        };
    }

    static object CoerceParameter(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (int.TryParse(value, out var integer))
        {
            return integer;
        }

        if (double.TryParse(value, out var number))
        {
            return number;
        }

        return value;
    }

    static bool IsReservedParameter(string key)
    {
        return key.Equals("apiVersion", StringComparison.OrdinalIgnoreCase)
            || key.Equals("providerModel", StringComparison.OrdinalIgnoreCase)
            || key.Equals("stream", StringComparison.OrdinalIgnoreCase)
            || key.Equals("model", StringComparison.OrdinalIgnoreCase)
            || key.Equals("messages", StringComparison.OrdinalIgnoreCase);
    }

    static string ResolveProviderModel(LlmModelConfigRecord config)
    {
        if (config.Parameters.TryGetValue("providerModel", out var providerModel) &&
            !string.IsNullOrWhiteSpace(providerModel))
        {
            return NormalizeDoubaoModel(config.Provider, providerModel.Trim());
        }

        if (config.Provider == LlmProviderType.DoubaoArk)
        {
            // 管理页保留展示名，实际请求方舟时使用火山方舟的版本化模型 ID。
            return NormalizeDoubaoModel(config.Provider, config.Model);
        }

        return config.Model;
    }

    static string NormalizeDoubaoModel(LlmProviderType provider, string model)
    {
        if (provider != LlmProviderType.DoubaoArk)
        {
            return model;
        }

        return model switch
        {
            "Doubao-1.5-lite-32k" => "doubao-1-5-lite-32k-250115",
            "Doubao-1.5-pro-32k" => "doubao-1-5-pro-32k-250115",
            "doubao-1.5-lite-32k" => "doubao-1-5-lite-32k-250115",
            "doubao-1.5-pro-32k" => "doubao-1-5-pro-32k-250115",
            "doubao-1-5-lite-32k" => "doubao-1-5-lite-32k-250115",
            "doubao-1-5-pro-32k" => "doubao-1-5-pro-32k-250115",
            _ => model
        };
    }

    static bool RequiresApiKey(LlmProviderType provider)
    {
        return provider is LlmProviderType.OpenAI or LlmProviderType.AzureOpenAI or LlmProviderType.DoubaoArk;
    }

    static string NormalizeEndpoint(string endpoint)
    {
        var normalized = string.IsNullOrWhiteSpace(endpoint)
            ? "https://api.openai.com/v1"
            : endpoint.Trim();

        return normalized.TrimEnd('/');
    }

    static string? GetCredential(LlmModelConfigRecord config, string key)
    {
        return config.Credentials.TryGetValue(key, out var value) ? value : null;
    }

    static string? GetParameter(LlmModelConfigRecord config, string key)
    {
        return config.Parameters.TryGetValue(key, out var value) ? value : null;
    }

    static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    static int EstimateTokens(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? 0
            : Math.Max(1, (int)Math.Ceiling(value.Length / 4.0));
    }

    static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
