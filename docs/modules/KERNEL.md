# Kernel 基础值契约

> 首次落地阶段：P05。  
> 本文记录 Kernel 值契约的边界，不记录实时完成状态；实际证据见 [P05 进度](../progress/P05.md)。

## Strong IDs

Kernel 使用不同 CLR 类型表达不同身份域：

- `EntityId`：运行时实体；
- `ActorId`：可持续关联的行动主体；
- `WorldId`：世界身份；
- `AbilityId`：能力定义身份；
- `ItemDefinitionId`：静态物品定义；
- `ItemInstanceId`：运行时/持久化物品实例；
- `TaskId`：通用异步/后台工作身份；
- `EntityGeneration`：同一实体槽位复用时的代际；
- `WorldEpoch`：同一 World 身份的运行世代。

不提供模糊的统一 `ItemId`，因为定义与实例具有不同生命周期和一致性语义。不同 Strong ID 不提供互相转换运算符；跨类型传递应在编译期失败。

## 空值与数值边界

底层数值为 `UInt64`。值 `0` 永久保留给 CLR `default(T)` / empty sentinel，不是合法业务 ID、Generation 或 Epoch。

值类型无法禁止 `default(T)`，因此规则是：

- 正常构造必须大于 0；
- Canonical 解析必须拒绝 0；
- `IsEmpty` 只用于识别未初始化/default；
- 读取 `Value` 或输出 canonical 文本时，empty/default 必须失败，不能把 0 当成真实身份继续传递。

## 序列化边界

Canonical 文本形式是 `InvariantCulture` 下的十进制无符号整数：

- 只接受十进制数字；
- 不接受正负号、前后空白；
- 不接受 0；
- 不接受超过 `UInt64.MaxValue` 的值。

数据库/网络 Adapter 可以选择数值或字符串字段，但必须在边界转换回对应的 Strong ID，并执行同样的非零/溢出校验。不得把 `EntityId` 的值直接反序列化成 `ActorId` 等其他身份类型。

## Result 与 KernelError

`Result` 和 `Result<T>` 只表达成功或带 `Error` 的失败。default Result 是未初始化状态，不等价于失败。

`KernelError.Code` 是机器稳定键：

- lowercase ASCII；
- 仅允许 `a-z`、`0-9`、`.`、`_`、`-`；
- 长度受限；
- 传输/持久化 Adapter 必须原样保留 Code；
- `Message` 面向人类，可改进措辞，不应作为业务分支键。

`ValidationIssue` 用于字段/路径级错误，拥有独立稳定 code、path 和 message。KernelError 内的 issue 集合由 Kernel 防御性复制，调用方不能通过修改原始集合改变已创建 Error。

## 契约记录

契约 ID / 版本：`kernel.primitives.v1`  
提供模块 / 状态所有者：Kernel / 纯值，无可变领域状态  
真实调用场景 / 调用方：P05 建立后由 P06/P07 及后续 Runtime/Modules 逐步采用；当前不预建 Handler/Service 接口  
读写类型 / 频率 / 单位：小型值类型；ID 无单位  
同步或异步 / 合法执行上下文：纯值，无线程亲和性  
输入、输出、错误码：见本文 Strong ID 与 Result/KernelError 规则  
取消、超时、重复、乱序语义：不适用于纯值对象  
是否涉及持久化 / 成功确认点：Kernel 本身不持久化；Adapter 必须执行边界校验  
兼容与迁移方式：当前为 v1 首次引入；未来若改变 wire 形式需版本化并提供迁移  
相关测试 / ADR / AC 参考：P05 Unit + compile-negative Gate；不参考 AzerothCore 专有实现


> 命名说明：路线图中的通用 “Error” 概念在 C# API 中命名为 `KernelError`，以满足启用的 CA1716 跨语言 API 规则；语义没有改变。


## Geometry（P06）

Geometry 是纯数学值契约，不拥有世界状态，也不查询 Runtime：

- `Position`：三维世界位置，单位米；
- `Vector`：三维向量，单位由调用语境决定；位置差向量单位米；
- `Distance`：非负米值；
- `Angle`：方向角，弧度，canonical 范围 `[0, 2π)`；
- `Transform`：Position + Yaw；
- `Bounds`：轴对齐闭区间边界盒。

坐标系为右手系，`+Z` 向上；Yaw=0 指向 `+X`，正方向绕 `+Z` 逆时针朝 `+Y`。所有 public 构造拒绝 NaN/Infinity。零向量本身合法，但归一化零向量失败。有限输入的算术若产生非有限结果，必须显式报溢出。

序列化格式仍由 Adapter 决定；Kernel 不引用 JSON/数据库序列化库。测试用 test-only wire DTO 验证数值字段经过 JSON 回读后可重新构造同一 Geometry 值，不把具体 JSON 形态升级为 Kernel wire 契约。

契约 ID / 版本：`kernel.geometry.v1`  
提供模块 / 状态所有者：Kernel / 纯数学值，无可变领域状态  
同步或异步 / 合法执行上下文：纯值，无线程亲和性  
持久化与传输：Adapter 负责格式；回到 Kernel 时重新执行有限数、范围与 Bounds 不变量校验  
相关测试 / ADR / AC 参考：P06 Unit + P03/P04 持续 Gate；不参考 AzerothCore 专有公式
