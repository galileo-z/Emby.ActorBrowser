# Emby Actor Browser

一个尽量不侵入 Emby Web 客户端的演员浏览插件：插件在服务器内部通过 `ILibraryManager` 读取演员/作品，并提供一个独立 Web 页面。

## 功能

- 分页显示全部 Actor（过滤掉导演、编剧等 Person 类型），默认每页 120 位
- 搜索演员
- A-Z + “中文/其他”快速筛选
- 优先使用 Emby 的 `SortName` 排序，浏览器再用 `zh-CN Intl.Collator` 排序
- 演员头像
- 在演员列表中直接选择本地 JPG/PNG/WebP 替换演员主图
- 作品数量（能从当前 Emby `ItemCounts` 取到时显示）
- 点击演员显示其电影与剧集
- 点击作品回到 Emby 原生详情页
- 前端不保存 Emby API Key

## 项目结构

```
src/Emby.ActorBrowser/
  Emby.ActorBrowser.csproj
  Plugin.cs
  Models/ActorBrowserDtos.cs
  Services/ActorBrowserService.cs
  Web/actor-browser.html
```

## 1. 先确认 Emby Server 版本

在 Emby 管理后台 -> Dashboard/控制台查看版本号。

`Emby.ActorBrowser.csproj` 默认：

```xml
<EmbySdkVersion>4.9.1.90</EmbySdkVersion>
```

**推荐让 NuGet SDK 版本与服务器版本保持一致。** 如果你的服务器是 4.8.x，请把这里改成对应的 `MediaBrowser.Server.Core` 版本后再编译。

也可以编译时覆盖：

```bash
dotnet build -c Release -p:EmbySdkVersion=4.9.1.90
```

## 2. 编译

需要安装 .NET SDK，然后在项目目录执行：

```bash
cd src/Emby.ActorBrowser
dotnet restore
dotnet build -c Release
```

输出通常位于：

```text
bin/Release/netstandard2.0/Emby.ActorBrowser.dll
```

## 3. 安装

1. 关闭 Emby Server。
2. 将 `Emby.ActorBrowser.dll` 复制到 Emby 的 `plugins` 目录。
3. 启动 Emby Server。
4. 在 Dashboard -> Plugins 中确认出现 `Actor Browser`。

## 4. 打开演员页

你的服务器地址是 `192.168.1.111:8096`，优先尝试：

```text
http://192.168.1.111:8096/emby/ActorBrowser
```

如果你的反向代理/部署方式去掉了 `/emby` 前缀，再尝试：

```text
http://192.168.1.111:8096/ActorBrowser
```

插件 API：

```text
GET /emby/ActorBrowser/People?startIndex=0&limit=120&search=<可选>&group=<可选>
GET /emby/ActorBrowser/Items?personId=<InternalId>&startIndex=0&limit=500
```

## 安全说明

当前 0.1.12 是 LAN-first 版本，插件的自定义端点没有额外做独立身份认证。它不会返回媒体文件路径或播放 URL，但会返回演员名和库内电影/剧集元数据。

**如果 Emby 的 8096/8920 端口直接暴露公网，不建议直接部署此版本。** 下一步应给 ActorBrowser 路由增加访问控制，或者只允许反向代理后的已认证入口访问。

## 兼容性说明

Emby Server 插件 API 与 NuGet SDK 版本有关。项目按 4.9.1.90 API 写法准备，但没有在本次生成环境中实际执行 `dotnet build`（环境没有 .NET SDK）。如果编译出现 API 签名差异，优先检查：

- `MediaBrowser.Server.Core` 版本是否与服务器匹配
- `IHasResultFactory / IHttpResultFactory` 在你的 SDK 版本中的签名
- `ILibraryManager.GetPeople` / `GetItemsResult` 是否有版本差异

把编译错误贴出来即可按你的 Emby 版本调整。

## 后续可加

- 真正按中文拼音首字母 A-Z 分组
- 按媒体库过滤（只看电影库 / 电视剧库）
- 按作品数量排序
- 国家/年龄/性别等元数据过滤
- 收藏演员
- 管理后台配置访问权限


## 0.1.4 编译修复

- 补充 `using MediaBrowser.Controller.Net;`，用于 `IHasResultFactory` / `IHttpResultFactory`。
- 补充 `IRequest Request { get; set; }`，满足当前 `IHasResultFactory : IRequiresRequest` 接口要求。


## v0.1.4 compatibility fixes

