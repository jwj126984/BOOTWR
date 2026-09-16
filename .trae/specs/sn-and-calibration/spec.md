# SN管理与热电偶标定功能 - 产品需求文档

## Overview
- **Summary**: 根据BMU上位机通信协议文档，为现有CAN刷写上位机应用添加SN管理和热电偶标定两个核心功能模块。
- **Purpose**: 完善上位机功能，支持设备序列号的读写操作以及热电偶参数的标定和采集。
- **Target Users**: BMU设备生产和维护人员

## Goals
- 实现SN读写功能（最多31字符）
- 实现热电偶标定功能（k、b、vref参数读写）
- 实现采集电压读取功能
- 保持与现有固件烧录功能的兼容性

## Non-Goals (Out of Scope)
- 不修改现有固件烧录功能的核心逻辑
- 不添加新的UI框架或第三方库
- 不实现网络通信功能

## Background & Context
现有应用已实现固件烧录功能，基于CAN通信协议与BMU设备通信。需要扩展支持SN管理和热电偶标定，遵循相同的通信协议规范。

## Functional Requirements
- **FR-1**: SN_WRITE - 分段写SN到RAM（CMD 0x16）
- **FR-2**: SN_READ - 分段读SN（CMD 0x17）
- **FR-3**: TC_CALIB_WRITE - 写标定k/b/vref到RAM（CMD 0x12）
- **FR-4**: TC_CALIB_READ - 读RAM中的k/b/vref（CMD 0x15）
- **FR-5**: TC_CALIB_SAVE - 保存RAM到Flash（CMD 0x13）
- **FR-6**: TC_CALIB_DEFAULTS - 恢复标定默认值到RAM（CMD 0x14）
- **FR-7**: TC_SAMPLE_READ - 读采集电压x或反解y（CMD 0x18）
- **FR-8**: SESSION_ENTER/EXIT - 会话管理（复用现有实现）

## Non-Functional Requirements
- **NFR-1**: 支持大端序（BE）的多字节整数和float
- **NFR-2**: 响应超时时间合理（建议30秒）
- **NFR-3**: 结果码解析正确（0x00=成功，其他为错误码）

## Constraints
- **Technical**: 使用现有CAN通信服务，遵循协议规范
- **Dependencies**: 依赖CanCommunicationService进行底层CAN通信

## Assumptions
- 设备已正确连接并处于可通信状态
- 用户已了解通信协议中的状态机约束（烧录与标定不能混状态）

## Acceptance Criteria

### AC-1: SN写入功能
- **Given**: CAN设备已连接且会话已解锁
- **When**: 用户输入SN字符串并点击写入
- **Then**: SN被分段写入设备RAM，且可通过读取验证
- **Verification**: `programmatic`

### AC-2: SN读取功能
- **Given**: CAN设备已连接且会话已解锁
- **When**: 用户点击读取SN
- **Then**: 返回完整的SN字符串（最多31字符）
- **Verification**: `programmatic`

### AC-3: 标定参数写入功能
- **Given**: CAN设备已连接且会话已解锁
- **When**: 用户设置k、b、vref参数并写入
- **Then**: 参数被写入设备RAM
- **Verification**: `programmatic`

### AC-4: 标定参数读取功能
- **Given**: CAN设备已连接且会话已解锁
- **When**: 用户点击读取标定参数
- **Then**: 返回各通道的k、b值和各AFE的vref值
- **Verification**: `programmatic`

### AC-5: 保存到Flash功能
- **Given**: RAM中已有SN或标定参数
- **When**: 用户点击保存到Flash
- **Then**: 所有参数被持久化到Flash
- **Verification**: `programmatic`

### AC-6: 采集电压读取功能
- **Given**: CAN设备已连接且会话已解锁
- **When**: 用户选择通道并点击读取
- **Then**: 返回该通道的采集电压x或反解电压y
- **Verification**: `programmatic`

## Open Questions
- [ ] 是否需要添加独立的会话管理界面？
- [ ] 是否需要添加操作确认对话框？