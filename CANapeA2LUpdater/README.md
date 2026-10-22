# CANape A2L Updater

第一版命令行工具通过 Vector ASAP2 Studio 自带的 .NET API，从 ELF/DWARF 中查找指定符号，并在 A2L 副本中创建或更新对应的 `MEASUREMENT`。

## 桌面版

双击 `bin\QTAutoAdder.exe`，选择 CANape 工程和对应 ELF，分别输入观测量和标定量，然后点击“Connect and Add”。工具会自动保存上次配置和文件选择目录。

- 默认生成 `原文件名.updated.a2l`，不会覆盖原始 A2L。
- 请在 CANape 处于 Offline 时切换数据库。
- 输出文件固定使用 ASAP2 1.71，并在写出后重新读取校验。

## 构建

```powershell
.\build.ps1
```

## 运行

```powershell
.\bin\CanapeA2LUpdater.exe `
  --a2l ".\XCP8(2)\ape\XCPsim.a2l" `
  --elf "C:\path\application.elf" `
  --signals ".\signals.txt" `
  --out ".\XCPsim.corrected.a2l"
```

每行一个信号名，空行和以 `#` 开头的行会被忽略。工具拒绝覆盖输入 A2L。

当前版本依赖：

- 64 位 CANape 及对应的 `CANapAPI64.dll`
- Vector ASAP2 Studio CANape 组件

程序会按以下顺序自动寻找 CANape API：

1. 环境变量 `CANAPE_API_DLL`
2. 程序目录
3. 当前正在运行的 CANape 安装目录（优先使用与当前 CANape 匹配的 API）
4. Windows 安装信息和常见安装目录

ASAP2 Studio 会通过 `ASAP2_STUDIO_BIN`、Windows 安装信息和常见安装目录自动识别。
界面只显示检测到的版本和路径；启动软件不会自动连接 ASAP3，只有手动点击
“刷新状态”或“连接并添加”才会建立连接。

自动识别失败时，可使用环境变量手动指定：

```text
CANAPE_API_DLL=D:\path\to\CANapAPI64.dll
ASAP2_STUDIO_BIN=D:\path\to\ASAP2Studio\Bin
```

## 在线添加观测量和标定量

前提：CANape 已打开目标工程、测量尚未启动，并保持 `Tools → ASAP3` 的
`ASAP3 Active` 窗口打开。

```powershell
.\OnlineUpdate.ps1 `
  -Project ".\XCP8(2)\LINGKE.cna" `
  -Elf "C:\path\application.elf" `
  -Measurements ".\test-measurements.txt" `
  -Calibrations ".\test-calibrations.txt"
```

- `Measurements` 和 `Calibrations` 至少提供一个，文件均为每行一个变量名。
- 工具会通过 ASAP3 自动读取当前模块使用的 A2L，不需要手动选择或更换 A2L。
- 写入前会创建带时间戳的 A2L 备份。
- 更新后仍加载原来的 A2L 路径，不重启 CANape，也不重新加载 CNA。
- 工具拒绝在测量运行期间修改数据库，并在结束前验证新增对象的类型。
- 保存 A2L 与保存 CANape 工程是两件事；关闭工程时仍可选择不保存 CNA。
