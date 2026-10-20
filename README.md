# CANape ASAP3 Online Adder

CANape 在线观测量和标定量自动添加工具。

## 功能

- 在线添加 CANape 观测量
- 在线添加 CANape 标定量
- 支持批量输入变量
- 自动读取当前 CANape 工程的 A2L
- 支持 ELF 文件解析
- 通过 ASAP3 接口在线更新 CANape
- 支持添加结果分类和错误提示
- 不需要手动反复更换 A2L 文件

## 使用要求

- Windows
- CANape x64
- CANape 工程已经打开
- CANape 测量已停止
- Tools → ASAP3 → ASAP3 Active 已打开

## 使用方法

1. 启动 CANape 并打开工程。
2. 打开 Tools → ASAP3，保持 ASAP3 Active。
3. 启动 `CanapeAsap3OnlineAdder.exe`。
4. 选择 CANape 工程和 ELF 文件。
5. 输入观测量和标定量。
6. 点击连接并添加。

## 文件说明

源码仓库不包含用户的 CNA、A2L、ELF、MF

