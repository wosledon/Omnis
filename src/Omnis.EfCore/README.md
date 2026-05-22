# Omnis EfCore

> 基于 `Entity Framework Core` 的数据库访问层

## 特性

- ✅ 单元OfWork 模式实现
- ✅ 事务管理与支持事务嵌套
- ✅ 软删除支持
- ✅ 审计功能（创建/修改人、时间）
- ✅ 版本控制（并发令牌）
- ✅ 批量操作支持

## 安装

```bash
dotnet add package Omnis.EfCore
```

## 配置

在 `Program.cs` 中注册：

```csharp
builder.Services.AddEfCore<MyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

## 使用示例

### 基础操作

```csharp
// 添加
await unitOfWork.AddAsync(entity);

// 批量添加
await unitOfWork.AddRangeAsync(entities);

// 更新
await unitOfWork.UpdateAsync(entity);

// 批量更新
await unitOfWork.UpdateRangeAsync(entities);

// 软删除
await unitOfWork.DeleteAsync(entity);

// 批量软删除
await unitOfWork.DeleteRangeAsync(entities);

// 查询
var query = unitOfWork.Q<Entity>();

// 提交
await unitOfWork.CommitAsync();
```

### 事务管理

#### 方式一：手动管理

```csharp
await using var tx = await unitOfWork.BeginTransactionAsync();

try
{
    await unitOfWork.AddAsync(entity1);
    await unitOfWork.AddAsync(entity2);
    await unitOfWork.CommitAsync(); // 保存更改
    
    await tx.CommitAsync(); // 提交事务
}
catch
{
    await tx.RollbackAsync(); // 回滚事务
    throw;
}
```

#### 方式二：使用扩展方法

```csharp
// 无返回值
await unitOfWork.WithTransactionAsync(async (uow, ct) =>
{
    await uow.AddAsync(entity1);
    await uow.AddAsync(entity2);
});

// 有返回值
var result = await unitOfWork.WithTransactionAsync(async (uow, ct) =>
{
    await uow.AddAsync(entity);
    return entity.Id;
});
```

#### 方式三：嵌套事务

```csharp
await using var outerTx = await unitOfWork.BeginTransactionAsync();
{
    // 外层事务操作
    
    await using var innerTx = await unitOfWork.BeginTransactionAsync();
    {
        // 内层事务操作
        await innerTx.CommitAsync(); // 内层提交（仅减少引用计数）
    }
    
    await outerTx.CommitAsync(); // 外层提交（真正提交到数据库）
}
```

## 实体配置

### 软删除实体

```csharp
public class Entity : ISoftDeleteEntity
{
    public bool IsDeleted { get; set; }
}
```

### 审计实体

```csharp
public class Entity : IAuditableEntity
{
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 版本控制实体

```csharp
public class Entity : IVersionable
{
    public byte[] RowVersion { get; set; } = null!;
}
```

## DATABASE.md

...

继续编辑README...

---

## 进阶用法

### 查询已删除的实体

```csharp
// 包含已删除实体
var allEntities = dbContext.WithDeleted<Entity>();

// 仅查询已删除的实体
var deletedEntities = dbContext.OnlyDeleted<Entity>();
```

### 自定义审计上下文

实现 `IAuditContextProvider` 接口：

```csharp
public class CustomAuditContextProvider : IAuditContextProvider
{
    public string? GetCurrentUserId()
    {
        // 从当前用户获取ID
        return "user-id";
    }
}
```

## LICENSE

MIT
