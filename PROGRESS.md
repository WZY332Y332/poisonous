# 项目状态与交接（Bili.Uwp 改造）

> 最后更新：2026-10-31
> 用途：跨会话交接。接手时先读本文件，再动代码。

## 一、目标与已确认决策

在 `Richasy/Bili.Uwp`（UWP 版，MIT）基础上改造：

1. **沿用现有 UI**，只重写数据/逻辑层（用户已确认）
2. 修复登录，并新增「Cookie 直接登录」
3. 点赞 / 投币 / 收藏可用
4. **收藏夹在主界面新增一个导航项**（用户已确认）
5. 后续可能：WebView 登录、后台驻留（UWP 做不到托盘，需 WinUI3）

## 二、仓库与构建

| 项 | 值 |
|---|---|
| 远端 | `git@github.com:WZY332Y332/poisonous.git`（public，main 分支） |
| 本地工作区 | `D:\10101010\AI\Bili.Uwp-main` |
| 部署密钥 | 私钥 `D:\10101010\AI\_keys\id_ed25519`（已配 `core.sshCommand`） |
| 构建 | GitHub Actions，`windows-2022`，x64 only，产物发布到 Release |

**关键：runner 必须钉 `windows-2022`。** `windows-latest` 已是 VS 18 镜像，不含 UWP 所需的 `Windows.winmd` 与 SDK 22000，会报几百个 `Windows.*` 命名空间缺失。

构建产物下载需经代理（GitHub Release CDN 被墙）：

```
https://ghproxy.net/<release_asset_url>
```

## 三、本机安装与日志

| 项 | 值 |
|---|---|
| 当前已装 | `Richasy.Bili 2.2310.2.5` |
| 包家族 | `Richasy.Bili_30bc4ykfppemj` |
| 日志目录 | `%LOCALAPPDATA%\Packages\Richasy.Bili_30bc4ykfppemj\LocalState\BiliDiag\bili-YYYYMMDD.log` |
| 签名证书 | `D:\10101010\windows11\wzy332.pfx` |
| 证书 Subject | `E=wzy332332@outlook.com|wzy332@tuta.io, CN=WZY332, O=LHKJ, C=CN` |

日志由 `Bili.Models.App/Other/AppLogger.cs` 写入，记录：状态机变化、Cookie 写入/回读、HTTP 请求（只记 path + 状态码 + 是否带 Cookie）、用户信息拉取失败。

## 四、已验证的接口契约（重要资产，勿重复试错）

用账号 `wzy_332`（mid `3546935694723239`）实测通过。

### 二维码登录

```
POST https://passport.bilibili.com/x/passport-tv-login/qrcode/auth_code
     参数: appkey=27eb53fc9058f8c3, local_id=<guid>, ts=<毫秒>, sign=md5(排序参数串+appSecret)
     返回: {"code":0,"data":{"url":"...","auth_code":"..."}}
POST https://passport.bilibili.com/x/passport-tv-login/qrcode/poll
     参数: appkey, auth_code, local_id, guid=<同一个 guid>, ts, sign
     状态: 86039 未确认 → 86090 已扫码 → 0 成功（返回 access_token + cookie_info）
```

**两个必须遵守的点：**

- **不要带 `build` 参数**。`ServiceConstants.BuildNumber = "5520400"` 是 2021 年的值，服务端会返回「版本过低，请升级至最新版」
- **`guid` 必须复用**，不能每次轮询重新生成

### Cookie 鉴权

```
GET https://api.bilibili.com/x/web-interface/nav   （带 Cookie 头）
     → {"code":0,"data":{"isLogin":true,"mid":...,"uname":"wzy_332"}}
```

**WinRT 的 `CookieManager` 在本应用里不可靠，且 `SetCookie` 的域带前导点（`.bilibili.com`）会抛 `0x800710DD`。**
现行方案：登录成功后把 Cookie 拼成字符串缓存，`GetCookieString()` 直接返回，请求时注入 `Cookie` 头，绕开容器。

