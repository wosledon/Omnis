using Microsoft.Extensions.DependencyInjection;
using Omnis.Llm.Providers;

namespace Omnis.Llm;

/// <summary>
/// LLM 网关核心服务依赖注入扩展。
/// </summary>
public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// 注册 LLM 网关应用服务和默认 OpenAI 兼容 Provider Client。
    /// </summary>
    public static IServiceCollection AddLlmGatewayCore(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddScoped<ILlmGateway, LlmGatewayService>();
        services.AddScoped<ILlmProviderClient, OpenAiChatClient>();

        return services;
    }
}
