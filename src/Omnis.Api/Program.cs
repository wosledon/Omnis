using Omnis.Api.Endpoints;
using Omnis.EfCore.Npgsql.Services;

var builder = WebApplication.CreateBuilder(args);

// 注册 OpenAPI 和知识管理模块；知识模块默认使用 PostgreSQL 持久化。
builder.Services.AddOpenApi();
builder.Services.AddPostgresKnowledgeManagement(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 本地开发调试优先使用 HTTP，避免 Postman/Apifox 因开发证书未信任而报 SSL 证书错误。
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// 轻量健康检查，供本地 Docker/反向代理探活使用。
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

// 挂载 PRD M1 知识管理相关接口。
app.MapKnowledgeEndpoints();

app.Run();