### 视频信息

```
GET https://api.bilibili.com/x/web-interface/view?bvid=
     → 标题/UP主/时长/播放/点赞/投币/收藏 全字段可用
```

### 交互（写操作，均需 csrf=bili_jct）

```
POST https://api.bilibili.com/x/web-interface/archive/like      aid, like(1/0), csrf
POST https://api.bilibili.com/x/web-interface/coin/add          aid, multiply, select_like, csrf
POST https://api.bilibili.com/x/v3/fav/resource/deal            rid, type=2, add_media_ids, del_media_ids, csrf
```

存活验证方式：故意传错 csrf，返回 `{"code":-111,"message":"CSRF 校验失败"}` 即证明接口可达且只需 csrf。

### 收藏夹

```
GET https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid=<mid>
     → 默认收藏夹(id=3543992039, 5 项)、收藏夹(id=4008922639, 109 项)
```

### 已废弃（不要再试）

```
https://passport.bilibili.com/login/app/third   → {"code":20000,"message":"该版本已不支持当前功能"}
```

### 密码登录

```
GET  /api/oauth2/getKey    → code 0，返回 RSA 公钥 + salt（加密流程可用）
POST /x/passport-login/oauth2/login → code -105「验证码错误」，要求极验
```

**结论：密码登录被极验风控拦住**，现有代码遇 `-105` 会转去扫码，形成死循环。要支持「输账号密码」只能走 WebView 登录（未实现）。

## 五、已定位的根因（避免重踩）

| 现象 | 根因 | 状态 |
|---|---|---|
| 扫码后提示「版本过低」 | 登录请求携带 2021 年的 `build=5520400` | 已修（登录类型不带 build） |
| 扫码成功但提示「登录失败」 | 二维码走 `passport.snm0516.aisee.tv`（TV 旧镜像域） | 已改为 `passport.bilibili.com` |
| 弹窗关闭但仍未登录 | 请求从未带 Cookie（日志 `带Cookie=False`） | 已修（缓存 Cookie 字符串注入） |
| Cookie 登录报 `0x800710DD` | `SetCookie` 域带前导点 | 已修（域不带点 + 失败仅告警） |
| 登录成功立刻掉线 | gRPC 无 `access_key` 请求失败 → 被当成登录失败自动登出 | 已修（拉资料失败不再登出） |

## 六、已改动的文件

| 文件 | 改动 |
|---|---|
| `src/Models/Models.App/Other/AppLogger.cs` | 新增本地日志系统 |
| `src/App/App.xaml.cs` | 启动时初始化日志（路径 `LocalState\BiliDiag`） |
| `src/Models/Models.App/Constants/ApiConstants.cs` | 二维码接口改用正式域 |
| `src/Lib/SignIn.Uwp/AuthorizeProvider/AuthorizeProvider.cs` | `build` 按类型排除；Cookie 缓存优先；Cookie 模式状态判定；登出清理 |
| `src/Lib/SignIn.Uwp/AuthorizeProvider/AuthorizeProvider.Extension.cs` | Cookie 写入重写；`SignInWithCookieAsync`（Cookie 登录）；轮询日志 |
| `src/Lib/SignIn.Uwp/SignInDialog.xaml(.cs)` | 新增「使用 Cookie 登录」面板与错误详情展示 |
| `src/ViewModels/ViewModels.Uwp/Account/AccountViewModel/AccountViewModel.SignIn.cs` | 拉资料失败不自动登出 + 日志 |
| `src/Lib/Lib.Implementation/HttpProvider/HttpProvider.cs` | 请求日志 |
| `src/Lib/SignIn.Uwp/ModernAuth/ModernAuthService.cs` | **新增**：按实测接口重写的认证核心（待接入） |
| `Uwp.props` + 3 个 csproj | SDK 目标 22000 → 19041 |
| `.github/workflows/release-builder.yml` | 重写：windows-2022 / 关闭构建签名 / 发布 Release / 产物与日志可下载 |