- Added an explicit `System.Memory 4.6.0` compile reference required by the Emby 4.9.1.90 API surface (`ReadOnlySpan` / `ReadOnlyMemory`).
- Updated HTML responses to call `IHttpResultFactory.GetResult(Request, ReadOnlySpan<char>, contentType)`.
- Replaced `BaseItem.IdString` with `BaseItem.Id.ToString("N")` for SDK compatibility.
- Uses explicit LINQ `Count()` for collection totals.


## Authentication / API Key

Actor Browser keeps Emby authentication enabled. Create a dedicated API key in Emby Server Dashboard -> Advanced -> Security, then open:

```text
http://SERVER:8096/emby/ActorBrowser?api_key=YOUR_KEY
```

The page automatically propagates the key to its JSON and image requests. Do not expose or share the URL because the API key is a credential.


## v0.1.4 runtime fix

The HTML endpoint now uses the `IHttpResultFactory.GetResult(ReadOnlySpan<char>, string, ...)` overload that does not require `IRequest`. This avoids a runtime null request context on some Emby Server builds. Explicit initialization diagnostics were also added for `ResultFactory` and `ILibraryManager`.


## v0.1.5 runtime fix

- `IHttpResultFactory` is now injected through the service constructor, the same dependency-injection path already used by `ILibraryManager`.
- Removed reliance on Emby populating the `IHasResultFactory.ResultFactory` property at runtime. On the tested server path this property remained null even though the route itself loaded successfully.
- The HTML page is still returned with `text/html; charset=utf-8` through the official `IHttpResultFactory.GetResult(...)` API.


## v0.1.6 display fixes

- 演员头像区域不再放姓名或首字母文字；无头像时使用纯人物占位图形。演员姓名仍显示在封面下方。
- 演员作品页的电影/剧集海报改为 `object-fit: contain`，优先完整显示原始封面，不再按 2:3 卡片强制裁切。
- 图片成功载入后会隐藏底层占位内容，避免透明图片透出占位文字/图形。


## v0.1.8 横向封面自适应

- 演员作品页会在图片加载后读取原图宽高。
- 宽图（宽度 > 高度 × 1.15）自动切换为横向作品卡片，并在网格中占两列。
- 横向卡片按照图片真实宽高比显示，不再被固定 2:3 竖框限制。
- 竖版海报仍保持原来的竖向卡片布局。
- 作品图请求宽度提升到 720px，横向卡片显示更清晰。


## v0.1.9 演员照片编辑

- 演员卡片右上角增加“换图”按钮。
- 可从浏览器选择 JPG、PNG 或 WebP，本地图片会通过 Emby 官方 `POST /Items/{Id}/Images/Primary` 接口替换演员主图。
- 上传成功后自动刷新当前卡片图片并增加缓存破坏参数，无需手动刷新整个页面。
- Emby 的图片上传接口要求管理员权限；若 API Key/当前认证没有权限，页面会显示 401/403 权限提示。


## v0.1.10 分页按钮修复

- 修复演员作品页底部“加载更多”错误绑定到演员列表分页的问题。
- 全局分页按钮现在根据当前视图切换行为：演员列表显示“加载更多演员”，作品页显示“加载更多作品”。
- 演员作品改为每次加载 120 条，继续加载时保持在当前演员作品页，不再重新渲染演员列表。
- 增加 `.more[hidden] { display:none !important; }`，避免自定义 `.more { display:block }` 样式导致 `hidden` 状态失效。
- 返回演员列表时会按实际演员总数恢复分页按钮，而不是无条件显示。


## v0.1.11 演员列表分页

- 演员列表不再通过“加载更多演员”把所有演员累积到同一个页面。
- 默认每页显示 60 位演员。
- 底部提供“上一页 / 页码 / 下一页”，并始终提供第 1 页和最后一页入口。
- 搜索演员时由服务器先过滤再分页，搜索结果页码和总数保持正确。
- A-Z / 中文其他筛选改为服务器端过滤后再分页，切换筛选会自动回到第 1 页。
- 从演员作品页返回时保留原来的搜索条件、字母筛选和当前页。
- 作品页继续保留“加载更多作品”，不受演员分页改动影响。


## v0.1.12 演员分页优化

- 演员列表默认从每页 60 位提高到每页 120 位，显著减少总页数。
- 当总页数不超过 17 页时，底部直接展示全部页码。
- 总页数超过 17 页时，固定保留第 1 页和最后一页，并一次显示当前页附近最多 15 个连续中间页码。
- “上一页 / 下一页”、搜索、A-Z 筛选、返回后保留当前页等逻辑不变。
