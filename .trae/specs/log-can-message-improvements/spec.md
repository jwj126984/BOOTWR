# 操作日志和CAN报文改进 - 产品需求文档

## Overview
- **Summary**: 修改操作日志功能框支持大小拉伸，并调整日志显示规则，确保CAN报文界面正确显示烧录过程中的报文。
- **Purpose**: 提升用户体验，使操作日志区域可调整大小，日志内容更加清晰，CAN报文界面能正确显示烧录过程。
- **Target Users**: 使用CAN刷写上位机的技术人员

## Goals
- 操作日志功能框支持水平拉伸调整大小
- 操作日志不显示报文收发的详细调试信息
- CAN报文界面正确显示烧录过程中的报文

## Non-Goals (Out of Scope)
- 修改其他界面布局或功能
- 添加新的日志级别或过滤功能
- 修改CAN通信协议或逻辑

## Background & Context
当前系统中：
1. 操作日志区域是固定宽度400px，无法调整
2. 日志服务会记录所有报文收发的Debug级日志，导致日志过多
3. CAN报文界面通过UDS过滤器显示特定ID的报文

## Functional Requirements
- **FR-1**: 操作日志功能框支持水平拉伸
- **FR-2**: 操作日志不显示报文收发的Debug日志
- **FR-3**: CAN报文界面在烧录过程中显示所有相关CAN报文

## Non-Functional Requirements
- **NFR-1**: 修改应保持代码风格一致性
- **NFR-2**: 不影响现有功能的正常运行

## Constraints
- **Technical**: WPF .NET 10框架，MVVM架构
- **Dependencies**: 现有日志服务和消息管理服务

## Assumptions
- 用户期望操作日志和CAN报文区域可以灵活调整大小
- 用户只关心操作日志中的高级别信息，不需要看到每个报文的收发细节

## Acceptance Criteria

### AC-1: 操作日志功能框可拉伸
- **Given**: 用户打开应用主窗口
- **When**: 用户拖动操作日志和CAN报文区域之间的分隔条
- **Then**: 操作日志区域的宽度应随之调整
- **Verification**: `human-judgment`

### AC-2: 操作日志不显示报文收发
- **Given**: 用户进行CAN通信操作（如连接、刷写等）
- **When**: 查看操作日志
- **Then**: 日志中不应包含"发送报文"和"接收报文"的Debug级别日志
- **Verification**: `human-judgment`

### AC-3: CAN报文界面显示烧录过程报文
- **Given**: 用户执行烧写操作
- **When**: 切换到CAN报文Tab页
- **Then**: 应能看到烧录过程中的所有CAN报文（发送和接收）
- **Verification**: `human-judgment`

## Open Questions
- [ ] 无