## 七、待办（按优先级）

1. ~~**收藏夹导航项**~~ ✅ 已完成（2.2310.2.4）
   - 同时新增「稍后再看」「观看历史」入口
   - 未登录时先引导登录，成功后再打开
   - 复用既有浮层视图，未新增页面
2. ~~**核对现有交互接口**~~ ✅ 已完成
   - App 域 `like` / `coin/add` / `like/triple` 全部存活，且只校验 CSRF，无需替换
   - 真正的问题是 POST 请求不带 Cookie，已修
3. **工具箱扩充**（进行中）
   - ✅ 新增「登录 Cookie 备份」（复制到剪贴板）
   - 可继续加：诊断日志导出、封面包量下载
4. **WebView 登录**（解决极验，用户暂缓）
   - 复用 `SessionWebView`，加载 `https://passport.bilibili.com/login`
   - 检测跳回 `bilibili.com` 后提取 Cookie → 调用已验证的保存逻辑
5. **视频信息层重写**（未开始）
   - 端点已实测：`x/web-interface/view?bvid=` 字段齐全
   - 需映射到现有 `VideoDetail` 模型
6. **接入 `ModernAuthService`**（可选，替代现有认证内部实现，接口不变）

## 八、构建 → 签名 → 安装 操作手册

```powershell
# 1. 触发：push 到 main（纯 md 提交已配置为不触发）
# 2. 等待完成，从 Release 取产物（build-<run_number> 标签）
# 3. 下载（走代理，必须校验大小，曾多次被截断）
curl -sL "https://ghproxy.net/<asset_url>" -o App_x_x64.msixbundle
# 4. 重签名（脚本已备好）
powershell -NoProfile -ExecutionPolicy Bypass -File D:\10101010\AI\_dl\resign.ps1 -BundlePath <bundle>
# 5. 安装前务必先关闭应用，否则报 0x80073D02
Stop-Process -Name Bili.App -Force -ErrorAction SilentlyContinue
Add-AppxPackage -Path <signed> -DependencyPath <Dependencies\x64\*.appx>
```

`resign.ps1` 做的事：unbundle → 7 个包改 Publisher 为 wzy332 证书 Subject → 逐包签名 → 按原版本重组 bundle → 签 bundle → 验签。

## 九、环境坑位清单

| 坑 | 说明 |
|---|---|
| StyleCop 是**错误级** | 空行（SA1513/SA1515）、成员顺序（SA1202/SA1203）都会让构建失败。**public 成员必须全部排在 private 之前** |
| 老式 csproj 需显式登记 | 新增 `.cs` 必须写进 `<Compile Include>`，否则根本不参与编译（曾潜伏两版才暴露为 `CS0103`）；新增 `.xaml` 需同时登记 `<Page>` 与 `<Compile>` |
| `DeleteLocalSetting` 只收枚举 | 传字符串会 CS1503 |
| `/flp:...;Verbosity=...` 的分号 | Windows runner 默认 pwsh，分号是语句分隔符 → 用参数数组 |
| 构建日志/artifact 需登录 | 已改为发布到 Release（公开直链） |
| Release CDN 被墙 | 用 `ghproxy.net`，并校验文件大小 |
| 单文件改版本号要 3 处 | workflow 的 input default、BUILD_VERSION 回退值 |
| 安装前必须关应用 | 否则 0x80073D02 |
| 含中文的 `.ps1` 必须 UTF-8 **带 BOM** | 否则 PS 5.1 按 ANSI 解析，语法报错 |
| `ship.ps1` 的 `$RunId` 要用 `[long]` | GitHub run id 超出 Int32 |
| 抓 workflow 要筛名字 | `per_page=1` 常抓到 Release Drafter，需过滤 `Build UWP*` |
