# CAN刷写上位机应用 - 产品需求文档

## Overview
- **Summary**: 开发一个基于WPF的CAN刷写上位机应用，实现APP固件刷写功能，支持CAN通讯配置、刷写流程控制、实时进度显示和日志记录。
- **Purpose**: 为车载电子设备提供可靠的固件刷写工具，支持主流CAN设备和刷写文件格式。
- **Target Users**: 汽车电子工程师、嵌入式开发人员、生产线操作人员。

## Goals
- 实现完整的CAN通讯配置界面，支持设备选择、波特率设置、通道配置
- 开发直观的刷写进度条组件，实时显示刷写进度
- 创建日志显示区域，支持操作日志、错误信息和系统状态记录
- 实现报文交互显示面板，展示CAN报文内容、ID、数据长度和时间信息
- 实现完整的APP刷写流程控制（连接建立、数据校验、刷写执行、结果确认）
- 开发CAN通讯协议处理逻辑，确保可靠数据传输
- 设计异常处理机制，支持刷写中断、失败恢复和错误重试功能
- 实现刷写数据解析与处理，支持主流刷写文件格式（BIN）

## Non-Goals (Out of Scope)
- 不支持LIN总线刷写
- 不支持FOTA远程升级功能
- 不包含硬件驱动开发
- 不支持加密刷写（初始版本）

## Background & Context
- 现有项目已集成ZLG CAN API（ZLGAPI.cs），提供CAN设备控制能力
- 项目使用WPF框架，.NET 10.0目标框架
- 已有烧录协议文档（CAN烧录上位机指南.md），定义了完整的刷写流程
- 支持的CAN设备类型包括USBCAN、PCIE-CANFD等多种设备

## Functional Requirements
- **FR-1**: CAN通讯配置 - 支持设备类型选择、通道配置、波特率设置
- **FR-2**: 进度显示 - 实时显示刷写进度百分比，支持进度条和数字显示
- **FR-3**: 日志记录 - 按时间戳记录操作日志、错误信息和系统状态
- **FR-4**: 报文显示 - 以表格形式展示发送/接收的CAN报文（ID、数据、长度、时间）
- **FR-5**: 刷写流程控制 - 实现SESSION、ERASE、DATA、COMMIT完整流程
- **FR-6**: CAN通讯协议 - 实现可靠的数据发送和接收逻辑
- **FR-7**: 异常处理 - 支持刷写中断、失败恢复和错误重试
- **FR-8**: 文件解析 - 支持BIN格式刷写文件的解析与处理

## Non-Functional Requirements
- **NFR-1**: 界面美观 - 现代化UI设计，符合Windows应用规范
- **NFR-2**: 操作直观 - 简洁的操作流程，清晰的状态反馈
- **NFR-3**: 通讯稳定 - 支持多种CAN设备，保证数据传输可靠性
- **NFR-4**: 刷写可靠 - 完善的错误处理和数据校验机制
- **NFR-5**: 响应及时 - 界面响应时间<100ms，进度更新频率>10Hz

## Constraints
- **Technical**: .NET 10.0, WPF, ZLG CAN SDK
- **Business**: 需兼容现有CAN设备驱动
- **Dependencies**: zlgcan.dll, ZLGAPI.cs

## Assumptions
- 用户已正确安装CAN设备驱动
- 用户具备基本的CAN通讯知识
- 刷写文件为标准BIN格式

## Acceptance Criteria

### AC-1: CAN通讯配置界面
- **Given**: 用户打开应用
- **When**: 用户选择设备类型、通道和波特率
- **Then**: 系统应正确配置CAN设备并建立连接
- **Verification**: `programmatic`

### AC-2: 刷写进度显示
- **Given**: 刷写过程中
- **When**: 数据传输进行中
- **Then**: 进度条应实时更新，显示0-100%进度
- **Verification**: `programmatic`

### AC-3: 日志记录功能
- **Given**: 应用运行中
- **When**: 执行操作或发生错误
- **Then**: 日志区域应记录时间戳、级别和消息内容
- **Verification**: `human-judgment`

### AC-4: 报文显示面板
- **Given**: CAN通讯已建立
- **When**: 发送或接收CAN报文
- **Then**: 表格应显示报文ID、数据内容、长度和时间戳
- **Verification**: `human-judgment`

### AC-5: 完整刷写流程
- **Given**: 设备已连接，刷写文件已选择
- **When**: 用户点击刷写按钮
- **Then**: 系统应依次执行SESSION→ERASE→DATA→COMMIT流程
- **Verification**: `programmatic`

### AC-6: 异常处理
- **Given**: 刷写过程中发生错误
- **When**: 收到错误响应或超时
- **Then**: 系统应显示错误信息并提供重试选项
- **Verification**: `human-judgment`

### AC-7: 文件解析
- **Given**: 用户选择BIN格式刷写文件
- **When**: 点击打开文件
- **Then**: 系统应正确解析文件内容并计算CRC
- **Verification**: `programmatic`

## Open Questions
- [ ] 是否需要支持多种刷写文件格式（如HEX、S19）？
- [ ] 是否需要添加刷写完成后的校验功能？
