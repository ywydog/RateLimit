# ClassIsland.RateLimit

> ClassIsland 限频规则插件：限制一个自动化在一定时间内的最多运行次数。

为 ClassIsland 自动化系统补充三种**限频规则**与一个**配套记录行动**，让"上课一次、下课一次、整点一次"等节流需求可以直接在规则集里配置。

> 🎓 **新手入门**：[docs/tutorial.html](./docs/tutorial.html) — 一份面向普通同学的可视化教程，看完 5 分钟即可上手。

## 兼容性

- ClassIsland 2.x（≥ 2.0.0）
- 操作系统：Windows / macOS / Linux（取决于宿主平台）

## 功能概览

### 三条限频规则

| 规则 ID | 名称 | 触发语义 |
|---|---|---|
| `classisland.plugin.ratelimit.interval` | 限频：时间间隔 | 距上次执行 ≥ 触发间隔 且 窗口内执行次数 < 最大次数 时放行 |
| `classisland.plugin.ratelimit.timePoint` | 限频：时间点 | 当前时间在任一配置时间点（HH:mm）± 1 分钟内 且 窗口内执行次数 < 最大次数 时放行 |
| `classisland.plugin.ratelimit.timeRange` | 限频：时间段 | 当前时间在配置的 `[开始, 结束]` 区间内（支持跨天，如 `22:00-06:00`）且 窗口内执行次数 < 最大次数 时放行 |

每条规则共享两个基础设置：

- **最大次数**：时间窗口内允许的最大执行次数（默认 `3`）
- **时间窗口**：限频时间窗口，单位秒（默认 `600`）

### 一个记录行动

| 行动 ID | 名称 | 用途 |
|---|---|---|
| `classisland.plugin.ratelimit.record` | 记录限频执行 | 放在工作流末尾，让服务在动作真执行后再记一次（仅在确实执行时计入历史） |

> 规则放行时会**自动入列**；如果同时在工作流末尾加了这个行动，会在动作真执行后再入列一次，**这意味着双重计数**。一般二选一：
>
> - 只配规则：每次放行记一次
> - 只配记录行动：每次实际执行记一次（需配合其他放行条件）

## 安装

从 [Releases](https://github.com/ywydog/RateLimit/releases) 下载最新的 `ClassIsland.RateLimit.cipx`，在 ClassIsland 中通过 **设置 → 插件 → 安装本地插件** 选择该文件即可。

或者从源码构建：

```bash
dotnet publish -p:CreateCipx=true --configuration Release
# 产物位于 bin/Release/net8.0/cipx/ClassIsland.RateLimit.cipx
```

## 使用示例

### 场景 1：每 5 分钟最多响铃 1 次

新增一条规则 `限频：时间间隔`：

- 最大次数：`1`
- 时间窗口：`300`（秒）
- 触发间隔：`60`（秒，避免连击）

### 场景 2：08:00 / 12:00 / 18:00 整点最多提醒 2 次

新增一条规则 `限频：时间点`：

- 最大次数：`2`
- 时间窗口：`3600`（秒）
- 时间点：`08:00,12:00,18:00`

### 场景 3：晚自习 19:00-21:30 内最多播报 5 次

新增一条规则 `限频：时间段`：

- 最大次数：`5`
- 时间窗口：`9000`（秒）
- 开始时间：`19:00`
- 结束时间：`21:30`

## 架构

```
┌────────────────────────────────────────────────────────────────┐
│                ClassIsland Automation Pipeline                │
└────────────────────────────────────────────────────────────────┘
        │
        ▼
┌─────────────────┐    反查    ┌────────────────────────────┐
│  RulesetService │ ─────────► │  RateLimitService          │
│  调用 Handle()  │            │  • ResolveWorkflowId       │
└─────────────────┘            │  • IsInMode (多态)         │
        │                      │  • 加载 / 裁剪 / 计数      │
        ▼                      └────────────┬───────────────┘
┌─────────────────┐                           │
│  IRateLimit     │ ◄───── Singleton ────────│
│  Service        │                           ▼
└────────┬────────┘              ┌──────────────────────────────┐
         │                       │ GlobalStorageService         │
         │  Record()             │ key=classisland.ratelimit.   │
         ▼                       │       history.<workflowGuid> │
┌─────────────────┐              │ value=List<DateTime> JSON    │
│ RateLimitRecord │              └──────────────────────────────┘
│ Action          │
└─────────────────┘
```

要点：

- `RateLimitBaseSettings` 是抽象基类，三个模式各自派生；模式判断下沉到 `IsInMode` 多态分发
- `CanExecute` 在放行时自动入列；`Record` 仅入列不动判断（供"实际执行"场景用）
- 工作流 GUID 通过反向查找（`ReferenceEquals`）惰性建立并缓存，**未命中不缓存**，避免冷启动时永远返 `Guid.Empty`
- 持久化层加 `lock` 防并发读写损坏 JSON
- `Record` 兜底 30 天裁剪，避免无主 history 无限增长

完整时序图见 [FLOWCHART.md](./FLOWCHART.md)。

## 日志

启动后可在 ClassIsland 日志中观察到以下关键事件（按使用频率从高到低）：

| 级别 | 触发条件 | 示例消息 |
|---|---|---|
| Information | 插件初始化 | `ClassIsland.RateLimit 插件初始化完成：已注册 3 条规则…` |
| Information | 限频拦截（窗口已满） | `限频拦截：窗口内已执行 3/3 次。模式=时间间隔，工作流=…` |
| Information | 手动重置历史 | `Reset：已清除工作流限频历史。工作流=…` |
| Warning | 找不到 settings 所属工作流 | `CanExecute：无法定位 settings 所属工作流（模式=…），默认放行。` |
| Warning | 收到非限频 settings | `收到非 RateLimitBaseSettings 类型的 settings（实际类型：…），按放行处理。` |
| Debug | 每次 Handle / Record | `限频规则 Handle 调用：模式=时间间隔，结果=放行` |
| Trace | 缓存命中 | `ResolveWorkflowId 命中缓存：模式=…，工作流=…` |

> 想看更细的诊断信息可在 ClassIsland 日志配置中把本插件的日志级别调到 `Debug` 或 `Trace`。

## 开发

### 环境要求

- .NET SDK 8.0
- Windows / macOS / Linux 任一

### 本地构建

```bash
dotnet restore
dotnet build
dotnet publish -p:CreateCipx=true --configuration Release
```

CI 工作流定义在 [.github/workflows/dotnet-build.yml](./.github/workflows/dotnet-build.yml)，每次 push / PR 自动构建并上传 `cipx` 产物。
