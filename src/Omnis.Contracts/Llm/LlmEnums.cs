namespace Omnis.Contracts.Llm;

/// <summary>
/// LLM 服务提供方类型。MVP 优先覆盖 OpenAI、Azure OpenAI 和 OpenAI 兼容接口。
/// </summary>
public enum LlmProviderType
{
    /// <summary>OpenAI 官方 Chat Completions 接口。</summary>
    OpenAI = 0,
    /// <summary>Azure OpenAI 部署接口。</summary>
    AzureOpenAI = 1,
    /// <summary>兼容 OpenAI Chat Completions 协议的第三方或本地网关。</summary>
    OpenAICompatible = 2,
    /// <summary>Ollama 本地模型服务，预留给 v0.5 之后接入。</summary>
    Ollama = 10,
    /// <summary>vLLM 本地或私有化部署服务，预留给 v0.5 之后接入。</summary>
    Vllm = 11,
    /// <summary>Text Generation Inference 服务，预留给 v0.5 之后接入。</summary>
    Tgi = 12
}

/// <summary>
/// 模型配置生命周期状态。
/// </summary>
public enum LlmModelStatus
{
    /// <summary>草稿，尚不参与路由。</summary>
    Draft = 0,
    /// <summary>启用，可参与模型路由。</summary>
    Active = 1,
    /// <summary>停用，保留配置但不再参与调用。</summary>
    Disabled = 2,
    /// <summary>归档，表示历史配置。</summary>
    Archived = 3
}

/// <summary>
/// 发送给模型提供方的消息角色。
/// </summary>
public enum LlmMessageRole
{
    /// <summary>系统提示词。</summary>
    System = 0,
    /// <summary>用户消息。</summary>
    User = 1,
    /// <summary>模型助手消息。</summary>
    Assistant = 2,
    /// <summary>工具调用消息，供后续 function/tool calling 扩展。</summary>
    Tool = 3
}

/// <summary>
/// LLM 调用审计日志中的调用结果状态。
/// </summary>
public enum LlmInvocationStatus
{
    /// <summary>主模型调用成功。</summary>
    Succeeded = 0,
    /// <summary>模型调用失败。</summary>
    Failed = 1,
    /// <summary>主模型失败后，备用模型调用成功。</summary>
    FallbackSucceeded = 2,
    /// <summary>调用被取消。</summary>
    Cancelled = 3
}

/// <summary>
/// 每个模型配置的基础熔断状态。
/// </summary>
public enum LlmCircuitState
{
    /// <summary>关闭熔断，模型可正常调用。</summary>
    Closed = 0,
    /// <summary>打开熔断，暂时跳过该模型。</summary>
    Open = 1,
    /// <summary>半开状态，熔断窗口结束后允许试探性调用。</summary>
    HalfOpen = 2
}
