# Desktop Tidy · 桌面整理

轻量 Windows 桌面整理工具：自动分类、搜索、拖动归类、自定义排列与明暗主题。
当前为 **v0.2.0 预览版、免安装 ZIP**；整理与拖动分类不会移动或删除桌面原文件。

## 界面预览

![Desktop Tidy 分类工作区](docs/images/workspace-overview.png)

<details>
<summary>查看深色主题与工作区设置</summary>

![Desktop Tidy 深色主题设置](docs/images/workspace-settings-dark.png)

</details>

> 展示图使用虚构示例数据生成，不包含真实桌面文件、路径或个人信息。

## 下载与安装

1. 打开 [Releases](https://github.com/KellonWww/desktop-tidy/releases)，选择最新预览版。
2. 下载 **DesktopTidy-v0.2.0-preview-win-x64.zip**，不要下载 Source code 压缩包。
3. 右键 ZIP → 全部解压，将整个目录放在固定位置，例如 `D:/Apps/DesktopTidy`。
4. 双击 `DesktopTidy.exe`，保留同目录的 `DesktopTidy.exe.config`。
5. 可右键 EXE 创建快捷方式；不需要安装 Python 或开发工具。

| 项目 | 要求 / 说明 |
|---|---|
| 系统 | 64 位 Windows 10 / 11；目前主要在 Windows 11 验证，Windows 10 兼容性待验收 |
| 运行环境 | .NET Framework 4.8 或兼容的更新版本；缺失时安装官方运行时，不是开发用 Targeting Pack |
| 界面组件 | Microsoft Edge WebView2 Runtime；Windows 11 通常已内置，缺失时需安装官方运行时 |
| 桌面 | Windows Explorer 原生桌面；其他桌面替代软件兼容性未验证 |
| 权限 | 通常不需要管理员运行 |
| 分发 | ZIP 不包含测试程序、采样器、用户配置或桌面数据 |
| 签名 | 当前无代码签名；请核对下载来源与 SHA-256，不要关闭系统安全防护 |

.NET Framework 官方下载：https://dotnet.microsoft.com/download/dotnet-framework/net48

### 校验下载文件

在 Windows 命令提示符执行下列命令，并与 Release 页面公布的 SHA-256 比对：

```bat
certutil -hashfile DesktopTidy-v0.2.0-preview-win-x64.zip SHA256
```

## 第一次使用

1. 启动后扫描桌面，进入分类工作区；首次引导默认保留原生桌面图标。
2. 想只看整理工作区，可在引导中勾选隐藏原生图标，也可之后在设置中切换。
3. 双击工作区图标打开原文件；右键打开 Windows 原生菜单，可重命名、删除或打开属性；拖到另一分类框只改变工作区归属。
4. 拖动分类标题调整位置；通过分类框按钮折叠、展开或排列图标。
5. 工作区内重命名、删除后自动刷新；右键操作过的目录会监听属性与文件变化，未同步时可点击 **刷新**。

应用识别仅使用本地规则，不联网、不上传图标、不启动目标程序；规则未覆盖的应用需手动归类。旧布局与16个自定义分类保留。

| 操作 | 使用方式 |
|---|---|
| 应用自动分类 | 按应用名、快捷方式目标、程序信息和网址识别娱乐 / 聊天 / 设计等；娱乐内分音乐、影视、游戏、社交，未知应用进入待分类 |
| 手动纠正 | 拖入目标分类后记住归属，刷新不覆盖；重新自动分类清除手动归属，可撤销 |
| 搜索 | 左栏按名称过滤；Ctrl+F 聚焦，Esc 清空搜索 |
| 分类排序 | 数量排序切换升序 / 降序；刷新不覆盖自定义排列 |
| 撤销 | Ctrl+Z 撤销最近一次支持撤销的布局 / 设置操作；搜索框内保留文本撤销 |
| 自定义分类 | 在设置 / 分类框右键菜单中管理分类，支持空分类显示 |
| 不透明度 | 设置中选择 25%、50%、75%、100%；完全隐藏使用独立收起入口 |
| 收起工作区 | 点击收起并确认；程序保留在托盘，原生图标恢复操作前状态 |
| 重新显示 | 托盘右键 → 显示分类窗口；图标可能在任务栏 ↑ 中 |
| 完全退出 | 托盘右键 → 退出；退出时请求显示原生桌面图标，并确认结果 |

### 文件与图标保护

- 分类、排列和显隐操作不会移动或删除桌面原文件；图标右键菜单中的重命名、删除、属性会操作真实文件。
- 删除须明确确认，优先移入回收站；无法回收时由 Windows 另行确认，删除快捷方式不会卸载程序。
- 文件删除、重命名不属于工作区 Ctrl+Z 的撤销范围；文件删除后可到回收站还原。更换快捷方式图标请打开右键菜单中的“属性”。
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
- **收起后找不到：** 在任务栏 ↑ 中查找托盘图标，右键显示分类窗口。
- **退出后图标仍未显示：** 桌面右键 → 查看 → 确认显示桌面图标；反馈系统版本、退出方式和脱敏日志。
- **安全软件拦截：** 确认仓库来源并校验哈希；当前未签名，不建议绕过明确的恶意软件告警。

## 开发与构建

主程序为 **C# 5 + WinForms + WebView2 + .NET Framework 4.8，Release x64**。
`tidy/` 是保留的早期 Python 草稿，不是发布版的启动入口。

使用 Visual Studio / MSBuild 与 .NET Framework 4.8 Targeting Pack：

```bat
msbuild DesktopTidy.sln /p:Configuration=Release /p:Platform=x64
```

或使用 Git Bash 与系统 Framework x64 编译器，警告视为错误：

```bash
bash scripts/build.sh
python scripts/package-release.py --build-dir build/prototype --version v0.2.0-preview
```

本地启动目录统一为 `build/prototype/`，后续构建直接更新其中的 `DesktopTidy.exe`，不再按功能另建启动目录。
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
