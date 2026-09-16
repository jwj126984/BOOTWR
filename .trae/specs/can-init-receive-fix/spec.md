# CAN盒初始化和接收重构 - 产品需求文档

## Overview
- **Summary**: 修复CAN盒初始化和接收功能，按照周立功官方API规范进行重构
- **Purpose**: 解决当前CAN通信中无法接收消息的问题，确保CAN设备正常工作
- **Target Users**: BMU CAN烧录上位机用户

## Goals
- 修复CAN接收循环未启动的问题
- 按照周立功官方API规范重构初始化代码
- 确保CAN设备能够正确发送和接收消息

## Non-Goals (Out of Scope)
- 添加新功能特性
- 修改烧录协议逻辑
- 修改UI界面

## Background & Context
根据日志分析，发现以下问题：
- `StartReceiveLoop()`方法被注释，导致无法接收CAN消息
- 发送SESSION请求后无法收到响应，提示"发送SESSION请求失败"
- 需要按照周立功官方API规范检查并修复初始化和接收逻辑

## Functional Requirements
- **FR-1**: CAN设备连接成功后自动启动消息接收循环
- **FR-2**: 按照周立功官方API规范配置CAN通道初始化参数
- **FR-3**: 正确配置波特率参数（timing0/timing1）
- **FR-4**: 确保CAN消息能够正常发送和接收

## Non-Functional Requirements
- **NFR-1**: CAN接收线程需正确处理异常和取消操作
- **NFR-2**: 线程安全的消息处理机制
- **NFR-3**: 符合周立功zlgcan.dll官方API规范

## Constraints
- **Technical**: 使用周立功zlgcan.dll v3.x版本API
- **Dependencies**: 需保持与现有FlashingService的兼容性

## Assumptions
- 周立功CAN设备硬件正常工作
- zlgcan.dll已正确部署

## Acceptance Criteria

### AC-1: CAN接收循环启动
- **Given**: CAN设备连接成功
- **When**: 调用Connect()方法返回true
- **Then**: 自动启动消息接收循环
- **Verification**: `programmatic`

### AC-2: SESSION请求成功发送并接收响应
- **Given**: CAN设备已连接
- **When**: 发送SESSION请求(0x10)
- **Then**: 成功收到响应(0x50)
- **Verification**: `programmatic`

### AC-3: 波特率配置符合官方规范
- **Given**: 配置250kbps波特率
- **When**: 初始化CAN通道
- **Then**: timing0=0x1C, timing1=0x03（50MHz时钟）
- **Verification**: `human-judgment`

### AC-4: 线程安全
- **Given**: 并发发送和接收消息
- **When**: 多线程访问CAN服务
- **Then**: 无数据竞争和异常
- **Verification**: `human-judgment`

## Open Questions
- [ ] 需要确认周立功官方API的波特率配置参数