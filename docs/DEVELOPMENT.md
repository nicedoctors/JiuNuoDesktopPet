# 开发指南

## 源码与发行包维护

GitHub 会过滤非 ASCII 附件文件名，发行附件采用 `JiuNuoDesktopPet-<版本>-win-x64.zip` 并设置中文显示标签。本地 ZIP、解压目录、产品名称和启动 EXE 继续使用“啾糯桌宠”，附件内容不因上传名称改变。

源码与资源的公开条件以 [发布准备状态](RELEASE_READINESS.md) 及 [素材权利](../ASSET_RIGHTS.md) 为准。完成确认后，源码及已获许可的角色运行资源提交至所选仓库的 `main`，免安装 ZIP 作为 GitHub Release 附件，不把内置 .NET 运行时提交进 Git。仓库可见性不随发行改变。原图、生成源图、最终提示词、选定素材及处理哈希保留；生成服务地址、配置编号、任务状态、计划副本和本地凭据不属于提交内容。最终生成回执只保留原图/提示词/输出哈希、模型、尺寸与来源链字段，满足离线重切校验；派生 `keyframes` 不提交，按现有处理工具生成。项目维护者负责核对源码、附件校验和与发布标签一致。

## 技术栈

- .NET 8
- WPF
- Win32 P/Invoke
- Windows Forms `NotifyIcon`（仅用于托盘入口和菜单）
- PowerShell 构建与打包脚本
- 原生 C x64 便携版启动器

## 准备环境

基础开发只需要 Windows 10/11 x64 和 .NET 8 SDK：

```powershell
dotnet --info
dotnet restore .\SoftMochiPet.sln
```

生成完整免安装包还需要 Python 3.12+ 与 Pillow（`python -m pip install Pillow==12.3.0`），用于打包前的隐私检查。启动器使用 Visual Studio 2022 / Build Tools 的 C++ 桌面开发组件（v143），或设置 `LLVM_MINGW_ROOT` 指向 LLVM-MinGW 20260616 UCRT x64 发行目录。工具来源与维护范围见 [发布准备状态](RELEASE_READINESS.md)。`DOTNET_EXE`、`PYTHON_EXE` 可分别指定本机工具路径，未设置时使用 PATH。

## 常用命令

### 运行

```powershell
.\run.ps1
```

`run.ps1` 先构建当前源码的 Release x64 配置，再启动对应 EXE；不会误启动 `dist` 中未更新的旧发行版。正式免安装目录需单独运行其根目录 `啾糯桌宠.exe`。

### 构建

```powershell
.\build.ps1
```

等价的核心命令为：

```powershell
dotnet build .\SoftMochiPet.sln -c Release -p:Platform=x64
```

### 测试

```powershell
dotnet run --project .\tests\SoftMochiPet.LogicTests\SoftMochiPet.LogicTests.csproj -c Release
```

测试项目是一个无界面测试运行器，适合验证确定性的状态机、物理和队列逻辑。WPF 窗口交互、鼠标捕获、Explorer 和显示器切换必须额外执行 `SELF_TEST.md` 中的实机检查。

### 发布目录版

```powershell
.\publish.ps1
```

输出到 `dist\啾糯桌宠`，主程序为 `啾糯桌宠.exe`。这是 Windows x64 自包含目录，包含 .NET 运行时，不要求目标电脑预装 .NET。

### 生成免安装包

```powershell
.\package.ps1
```

脚本会：

1. 将自包含 WPF 主程序发布到 `程序文件` 子目录；
2. 编译根目录唯一正常入口 `啾糯桌宠.exe`；
3. 复制对应版本使用说明、代码许可、素材权利记录与第三方声明；
4. 生成不含测试工具的文件夹和 ZIP 两种正式产物。

输出路径：

```text
dist\啾糯桌宠-免安装版\
dist\啾糯桌宠-免安装版.zip
```

若未安装 Visual C++ 工具链，脚本只在所选模式的缓存启动器存在、且时间戳不早于启动器源码和程序资源时复用缓存。专属测试入口仍可在开发构建中使用独立的 `FeatureTest` 编译中间目录，但不会进入正式包。全新克隆的仓库通常没有这些缓存，因此需要 Visual C++ Build Tools 或显式配置 LLVM-MinGW。

