# 网易云任务栏歌词

在 Windows 11 主任务栏空白区域显示网易云音乐同步歌词。程序使用独立的透明、置顶、鼠标穿透窗口，不修改或注入 Explorer。

## 下载与安装

从 [Releases](https://github.com/MOYU-owo/LyricsStatusBar/releases/latest) 下载 LyricsStatusBar-Win11-x64-Setup.exe 并运行。

本工具需要 [BetterNCM](https://github.com/std-microblock/chromatic) 提供网易云音乐播放状态和歌词：

- 已安装 BetterNCM：安装程序会自动识别数据目录并部署桥接插件。
- 尚未安装 BetterNCM：可以先安装本工具；检测到 BetterNCM 后会自动补装桥接插件。
- 自动检测失败：右键通知区域中的本工具图标，选择“安装/修复 BetterNCM 桥接”。

插件安装或更新后，需要彻底退出并重新打开网易云音乐。安装包为 Win11 x64 自包含版本，无需另外安装 .NET。

## 使用

播放网易云音乐后，歌词会显示在任务栏应用按钮与系统托盘之间的空白区域，并保持鼠标穿透。

通知区域菜单提供：

- 启用歌词
- 开机启动
- 设置
- 诊断状态
- 安装/修复 BetterNCM 桥接
- 退出

歌词快慢可在设置中的“Lyric advance (ms)”调整，正数表示歌词提前。

## 兼容范围

- Windows 11 x64
- 网易云音乐 Windows 客户端 2.10.x–3.0.x
- BetterNCM 1.x
- 原生 Windows 11 任务栏
- 暴露标准任务栏窗口的 ExplorerPatcher 布局
- 主显示器横向任务栏

## 构建

需要 .NET 10 SDK、LLVM-MinGW x86 工具链和 Inno Setup 6：

    .\scripts\test.ps1
    .\scripts\build.ps1
    .\scripts\package.ps1

构建产物位于 artifacts：

- Win11 x64 自包含主程序
- x86 BetterNCM 原生桥接 DLL
- BetterNCM 插件包
- 便携 ZIP
- 单文件安装程序

## 隐私与许可

桥接仅使用当前 Windows 用户可访问的命名管道和 BetterNCM 数据目录中的本地临时消息文件，不开放网络端口，不传输网易云 Cookie，不持久化歌词正文。

项目采用 MIT 许可证。参见 [隐私说明](PRIVACY.md) 和 [第三方许可说明](THIRD_PARTY_NOTICES.md)。
