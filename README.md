# 网易云任务栏歌词

在 Windows 11 主任务栏空白区显示网易云音乐同步歌词。程序使用透明、置顶、鼠标穿透的独立窗口，不修改 Explorer。

## 最简单的安装方法

运行“LyricsStatusBar-Win11-x64-Setup.exe”：

- 已安装 BetterNCM：安装程序会自动识别数据目录并部署桥接插件。
- 尚未安装 BetterNCM：可以先完成本工具安装；安装 BetterNCM 后，本工具会在启动时自动检测并补装插件。
- 自动检测失败：右键通知区域中的本工具图标，选择“安装/修复 BetterNCM 桥接”。

插件安装或更新后，需要彻底退出并重新打开网易云音乐。主程序是 Win11 x64 自包含版本，目标电脑不需要另装 .NET。

## 使用

播放网易云音乐后，歌词会显示在任务栏应用按钮与系统托盘之间的空白区。歌词区域不会拦截鼠标点击。

通知区域菜单提供：

- 启用歌词
- 开机启动
- 设置
- 诊断状态
- 安装/修复 BetterNCM 桥接
- 退出

歌词快慢可在设置中的“Lyric advance (ms)”调整；正数会让歌词提前。

## 兼容范围

- Windows 11 x64
- 网易云音乐 Windows 客户端 2.10.x–3.0.x
- BetterNCM 1.x
- 原生 Win11 任务栏及暴露标准任务栏窗口的 ExplorerPatcher 布局
- 第一版仅支持主显示器横向任务栏

## 构建

需要 .NET 10 SDK、LLVM-MinGW x86 工具链和 Inno Setup 6：

    .\scripts\test.ps1
    .\scripts\build.ps1
    .\scripts\package.ps1

输出位于 artifacts。构建脚本生成：

- Win11 x64 自包含主程序
- x86 BetterNCM 原生桥接 DLL
- LyricsStatusBarBridge.plugin
- 便携 ZIP
- 单文件安装 EXE

## 隐私与许可

桥接仅使用当前 Windows 用户可访问的命名管道和 BetterNCM 数据目录中的本地临时消息文件，不开放网络端口，不传输网易云 Cookie，不持久化歌词正文。

项目采用 MIT 许可证。隐私说明见 PRIVACY.md，第三方说明见 THIRD_PARTY_NOTICES.md。
## GitHub 发布

仓库包含两条 GitHub Actions 工作流：

- 普通提交和 Pull Request 自动构建并运行测试。
- 推送 v 开头的版本标签时，自动构建 Win11 x64 安装包、便携 ZIP 和 BetterNCM 插件包，生成 SHA-256 校验文件、构建来源证明并发布 GitHub Release。

发布工作流支持可选 Authenticode 签名。取得受信任的代码签名证书后，将 PFX 的 Base64 内容保存为仓库机密 WINDOWS_SIGNING_CERTIFICATE_BASE64，将密码保存为 WINDOWS_SIGNING_CERTIFICATE_PASSWORD。证书和密码绝不能提交到仓库。

## SmartScreen 说明

自签名证书不会让普通 Windows 电脑自动信任安装包，实际效果通常与未签名相同。公开分发应使用受信任 CA 的 OV/EV 代码签名证书或 Microsoft Artifact Signing，并在每个版本中保持同一发布者身份。即使使用受信任签名，新发布文件也可能暂时显示“无法识别的应用”，直到建立 SmartScreen 信誉；Microsoft Store 分发才是最直接避免下载警告的方式。