产品名称、发布程序集和启动入口统一为“啾糯桌宠”；源码项目名及既有数据标识不改。兼容边界与升级自启说明以 [正式更名与旧版兼容](../README.md#正式更名与旧版兼容) 为准。

## 代码结构

| 目录 | 职责 |
| --- | --- |
| `src/SoftMochiPet/Core` | 行为策略、拖拽速度、窗口攀爬、队列和纯逻辑 |
| `src/SoftMochiPet/Interop` | Win32 常量、结构体和 P/Invoke 声明 |
| `src/SoftMochiPet/Models` | 设置、删除事件和长期生活状态 |
| `src/SoftMochiPet/Services` | 显示器几何、窗口地形、文件监听、日志、声音和托盘服务 |
| `src/SoftMochiPet/MainWindow.xaml.cs` | WPF 生命周期、状态机编排和用户交互 |
| `tests/SoftMochiPet.LogicTests` | 无界面回归测试 |
| `launcher` | 便携版最外层原生启动入口 |

新增可确定性验证的行为时，优先把计算和决策放到 `Core` 或独立服务中，再由窗口层调用。这样可以避免把所有逻辑绑定到 WPF Dispatcher，也便于在无桌面会话中运行测试。

## 本地数据

| 数据 | 路径 |
| --- | --- |
| 设置 | `%APPDATA%\SoftMochiPet\settings.json` |
| 生活状态 | `%APPDATA%\SoftMochiPet\life-state.json` |
| 当前日志 | `%LOCALAPPDATA%\SoftMochiPet\activity.log` |
| 上一份日志 | `%LOCALAPPDATA%\SoftMochiPet\activity.previous.log` |

调试状态迁移时可以先退出应用，再备份或移走生活状态文件。不要在应用运行时编辑该文件；退出流程可能覆盖手工改动。

## 日志排查

`activity.log` 使用带时间、PID 和事件字段的结构化文本。建议按以下顺序定位问题：

1. 找到最近一次 `PetLoaded`，确认属于当前进程；
2. 检查显示器列表、DPI、工作区和桌宠尺寸；
3. 沿 `StateTransition` 观察状态和动作目的；
4. 对拖拽问题检查 `DragStarted`、释放速度和落地事件；
5. 对删除问题检查监听来源、位置快照、`QueueId` 和进食完成事件；
6. 最后搜索异常、捕获失败或服务停止记录。

当前日志对外部路径和文件名进行脱敏；旧版日志、外部工具输出和新加字段仍须人工检查。不要把完整本机日志直接附到公开 Issue。

## 不应提交的内容

```text
bin/
obj/
dist/
.codex-temp/
artifacts/
test_artifacts/
tmp/
```

此外，不要提交本机设置、生活状态、日志、IDE 用户配置或包含个人路径的测试数据。

## 发布检查

`.github/workflows/verify.yml` 在 Windows 2022 上执行构建、无界面测试、隐私与凭据扫描，以及完整打包校验；不上传或公开发行包。使用 [actions/checkout](https://github.com/actions/checkout) v6、[actions/setup-dotnet](https://github.com/actions/setup-dotnet) v5 和 [actions/setup-python](https://github.com/actions/setup-python) v6（均为 MIT），固定提交见工作流。图像工具使用 [Pillow 12.3.0](https://pypi.org/project/pillow/12.3.0/)（MIT-CMU），凭据扫描使用 [Gitleaks 8.30.1](https://github.com/gitleaks/gitleaks/releases/tag/v8.30.1)（MIT，下载校验值固定于工作流）。这些工具未经本地修改，不随桌宠发行。许可证原文用 `.gitattributes` 保留原始字节，以免不同 Git 换行设置破坏来源哈希。

`package.ps1` 使用 `dist/.package-<唯一编号>` 暂存自包含运行目录和 ZIP，调用 `tools/verify_portable_package.ps1` 核对 x64 GUI 入口、启动器/主程序版本、内置 .NET/WPF、角色帧完整性、源素材哈希及 ZIP 全文件哈希，成功后才把旧正式包移入 `dist/旧版归档` 并替换正式路径。编译或校验失败时原正式包保留；暂存目录用于排查。这个流程只服务当前免安装发布，由项目维护者随角色素材契约维护帧目录数量。行为控制版仅在明确传入对应参数时更新。

在准备可分发版本前：

1. 运行 Release 构建和全部逻辑测试；
2. 后台静音完成离线检查；实际桌面、窗口与声音验收由用户按 `SELF_TEST.md` 执行，并分别记录验证范围；
3. 执行 `package.ps1` 并在全新目录解压；
4. 交付根目录启动器，由用户亲自启动确认实际功能与手感；
5. 检查 ZIP 内没有日志、设置、生活状态或开发机路径；
6. 如有代码签名证书，在压缩 ZIP 前对可执行文件签名；
7. 记录版本号、提交 SHA 和 SHA-256 校验值。
