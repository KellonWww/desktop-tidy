# Desktop Tidy · 桌面整理

轻量 Windows 桌面图标整理工具：分类工作区、搜索、拖动归类与自定义排列。
当前为 **预览版、免安装 ZIP**，没有 MSI / Setup 安装程序。

## 下载与安装

1. 打开 [Releases](https://github.com/2432448868/desktop-tidy/releases)，选择最新预览版。
2. 下载 **DesktopTidy-v0.1.0-preview-win-x64.zip**，不要下载 Source code 压缩包。
3. 右键 ZIP → 全部解压，将整个目录放在固定位置，例如 `D:/Apps/DesktopTidy`。
4. 双击 `DesktopTidy.exe`，保留同目录的 `DesktopTidy.exe.config`。
5. 可右键 EXE 创建快捷方式；不需要安装 Python 或开发工具。

| 项目 | 要求 / 说明 |
|---|---|
| 系统 | 64 位 Windows 10 / 11；目前主要在 Windows 11 验证，Windows 10 兼容性待验收 |
| 运行环境 | .NET Framework 4.8 或兼容的更新版本；缺失时安装官方运行时，不是开发用 Targeting Pack |
| 桌面 | Windows Explorer 原生桌面；其他桌面替代软件兼容性未验证 |
| 权限 | 通常不需要管理员运行 |
| 分发 | ZIP 不包含测试程序、采样器、用户配置或桌面数据 |
| 签名 | 当前无代码签名；请核对下载来源与 SHA-256，不要关闭系统安全防护 |

.NET Framework 官方下载：https://dotnet.microsoft.com/download/dotnet-framework/net48

### 校验下载文件

Releases 提供 `SHA256SUMS.txt`。在 Windows 命令提示符执行下列命令，比对 ZIP 哈希：

```bat
certutil -hashfile DesktopTidy-v0.1.0-preview-win-x64.zip SHA256
```

## 第一次使用

1. 启动后扫描桌面，进入分类工作区；首次引导默认保留原生桌面图标。
2. 想只看整理工作区，可在引导中勾选隐藏原生图标，也可之后在设置中切换。
3. 双击工作区图标打开原文件；拖到另一分类框只改变工作区归属。
4. 拖动分类标题调整位置；通过分类框按钮折叠、展开或排列图标。
5. 文件增加、删除或重命名后，点击 **刷新**。

| 操作 | 使用方式 |
|---|---|
| 搜索 | 左栏按名称过滤；Ctrl+F 聚焦，Esc 清空搜索 |
| 分类排序 | 数量排序切换升序 / 降序；刷新不覆盖自定义排列 |
| 撤销 | Ctrl+Z 撤销最近一次支持撤销的布局 / 设置操作；搜索框内保留文本撤销 |
| 自定义分类 | 在设置 / 分类框右键菜单中管理分类，支持空分类显示 |
| 不透明度 | 设置中选择 25%、50%、75%、100%；完全隐藏使用独立收起入口 |
| 收起工作区 | 点击收起并确认；程序保留在托盘，原生图标恢复操作前状态 |
| 重新显示 | 托盘右键 → 显示工作区；图标可能在任务栏 ↑ 中 |
| 完全退出 | 托盘右键 → 退出；退出时请求显示原生桌面图标，并确认结果 |

### 文件与图标保护

- C# 版本整理图标的显示与归属，不移动、删除桌面原文件。
- 收起工作区恢复原显隐，完全退出显示原生图标；显隐不混入布局撤销。
- 隐藏前启动恢复守护进程，程序异常结束后守护请求显示原生图标。
- Explorer 临时重建桌面窗口时，退出恢复重新定位并有限重试。
- 预览版尚未完成所有真实桌面、多屏 DPI、Explorer 重启及长期驻留验收，建议小范围试用。

## 更新与卸载

**更新：** 托盘右键退出旧版，再解压运行新版；不要覆盖正在运行的 EXE。
程序使用单实例锁，旧版未退出时再次运行不会打开第二个实例。

**卸载：** 完全退出后删除解压目录和手动创建的快捷方式。
清除个人设置时，再手动删除 `%LOCALAPPDATA%/DesktopTidy`；将丢失布局、分类、备份和日志。

## 配置与常见问题

- 布局、撤销与图标位置快照：`%LOCALAPPDATA%/DesktopTidy/Workspace/`。
- 错误日志：`%LOCALAPPDATA%/DesktopTidy/Prototype/error.log`，发生错误时生成。
- 配置和快照可能含文件名、完整路径；反馈前脱敏，不要公开上传。
- **缺少 .NET：** 安装 .NET Framework 4.8 运行时后重试。
- **双击没反应：** 检查托盘是否有旧版，退出旧版再启动；仍失败时查看错误日志。
- **收起后找不到：** 在任务栏 ↑ 中查找托盘图标，右键显示工作区。
- **退出后图标仍未显示：** 桌面右键 → 查看 → 确认显示桌面图标；反馈系统版本、退出方式和脱敏日志。
- **安全软件拦截：** 确认仓库来源并校验哈希；当前未签名，不建议绕过明确的恶意软件告警。

## 开发与构建

主程序为 **C# 5 + WinForms + .NET Framework 4.8，Release x64**，无第三方 UI 依赖。
`tidy/` 是保留的早期 Python 草稿，不是发布版的启动入口。

使用 Visual Studio / MSBuild 与 .NET Framework 4.8 Targeting Pack：

```bat
msbuild DesktopTidy.sln /p:Configuration=Release /p:Platform=x64
```

或使用 Git Bash 与系统 Framework x64 编译器，警告视为错误：

```bash
BUILD_DIR=build/release bash scripts/build.sh
python scripts/package-release.py --build-dir build/release --version v0.1.0-preview
```

构建生成主程序与开发诊断工具，不会自动运行项目单元测试。
打包只纳入主程序、运行时配置和说明；ZIP 与 SHA-256 校验文件输出到 `dist/`。

| 目录 | 内容 |
|---|---|
| `src/DesktopTidy/` | 主程序、分类布局、Explorer 交互 |
| `scripts/` | 构建、免安装打包、品牌资源生成 |
| `tools/` | 开发诊断 / 测试工具，不随用户包分发 |
| `docs/requirements/桌面整理工具/` | 技术方案与实现记录 |

## 当前验证与访问

- Release x64 严格编译通过。
- 退出恢复的16项隔离原生诊断通过，涵盖初始显隐、收起、退出、窗口重建 / 临时缺失、父进程强制结束。
- 隔离验证不代表所有真实桌面兼容性通过；未运行项目单元测试。
- 仓库默认私有：下载者需仓库访问权限，或由所有者直接分享发布 ZIP。
