using Omnis.Api.Endpoints;
using Omnis.EfCore.Npgsql;

var builder = WebApplication.CreateBuilder(args);

// 注册 OpenAPI 与 Npgsql 基础设施适配层。
// Npgsql 层统一提供知识管理、RAG 检索观测、对话引擎和 schema 初始化能力。
builder.Services.AddOpenApi();
builder.Services.AddOmnisNpgsqlInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 本地开发调试优先使用 HTTP，避免 Postman/Apifox 因开发证书未信任而报 SSL 证书错误。
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// 轻量健康检查，供本地 Docker 或反向代理探活使用。
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

// 挂载知识管理、RAG 问答和对话引擎 API。
app.MapKnowledgeEndpoints();
app.MapRagEndpoints();
app.MapConversationEndpoints();
app.MapChannelEndpoints();
app.MapLlmEndpoints();

app.Run();
