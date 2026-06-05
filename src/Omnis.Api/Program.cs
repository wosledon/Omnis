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

// 暴露 wwwroot 下的轻量管理页面，当前用于 LLM 网关模型配置和联调。
app.UseStaticFiles();

// 轻量健康检查，供本地 Docker 或反向代理探活使用。
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.MapGet("/admin/llm", () => Results.Redirect("/admin/llm.html", permanent: false));
app.MapGet("/admin/knowledge", () => Results.Redirect("/admin/knowledge.html", permanent: false));
app.MapGet("/admin/chat", () => Results.Redirect("/admin/chat.html", permanent: false));

// 挂载知识管理、RAG 问答和对话引擎 API。
app.MapKnowledgeEndpoints();
app.MapRagEndpoints();
app.MapConversationEndpoints();
app.MapChannelEndpoints();
app.MapLlmEndpoints();

app.Run();
