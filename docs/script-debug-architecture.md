# ScriptLab 调试设计逻辑与技术理论

更新时间：2026-05-18

本文只描述 Graph C# / Blueprint 调试方案的逻辑思路和相关技术理论，不讨论代码模块划分、文件结构或具体实现类。

## 1. 总体判断

这个方案的核心不是“给蓝图节点绑一个事件”，而是建立一套统一的调试语义：

```text
源码位置
  <-> 图节点
  <-> 中间表示指令
  <-> PDB sequence point
  <-> 调试器断点
  <-> 运行时观测事件
```

其中最关键的原则是：

- 真断点必须交给调试器处理。
- 蓝图节点只是源码语义的图形投影，不是第二套语义。
- 非暂停观察和暂停断点必须分开。
- 映射关系必须有一个唯一真相来源。

因此设计被拆成三条线：

- Breakpoint：暂停程序，读取真实调用栈和变量。
- Trace：不暂停程序，只在有观察者时统计执行经过。
- Watch / Pin Inspect：不暂停或辅助暂停，只在有观察者时采集用户明确关心的表达式值。

这里的“有观察者”通常就是调试面板、Trace 视窗、Watch 视窗或外部客户端订阅已经打开。没有观察者时，DebugMap 仍然知道哪些位置可观测，但运行时不应该持续记录 Trace/Watch 数据。

## 2. 为什么不能把蓝图断点简单等同于某一行断点

从表面看，蓝图节点可以映射到源码某一行，所以蓝图断点似乎等于“在这一行打断点”。这个理解只在最简单语句上成立。

问题在于源码行不是稳定的语义单位：

- 一行可能包含多个表达式。
- 一个蓝图节点可能对应一段表达式，而不是完整 statement。
- 一个 statement 可能投影成多个蓝图节点。
- 用户可能把断点打在 `{`、空行、注释、参数、表达式中间。
- 编译器最终能断下来的位置取决于 PDB sequence point，而不是视觉上的行号。

例如：

```csharp
if (Input.KeyDown(Key.W))
{
    Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta));
}
```

这里至少有这些语义位置：

- `Input.KeyDown(Key.W)`：条件表达式。
- `if (...)`：分支判断。
- `{`：语法块开始，不是独立可执行语义。
- `Speed * delta`：表达式值。
- `new Vec3(...)`：构造参数。
- `Transform.Translate(...)`：实际副作用调用。

如果用户在 `{` 处打断点，蓝图不能简单高亮 `{`，因为 `{` 没有运行时语义。正确逻辑是：

```text
源码位置 `{`
  -> 找到所属 block
  -> 找到 owning statement
  -> 找到可断的语义节点
  -> 绑定到 Branch 或 block 内第一个可断语句
```

所以蓝图断点不是“行断点的 UI 皮肤”，而是“语义断点请求”。它最终可以落到某一行，但绑定过程必须经过语义映射。

## 3. 为什么需要稳定 debugSiteId

图节点 ID 通常是视图 ID，例如 `n0`、`n1`。它适合当前画布，但不适合长期保存。

原因：

- 用户重新排版可能改变节点顺序。
- 删除并重建图可能生成新的节点 ID。
- 局部变量折叠或展开会改变图节点数量。
- 同一源码语义在不同视图模式下可能对应不同节点。

`probeId` 也不能作为长期身份：

- probe 是某次 debug 编译产物。
- 插桩策略变化会改变 probe 数量。
- Watch / Trace 增减也会改变 probe 分配。
- release build 可能完全没有 probe。

因此需要一个稳定的语义锚点：

```text
debugSiteId = 当前源码语义位置的调试身份
```

它应该来自源码、绑定结果和 IR 语义，而不是来自画布布局或插桩顺序。

正确关系是：

```text
graphNodeId(current view) -> debugSiteId
probeId(current build)    -> debugSiteId
PDB sequence point        -> debugSiteId
source span               -> debugSiteId
```

断点、Trace、Watch、错误定位都围绕 `debugSiteId` 对齐。

## 4. 为什么 DebugMap 是唯一映射真相

调试时至少有四个坐标系：

- 源码坐标：文件、行、列、span。
- 图坐标：节点、pin、边。
- 编译坐标：方法、IL offset、PDB sequence point。
- 运行时坐标：probe、stack frame、变量引用。

如果 UI、编译器、运行时各自计算映射，就会出现不一致：

- UI 认为节点 A 对应第 12 行。
- PDB 认为真正可断位置是第 13 行。
- probe 事件返回的是另一个生成代码位置。
- 调试器 stopped event 无法反查蓝图节点。

DebugMap 的作用是把所有坐标系统一到同一个表中：

```text
debugSiteId
  -> source span
  -> graph node id current
  -> optional probe id
  -> PDB sequence point
  -> method token / IL offset
  -> local scope
```

这样所有方向都能反查：

```text
蓝图断点 -> DebugMap -> 源码/PDB 断点
源码断点 -> DebugMap -> 蓝图节点
断点命中 -> DebugMap -> 源码和蓝图高亮
Trace 事件 -> DebugMap -> 节点热度
Watch 值 -> DebugMap -> pin / expression
```

## 5. 源码到蓝图的逻辑链路

第一条链路是静态分析链路：

```text
.ash.cs
  -> SyntaxTree
  -> 子集判定
  -> 符号绑定
  -> 行为 IR
  -> 蓝图投影
```

每一步的目的不同：

- SyntaxTree 负责保留源码结构。
- 子集判定负责确认这段 C# 是否可图形化。
- 符号绑定负责把 C# 名字转成稳定行为 ID。
- IR 负责保存可分析、可调试、可投影的语义。
- 蓝图投影负责把语义呈现成图。

这里的理论基础是：图不是源码的文本格式转换，而是源码语义的投影。

所以蓝图不应该保存另一份可执行语义。否则会出现“双真相”：

```text
.ash.cs 说一套
蓝图 JSON 说另一套
```

一旦出现双真相，调试、错误定位、回写、版本控制都会变复杂。

## 6. 为什么需要 IR

直接从源码生成蓝图会遇到两个问题：

- C# 语法结构不等于蓝图语义结构。
- 调试需要比源码更细的语义位置。

例如局部变量：

```csharp
var amount = Speed * delta;
Transform.Translate(Self, new Vec3(0f, 0f, amount));
```

源码里是两条 statement，但蓝图里可能有两种显示方式：

紧凑视图：

```text
Speed + delta -> Multiply -- amount --> Make Vec3 -> Translate
```

展开视图：

```text
Speed + delta -> Multiply -> Set Local amount -> Get Local amount -> Make Vec3 -> Translate
```

如果没有 IR，蓝图折叠后可能丢失 `StoreLocal / LoadLocal` 的调试意义。

IR 的理论作用是：

- 把源码语法变成稳定语义指令。
- 保留局部变量、表达式、调用、分支等调试单位。
- 给每个可调试单位分配 `debugSiteId`。
- 让蓝图可以折叠显示，但不丢失底层语义。

## 7. 断点可断性理论

不是所有语义位置都能独立断点。

可以断的通常是：

- statement。
- 调用语句。
- 分支判断。
- 编译器生成了可用 PDB sequence point 的位置。

不一定能独立断的通常是：

- 单个表达式的一部分。
- 参数内部表达式。
- 纯数据节点。
- 被折叠的局部变量读取。
- `{`、空行、注释。

所以每个调试点需要区分：

```text
breakable      = 可作为暂停断点
observable     = 可作为 Trace / Watch 观测点
sourceOnly     = 只能作为源码位置，无法映射蓝图语义
```

表达式节点如果不可断，应该绑定到 owning breakable statement：

```text
Speed * delta
  -> owning statement: Transform.Translate(...)
```

这就是为什么断点系统需要 `owningBreakableDebugSiteId`。

## 8. PDB sequence point 的理论作用

C# 调试器不是直接按源码字符串执行。编译后真正运行的是 IL。

PDB sequence point 的作用是建立映射：

```text
IL offset -> source file + line + column
```

调试器能够在某个源码位置暂停，是因为 PDB 告诉它：

```text
这一段 IL 对应用户源码中的这个位置
```

这带来一个重要结论：

蓝图断点要成为“真断点”，最终必须能落到：

- source breakpoint，被 adapter / CLR debugger 验证；
- 或 instruction / IL offset breakpoint；
- 或其他真实调试器支持的位置。

仅仅有 `probeId` 不代表真断点。

## 9. 为什么 probe 必须 hidden

为了支持 Trace / Watch，debug 编译时可以预先插入 probe，但是否记录事件由观察者开关决定：

```text
DebugProbe.Enter(...)
DebugProbe.Value(...)
```

但这些 probe 是调试辅助代码，不是用户源码语义。

如果 probe 出现在用户单步里，会导致：

- 用户 step over 时停在生成代码上。
- 调用栈被内部 probe 污染。
- 源码行高亮跳到不存在的逻辑。
- 蓝图节点定位混乱。

所以 probe 必须通过 hidden line directive 或等价机制隐藏。

理论目标是：

```text
probe 可被运行时执行
probe 不应成为用户默认可见的调试位置
```

## 10. Breakpoint 与 Trace 的本质区别

Breakpoint 是控制流机制：

```text
命中 -> debuggee 暂停 -> 调试器读取 frame -> 用户检查状态
```

Trace 是观测机制：

```text
观察者已打开 -> 命中 -> 记录事件 -> 程序继续运行 -> UI 稍后读取摘要
```

二者不能混用。

如果用 Trace 模拟断点，会得不到真实 frame、locals、arguments、this。

如果用断点模拟 Trace，高频 Update 会不断暂停程序，无法使用。

因此设计上必须分离：

- Breakpoint：少量、精确、暂停、读取真实状态。
- Trace：高频、非暂停、有观察者才采集、聚合、丢弃明细也可接受。

## 11. Trace 聚合理论

Trace 面对的是高频事件流，所以它必须是 observer-driven，而不是后台永久开启。

后台默认开启 Trace 的问题：

- 用户没有打开视窗时仍有 CPU 和内存开销。
- 事件流会持续增长，必须额外清理。
- 用户容易误解为系统保存了完整历史。
- 多实体高频 Update 会把无意义数据推入调试状态。

因此正确生命周期是：

```text
Trace 视窗打开 / 客户端订阅
  -> 启用 Trace 采集
  -> 运行时记录 Enter 事件
  -> Session 聚合 snapshot

Trace 视窗关闭 / 客户端取消订阅
  -> 停止 Trace 采集
  -> 清空或冻结 snapshot
```

错误做法：

```text
每命中一次 -> 推送一次 UI 更新
```

这会造成：

- UI 卡顿。
- IPC 压力过大。
- 内存无限增长。
- 高频 Update 中数据没有可读性。

正确做法：

```text
debugSiteId -> hitCount / lastEntity / lastTimestamp / droppedCount
recent samples ring buffer
UI 定时拉取 snapshot
```

Trace 关注的是趋势和热点，不是完整历史。

所以允许：

- 明细丢弃。
- 保留 dropped count。
- 固定容量 ring buffer。
- UI 100-250ms 拉取一次摘要。

## 12. Watch / Pin Inspect 理论

表达式值很有用，但不能默认全部采集。

原因：

- 表达式数量可能很多。
- 某些表达式有计算成本。
- 插桩会改变代码形态。
- 临时值不一定有稳定 local slot。
- 全量采集会让调试 build 语义和性能偏离过大。

所以 Watch / Pin Inspect 的原则是：

```text
用户明确指定 + Watch 视窗或客户端观察打开 -> 才采集该值
```

采集方式可以是：

- 调试器 expression evaluation。
- 编译期插入临时 `DebugProbe.Value`。
- 后端受控读取特定 local / field。

但默认不把所有 pin 都变成 runtime value stream。

同理，手写 `GraphDebug.Watch` 可以作为源码里的观察声明存在，但声明不等于后台永久采集。只有调试会话打开 Watch 观察后，运行时才应该记录对应 value event。

## 13. 暂停时变量读取理论

真正的变量读取应该发生在暂停状态。

暂停后可读取的变量来源有几类：

```text
Arguments  -> 当前 stack frame 参数
Locals     -> 当前 stack frame 局部变量
This       -> 当前对象实例
Inspector  -> entity/component store 或行为字段
Watch      -> 显式采集的表达式值
```

其中 Arguments、Locals、This 是调试器 frame 概念。

Inspector 是引擎对象模型概念。

Watch 是用户观测概念。

当前实现边界：

- `ScriptDebugSession.ReadPausedSnapshot` 已预留 `IScriptFrameVariableBackend`，非 synthetic debugger stop 可通过该接口填充 Arguments、Locals、This。
- 实验层已有 `DapDebugSessionClient` 和 `DapScriptFrameVariableBackend`，用 fake DAP 响应验证 `threadId -> stackTrace -> scopes -> variables -> paused snapshot`。
- 实验层已有 `DapScriptStoppedEventResolver`，用 fake DAP `stopped` event 和 `stackTrace` 验证 `stopped.threadId -> ScriptStoppedEvent.ThreadId -> DebugMap source binding`。
- `DapProtocolClient` 暴露 `IDapEventSource.DrainEvents()`，`DapDebugSessionClient.DrainStoppedEvents()` 会过滤 stopped event，再由 resolver 转成 `ScriptStoppedEvent`。
- `DapDebugSessionRuntime` 是当前实验性组合对象，只组合 source breakpoint apply、drained stopped event resolve 和 paused snapshot frame variables。
- `DapAdapterProcess` 是当前最小 adapter process owner，只负责启动 stdio 进程并暴露 `DapProtocolClient`。
- `DapDebugSessionLauncher` 已用 fake DAP transport 验证 `initialize -> setBreakpoints -> configurationDone -> launch -> DapDebugSessionRuntime` 的实验握手。
- 当前还没有接真实 .NET debug adapter、attach 模式或异步事件泵；DAP launch/stopped/frame variables 仍是骨架和单元层闭环。
- synthetic probe stop 永远不调用 frame variable backend；它只能显示 Inspector 和 Watch / Pin Inspect。
- 未接入 frame variable backend 的真实 debugger stop 会把 Arguments、Locals、This 标记为 unavailable，而不是用 Inspector 或 Watch 值伪装。

这三者不能混成一种变量来源，否则会造成：

- 用户以为某个值来自真实暂停 frame，但其实只是上一次 probe 采样。
- continue 后旧 variables reference 仍被错误使用。
- 多实体情况下值来源不明确。

正确状态规则：

```text
Running:
  不读取 frame variables

Paused:
  读取当前 thread/frame scopes

Continue / Step:
  清空旧 frame / variables reference
```

## 14. DAP 的理论位置

DAP 是调试前端和调试后端之间的协议，不是语言语义模型。

它提供的是通用调试动作：

- initialize
- setBreakpoints
- launch / attach
- stopped
- stackTrace
- scopes
- variables
- continue
- next / stepIn / stepOut

它不理解蓝图节点，也不理解 Graph C# 的 `debugSiteId`。

所以系统边界应该是：

```text
蓝图 / Graph C# 语义
  -> ScriptDebugSession 归一化
  -> DAP source/instruction breakpoint
  -> DAP stopped/frame/variables
  -> DebugMap 反查蓝图语义
```

DAP adapter 只负责真实调试动作。

Graph C# 的语义解释必须留在 DebugMap / DebugSession。

## 15. DAP capability 的意义

不同调试器支持能力不同。

例如：

- 有的支持条件断点。
- 有的支持 hit count。
- 有的支持 instruction breakpoint。
- 有的支持 breakpoint locations 查询。
- 有的变量 evaluate 能力有限。

所以不能假设所有能力都存在。

正确流程：

```text
initialize
  -> capabilities
  -> 根据能力启用功能
```

当前 `DapBreakpointBackendCapabilities` 明确读取：

```text
supportsConditionalBreakpoints
supportsHitConditionalBreakpoints
supportsBreakpointLocationsRequest
supportsInstructionBreakpoints
```

当前 `DapDebugSessionClient` 已统一封装：

```text
initialize
setBreakpoints
configurationDone
launch
stopped event parsing
DrainEvents -> stopped event filtering
stackTrace
scopes
variables
```

`DapScriptBreakpointBackend`、`DapScriptStoppedEventResolver` 和 `DapScriptFrameVariableBackend` 都通过这层访问 DAP，避免 breakpoint、stopped event 和 frame variable 路径各自解析 JSON。
`DapDebugSessionRuntime` 只负责把这三个实验部件装配在一起；它不拥有 adapter 进程，也不启动后台事件泵。
`DapDebugSessionLauncher` 只负责一次性握手和 runtime 创建；真实 adapter 参数、进程生命周期策略、事件泵和 shutdown 协议仍需后续设计。

如果不支持条件断点：

```text
不要假装支持
不要在 UI 显示为 verified
返回 unsupported
```

这是调试体验可信度问题。

## 16. 为什么 setBreakpoints 必须按文件整组提交

DAP 的 source breakpoint 模型是按 source 文件替换断点集合。

也就是说：

```text
setBreakpoints(file, [bp1, bp2, bp3])
```

表示这个文件当前就是这三个断点。

如果新增一个断点时只提交新断点，旧断点可能会被 adapter 清掉。

所以 session 必须维护每个文件的完整 breakpoint state：

```text
用户增加/删除一个断点
  -> 更新本地完整集合
  -> 对该文件重新 setBreakpoints
```

这也是为什么断点状态必须集中管理，不能由每个 UI 控件独立发请求。

## 17. 多实体和高频 Update 的理论处理

真断点默认暂停整个 debuggee。

多实体情况下，一个行为可能挂在很多 entity 上。默认策略应该简单明确：

```text
任意实例命中 -> 暂停
```

如果用户只关心某个实体，需要过滤：

- entityId
- behaviorInstanceId
- condition
- hit count

优先使用调试器条件断点。

如果调试器不支持，再考虑 probe-assisted guard。

Trace / Watch 则天然面向高频和多实体，所以必须在有观察者时聚合：

```text
debugSiteId + optional entity -> hitCount / lastValue / droppedCount
```

## 18. 蓝图同步的理论流程

蓝图和源码共享同一份 breakpoint state。

蓝图设置断点：

```text
graphNodeId
  -> debugSiteId
  -> source/PDB location
  -> debugger breakpoint
  -> verified result
  -> source gutter + graph node 同步显示
```

源码设置断点：

```text
file + line + column
  -> source span
  -> debugSiteId candidates
  -> graph node candidates
  -> verified / ambiguous / sourceOnly
  -> blueprint highlight
```

断点命中：

```text
debugger stopped
  -> current thread/frame
  -> source/PDB/IL location
  -> debugSiteId
  -> graph node
  -> source + blueprint selection
```

这三条路径必须回到同一个 `debugSiteId`，否则同步会漂移。

## 19. 设计中的降级策略

调试系统必须明确区分“可用但降级”和“不可用”。

常见降级：

- 蓝图节点不可断：绑定到 owning statement。
- 源码位置不属于蓝图区域：source-only。
- 多个候选：ambiguous，等待 UI 选择。
- probe backend：只能 synthetic stop，不能读取真实 frame。
- DAP 不支持条件断点：unsupported。
- 没有 Watch 采样：Watch scope unavailable。

后端结果里的 `verified` 只表示真实 debugger / DAP backend 验证过断点位置。
probe backend 成功启用断点时只能返回 `applied + synthetic`，不能把 synthetic probe stop 标记为 verified。

`ScriptDebugSession` 创建时必须先校验 DebugMap schema、source path 和 source checksum。
校验失败时不能继续解析断点、Trace、Watch 或 paused snapshot。

不应该做的降级：

- 把 hidden probe location 伪装成真 source breakpoint。
- 把上一次 Watch 值伪装成当前暂停 frame local。
- 把 graph node id 当成稳定断点。
- 把未 verified 的 PDB 位置显示成 verified。

## 20. 当前 V1 的正确边界

当前 V1 应该验证的是调试语义闭环，而不是完整编辑器：

```text
源码可分析
IR 可生成
蓝图可投影
DebugMap 可生成
断点可双向绑定
Probe backend 可 headless 验证
DAP backend 可逐步替换 probe backend
Trace/Watch 和 Breakpoint 语义分离
```

不应该在这一阶段优先做：

- 完整 UI。
- 独立 Web 蓝图编辑器。
- Rider 插件工程。
- 任意 C# 到蓝图。
- 完整蓝图回写。
- IL weaving / profiler / ReJIT。

原因是这些都会放大复杂度，但不能先证明最关键的问题：

```text
蓝图节点能否可靠对应真实调试器位置？
源码断点能否可靠反查蓝图语义？
暂停变量能否来自真实 frame？
高频观察能否不暂停程序？
```

## 21. 推荐推进顺序

第一阶段：证明映射正确。

```text
source span
  -> debugSiteId
  -> graph node
  -> PDB sequence point
```

第二阶段：证明断点正确。

```text
source breakpoint
blueprint breakpoint
brace normalization
verified / ambiguous / sourceOnly / unbound
```

第三阶段：证明暂停正确。

```text
stopped event
thread/frame
DebugMap reverse lookup
source + blueprint selection
```

第四阶段：证明变量正确。

```text
arguments
locals
this
inspector
watch
```

第五阶段：证明非暂停观察正确。

```text
Trace aggregation
Watch / Pin Inspect
sampling
ring buffer
dropped count
```

第六阶段：再选择 UI。

```text
IDE / Rider / standalone editor
  -> 只消费本地服务协议
  -> 不自行解释 probeId
  -> 不复制 DebugMap 逻辑
```

## 22. 最终判断

这套方案成立的关键不在于“能不能画蓝图”，而在于能否坚持以下边界：

- 蓝图是源码语义投影。
- DebugMap 是唯一映射真相。
- `debugSiteId` 是调试语义锚点。
- 真断点走真实调试器。
- Trace / Watch 走非暂停观测。
- 变量读取只在暂停 frame 中可信。
- UI 只是客户端，不拥有调试语义。

只要这些边界稳定，后续无论接 Rider、独立编辑器、还是引擎内 editor，调试模型都不会重写。

## 23. 后续扩展架构讨论（未经验证）
本章把后续讨论统一收敛为一个独立研究点：如何从真实 C# 能力、项目索引、第三方 DLL 和用户代码中生成可蓝图化节点，并保证这些节点最终仍能回到源码、调试和写回流程。

这些内容不属于当前 V1 调试闭环的完成条件。当前 V1 只要求证明 `debugSiteId -> DebugMap -> Breakpoint / Trace / Watch / Paused Frame` 这条链路成立；本章记录的是后续可演进方向，均为未经验证的架构假设。

本章统一覆盖四类问题：
- 能力发现：用户方法、插件、第三方 DLL、项目索引。
- 能力约定：Blueprint Contract、签名可蓝图化、Unknown Effect。
- 图形表达：黑盒节点、构造节点、`new`、局部变量、作用域子图。
- 写回与调试：顺序屏障、保存后元数据、DebugMap 回填。

本章的默认约束是：

```text
能发现 API
  不等于能自动注册为蓝图节点

能注册为蓝图节点
  不等于能展开成可编辑子图

能显示成黑盒节点
  不等于能 pin 级 Watch、Trace 或安全重排
```

因此后续实现必须把 V2+ 能力拆成三个门：

```text
Candidate
  索引扫描得到的符号、方法、类型、构造函数或宏。

Opaque Graph Symbol
  签名可表达，可作为黑盒节点调用，但 body 不归蓝图写回系统拥有。

Full Graph Symbol
  签名和方法体都满足 Graph Contract，可展开、编辑、写回和逐节点调试。
```

只有 `Full Graph Symbol` 才能进入“蓝图编辑即源码编辑”的强语义路径。
`Opaque Graph Symbol` 可以用于搜索、调用、源码定位和 Step Into，但不能让蓝图写回系统假装拥有其内部语义。

### 23.1 节点扩展方向

以下内容是目前讨论形成的设计方向，尚未经过实现、测试或真实项目验证。

目标不是维护另一套“方法节点库”，而是让真实能力自动成为节点：

```text
真实 API / 用户方法 / 类型系统 / 语言结构
  -> 节点描述
  -> 蓝图投影
```

也就是说：

- 方法可以成为 Function 节点。
- 字段可以成为 Field 节点。
- `if`、`return`、local、loop 可以成为语法节点。
- `new Vec3(...)` 这类构造可以成为 Make / Construct 节点。
- 少数高级组合逻辑可以成为 Macro 节点。

不推荐长期维护：

```text
TranslateNode
KeyDownNode
MoveToNode
PlaySoundNode
```

更推荐维护：

```text
FunctionId
TypeId
Effect
Context
Signature
Doc
Debug contract
```

节点 UI、pin、搜索、文档、断点和 DebugMap 都从这些描述派生。

### 23.2 插件能力自动引入

插件可以自动向蓝图系统提供节点，但前提是插件必须声明并满足蓝图约定。

推荐流程：

```text
用户安装插件
  -> 读取插件 manifest / schema / attribute metadata
  -> 校验 FunctionId / TypeId / MacroId
  -> 校验参数类型、返回类型、effect、context
  -> 合并到项目 BindingRegistry / Node Index
  -> Blueprint palette 自动出现节点
```

不推荐：

```text
扫描插件程序集
  -> 所有 public 方法自动变节点
```

原因：

- public 方法可能有隐藏副作用。
- 参数类型可能不可图形化。
- 返回值可能不可序列化或不可调试。
- 方法可能阻塞、异步、访问文件或网络。
- 版本升级可能破坏节点身份。

插件能力可以分三类：

```text
Function Extension
  暴露真实 API 方法为节点

Type Extension
  暴露新 pin 类型、Inspector 类型、序列化类型

Macro Extension
  暴露快捷组合逻辑，可展开为 Graph C# / IR / 子图
```

### 23.3 Blueprint Contract

插件或用户方法想自动进入蓝图系统，需要满足一套 Blueprint Contract / Graph Contract。

最低要求：

```text
稳定身份
  pluginId / functionId / typeId / macroId / migration

可图形化签名
  参数名稳定
  参数类型可作为 pin
  返回值类型可表达
  默认值可序列化

副作用声明
  pure / read world / mutate world / spawn / destroy / IO / async

上下文限制
  Update / FixedUpdate / init / editor / construction

调试约定
  可否断点
  可否 Trace
  可否 Watch 返回值
  是否有异步或延迟行为
```

普通插件可以只给代码使用。只有声明并通过约定校验的能力，才进入蓝图节点系统。

插件接入时需要保留两个注册结果：

```text
Discovery result
  插件或程序集里能找到什么。

Contract result
  哪些能力通过 Graph Contract，可以进入蓝图 palette。
```

如果插件 manifest 缺少 effect、context、稳定 ID 或迁移信息，可以保留为诊断候选，但不能静默注册为可写回节点。

### 23.4 所有 C# 的承载分级

更准确的目标不是“所有 C# 都能展开成蓝图”，而是：

```text
所有 C# 都可以被蓝图视图承载；
只有满足 Graph Contract 的部分可以展开为蓝图。
```

建议分级：

```text
Level 0: Source-only
  只在源码里看，蓝图不显示或只显示占位。

Level 1: Opaque Code Block
  蓝图显示 C# 黑盒块，可高亮和定位源码，但不可展开编辑。

Level 2: Callable Black Box
  方法签名满足蓝图约定，内部任意 C#，蓝图只显示输入/输出 pin。

Level 3: Full Graph
  方法体也满足 Graph C# 子集，可完全展开、编辑、回写、逐节点调试。
```

优先推荐从方法边界折叠，而不是从任意语句碎片折叠。

原因：

- 方法天然有输入、输出、返回值和调用栈边界。
- 语句碎片的输入输出、局部变量捕获、副作用和控制流更难稳定推断。
- 方法黑盒可以自然支持 Step Into 源码。

不同级别允许的能力必须明确限制：

```text
Level 0 Source-only
  只提供源码诊断、源码定位和普通 IDE 调试。

Level 1 Opaque Code Block
  可以高亮源码范围，但不生成稳定 pin，不支持蓝图写回内部语义。

Level 2 Callable Black Box
  可以生成输入/输出 pin 和执行 pin；可以断在调用边界；Step Into 回到源码或反编译位置。

Level 3 Full Graph
  可以生成内部节点、pin 级 Watch、Trace、断点归一化、DebugMap 和写回。
```

UI 不应把 Level 1/2 显示成可编辑内部图。黑盒节点的可读性来自签名、文档和调用边界，而不是伪造内部节点。

### 23.5 用户 helper method 自动节点

用户自己写的 helper method 可以自动成为蓝图节点，不需要手写节点描述。

示例：

```csharp
protected override void Update(float delta)
{
    var cover = FindCover(Self, ThreatPosition);
    MoveTo(Self, cover);
}

private Vec3 FindCover(EntityRef self, Vec3 threatPos)
{
    // 内部可以使用复杂 C#、循环、LINQ、缓存或第三方库。
}
```

蓝图可以显示：

```text
[Find Cover] -> cover
[Move To]
```

判定应拆成两层：

```text
Call Boundary Contract
  决定这个方法调用能不能折叠成节点。

Method Body Contract
  决定这个方法内部能不能展开成子图。
```

规则：

```text
签名可蓝图化 + 方法体可蓝图化
  -> 可展开用户方法子图

签名可蓝图化 + 方法体不可蓝图化
  -> opaque 用户方法节点

签名不可蓝图化
  -> source-only
```

没有 effect 标注且方法体不可分析时，默认应保守处理：

```text
UnknownEffect / OpaqueImpure
```

它可以显示成节点，但必须带执行 pin，不能当 pure 表达式随意重排。

helper method 的身份需要分成缓存身份和持久身份：

```text
缓存身份
  document path + containing type + method name + parameter type list + arity

持久身份
  显式 [GraphCallable("stable.id")] 或项目索引生成并可迁移的 methodId
```

没有显式稳定 ID 的 helper method 可以先作为当前工程内候选节点使用，但断点、布局和跨文件引用不能只依赖文件路径或方法名。重命名、移动文件、partial class 合并、namespace 变化和 overload 调整都必须触发重绑或迁移诊断。

### 23.6 签名可蓝图化定义

“签名可蓝图化”指方法边界能不能稳定表达为蓝图节点。

换句话说，蓝图必须能画出：

```text
调用身份
输入 pin
输出 pin
副作用边界
上下文限制
```

建议第一版保守规则：

```text
方法可静态解析
非泛型或泛型已封闭
非 async
无 unsafe / pointer / Span<T>
无 dynamic / object 边界
无 delegate / Action / Func callback
无 ref 参数
out 参数第一版可禁用，后续可映射为输出 pin
参数和返回值属于已注册 GraphType
返回值最多一个，tuple 需要明确拆 pin 支持
```

签名通过后还需要产生一个最小 Contract 输出：

```text
symbolId
displayName
inputPins
outputPins
effect
context
debugCapabilities
migrationIds
contractHash
```

`contractHash` 只描述调用边界。方法体变化只应改变 `bodyHash`，不应让已有调用点丢失 pin；签名、effect、context 或 pin 类型变化才需要更新调用点诊断和连线。

允许示例：

```csharp
private bool IsLowHealth(float hp)
private Vec3 FindCover(EntityRef self, Vec3 threat)
private void ApplyKnockback(EntityRef target, Vec3 force)
private float Clamp01(float value)
```

不允许或 source-only 示例：

```csharp
private T Pick<T>(IEnumerable<T> items)
private async Task<Vec3> FindCoverAsync(...)
private void Read(out Vec3 result)
private void Modify(ref float speed)
private void OnDone(Action callback)
private object GetAnything()
```

### 23.7 自动发现与增量索引

用户方法、项目工具方法、插件方法、类型描述和宏节点不应该每次全量查询。

建议使用分层缓存和增量更新：

```text
首次加载 / 冷启动
  -> 全量扫描一次
  -> 建索引

文件修改
  -> 只重扫该文件里的方法
  -> 更新所属 type / Behavior 的节点

程序集或插件变更
  -> 只重扫该 assembly / plugin

manifest / schema 变更
  -> 只重载该 manifest
```

缓存层级：

```text
Project Node Index
File Method Index
Plugin Node Index
Type Index
BindingRegistry
```

每个方法节点可以维护：

```text
stableKey:
  document path
  containing type
  method name
  parameter type list
  arity

hash:
  signatureHash
  bodyHash
  contractHash
```

变化策略：

```text
只改方法体
  -> 节点 pin 不变，只更新 body graphability / debug map

改参数或返回值
  -> 节点 pin 改变，更新调用点诊断和蓝图连线

改 effect / context
  -> 更新 analyzer 和节点执行语义

改方法名
  -> 尝试 rename tracking / migration
```

索引缓存必须记录输入来源和失效原因：

```text
source file changed
assembly identity changed
plugin manifest changed
schema version changed
Graph Contract version changed
engine binding version changed
```

失效后不能沿用旧的 `debugSiteId`、旧 pin id 或旧 PDB 位置。索引只负责重新生成候选和 contract，断点、布局和 Watch 需要通过 semantic anchor 重新绑定。

### 23.8 构造函数与 new 表达式

`new` 也可以蓝图化，但不能默认允许任意 C# class。

推荐模型：

```text
new Vec3(...)
  -> Make Struct / Make Value 节点

new RegisteredDataClass(...)
  -> Construct 节点

new EngineObject / Component / Entity
  -> 不直接 new，走 Spawn / AddComponent / Factory

new UnknownClass(...)
  -> source-only 或提示 Extract Method
```

构造节点还必须声明对象语义：

```text
Value
  值类型或不可变数据，适合 Make Value 节点。

OwnedData
  当前脚本实例拥有的可序列化数据，需要明确保存和复制规则。

RuntimeObject
  引擎对象、组件、实体、资源句柄，不能直接 new，必须走 Spawn / AddComponent / Factory / Asset load。

ExternalObject
  第三方引用对象，默认 Opaque，不能假设可序列化或可跨帧保存。
```

蓝图系统不能只根据 `new` 的语法形态决定节点类型，必须由 `TypeId`、构造 contract 和 ownership/lifetime 规则共同决定。

多个构造方法用稳定身份区分：

```text
constructorId = typeId + ".ctor(" + parameterTypeList + ")"
```

例如：

```text
game.InventoryItem.ctor()
game.InventoryItem.ctor(string)
game.InventoryItem.ctor(string,int)
```

UI 可以显示为多个节点，也可以显示一个节点加 overload 选择器。

复杂构造更推荐 factory method：

```csharp
InventoryItem.CreateWeapon(name, power)
InventoryItem.CreatePotion(id)
```

因为 factory method 比 constructor 更适合蓝图搜索、命名和文档。

### 23.9 new 结果接局部变量

代码：

```csharp
var item = new InventoryItem("Sword", 10);
Use(item);
```

不应被看成一个单一节点，而应拆成两个语义：

```text
Construct InventoryItem
Set Local item
Get Local item
Use
```

紧凑视图可以折叠为：

```text
[Construct InventoryItem] -- item --> [Use]
```

但底层 IR 仍应保留：

```text
StoreLocal item
LoadLocal item
```

必须展开 Local 的情况：

```text
变量被多次使用
变量重新赋值
变量跨分支使用
变量被 Watch / Pin Inspect
用户给变量设置断点或显式 Promote to Local
```

### 23.10 类、方法与作用域子图

方法和类不仅是节点来源，也应该是作用域容器。

建议作用域模型：

```text
Class scope
  fields
  methods

Method scope
  parameters
  locals
  body graph

Block scope
  block locals
  nested control flow
```

变量分类：

```text
Parameter
  生命周期：一次方法调用
  作用域：整个方法体
  调试来源：frame arguments

Local variable
  生命周期：一次方法调用
  作用域：声明所在 block / method
  调试来源：frame locals

Field
  生命周期：对象实例
  作用域：整个类
  调试来源：Inspector / this fields
```

蓝图变量可见性必须跟 C# lexical scope 一致：

```text
当前 block
  -> parent block
  -> method scope
  -> class fields
  -> global / registered values
```

方法折叠成节点时：

```text
外层只看到方法签名 pin
内部 locals 不外泄
Step Into 才进入方法源码或子图
```

### 23.11 蓝图写回代码顺序

蓝图写回 C# 时，顺序不能来自节点坐标。

正确来源：

```text
执行顺序：exec edge
表达式顺序：data dependency
变量声明位置：scope + dominance
副作用顺序：effect barrier
代码生成：局部 Roslyn rewrite
```

必须区分：

```text
pure data graph
  -> 可生成表达式

impure exec graph
  -> 必须按 control flow 生成 statement
```

有副作用或 unknown effect 的节点不能重排：

```text
Set Field
mutating call
Spawn / Destroy
IO
Random
Time
Unknown / OpaqueImpure
```

局部变量声明应放在：

```text
所有使用点的最近公共合法作用域
并且在所有使用前
并且不穿过会改变依赖值的副作用
```

推荐写回流程：

```text
1. 确定写回范围：method / block / selected subgraph
2. 建立 scope tree
3. 从 entry exec pin 遍历 control flow
4. 对每个 statement 节点生成 statement
5. 对 statement 的输入 data graph 生成 expression
6. 对多次使用或需要调试身份的 expression 提升 local
7. 插入 local declaration 到最近公共合法 scope
8. 生成 if/else/return/loop 等结构化语句
9. 用 Roslyn 替换原 method/block syntax
10. Formatter 格式化局部范围
```

### 23.12 保存写回与新增节点元数据

蓝图编辑时可以使用临时元数据，保存后再生成权威元数据。

建议分层：

```text
编辑期临时元数据
  tempNodeId
  draft semantic payload
  layout
  selection
  undo / redo

保存后权威元数据
  sourceSpan
  debugSiteId
  Static DebugMap

debug 编译后元数据
  PDB sequence point
  IL offset
  method token
  local scope
  probeId(optional)
  breakableVerified
```

完整流程：

```text
1. 用户在蓝图新增节点
2. 节点获得 tempNodeId
3. 用户连线、设置参数、布局
4. 用户保存
5. 蓝图编辑模型写回 .ash.cs
6. 重新 parse / analyze / bind / lower
7. 重新投影 BlueprintGraph
8. 用 semantic anchor 把 tempNodeId 映射到新 debugSiteId
9. 迁移布局、选中状态、断点、Watch
10. 生成 Static DebugMap
11. 如处于 debug session，触发 debug compile
12. 读取 PDB，生成 Compiled DebugMap
13. 重新 apply breakpoints
14. UI 显示 verified / unbound / ambiguous
```

不能让前端直接生成：

```text
probeId
PDB sequence point
IL offset
breakableVerified
```

这些必须来自 debug compile 和真实 PDB / debugger 结果。

Compiled DebugMap 的可用性必须由构建校验字段决定：

```text
buildId
sourceChecksum
assemblyMvid
pdbId
schemaVersion
Graph Contract version
```

任一字段不匹配时，Compiled DebugMap 立即失效：

```text
失效后
  不能沿用旧 PDB sequence point
  不能沿用旧 IL offset
  不能把旧 breakableVerified 当成事实
  不能用旧 probeId 重新绑定断点

允许保留
  breakpointAnchor
  layout anchor
  Watch anchor
  用户选择和临时 UI 状态
```

当前 V1 的 `ScriptBreakpointState` 已生成 `breakpointAnchor`，只表达用户原始断点意图。
它不等于当前 `debugSiteId`，也不表示跨构建重绑定算法已经完成。

重新编译后，断点和 Watch 必须先通过 `breakpointAnchor` / semantic anchor 绑定到新的 `debugSiteId`，再由新的 Compiled DebugMap 绑定到真实 PDB / backend breakpoint。

### 23.13 第三方 DLL 扫描与宽松引入

第三方 DLL 可以像 IDE 一样扫描，建立项目级索引，而不是每次蓝图打开时临时反射。

宽松引入应分三段：

```text
Scan
  读取符号和 metadata，生成候选。

Classify
  判断签名、类型、effect、context、debug capability。

Register
  只把通过最低 Graph Contract 的候选加入 palette。
```

第三方 DLL 默认不能生成 `Full Graph Symbol`。除非同时拥有源码、Graph Contract、可分析方法体和写回策略，否则只能作为 Opaque / UnknownEffect 调用边界。

扫描目标：

```text
assembly metadata
namespace
type
constructor
method
property
field
parameter type
return type
generic arity
attribute
XML doc id
```

如果有源码，则优先用 Roslyn，因为能拿到：

```text
source span
XML 注释
nullable 信息
default parameter
method body
using / namespace
```

索引可以生成：

```text
Symbol Index
Type Index
Function Index
Constructor Index
Plugin Index
Graph Node Candidate Index
Diagnostic Index
```

宽松策略下，不需要把安全性作为硬门槛。

硬门槛只保留“蓝图技术上无法表达”的情况：

```text
pin 无法表达
调用目标无法静态确定
开放泛型无法实例化
unsafe / pointer 无法画 pin
ref / out 第一版未支持
delegate / callback 无法表达控制流
```

其他风险只作为提示：

```text
IO
网络
线程
Task / async
Random
DateTime
全局状态
第三方未知副作用
复杂循环
可能阻塞
不可序列化返回值
```

这些可以显示成节点，但带标签或 tooltip：

```text
May Block
IO
Async
Unknown Effect
Not Deterministic
Opaque
Not Serializable
```

因此扫描器的定位可以是：

```text
API 发现器
Graph Contract 诊断器
Node Candidate 生成器
Adapter 草案生成器
```

而不是严格安全审计器。

### 23.14 索引驱动注册

有项目索引后，可以由索引直接生成注册表，但要区分：

```text
Index Candidate
  扫描得到的候选符号

Registered Graph Symbol
  已经通过最低 Graph Contract 校验、可以被蓝图使用的符号
```

宽松模式下，可以自动注册更多符号：

```text
当前 Behavior 内签名可蓝图化的 helper method
签名可表达的项目工具方法
简单 enum
简单 value struct / record struct
已带 GraphCallable / GraphType attribute 的符号
插件 manifest 明确声明的符号
第三方 DLL 中签名可表达的方法，作为 Opaque / Unknown Effect 节点候选
```

但“扫到了”不等于“完全可信”。

注册后的 Unknown 方法应默认：

```text
Opaque
UnknownEffect
有执行 pin
不参与 pure 表达式内联
不参与自动重排
可以被调用、断点、Step Into 源码或反编译位置
```

这允许用户快速使用第三方库，同时不让写回系统错误改变执行语义。

注册模式建议显式化：

```text
Strict
  只有显式 GraphCallable / manifest 声明的符号可进入 palette。

Project
  当前项目内签名可表达的 helper method 可作为候选，缺少 effect 时为 OpaqueImpure。

Loose
  第三方 DLL 签名可表达的符号可作为 Opaque / UnknownEffect 候选，但默认不参与自动写回重排。
```

无论哪种模式，`Registered Graph Symbol` 都必须带来源信息和 contract 诊断。UI 可以让用户看到“为什么这个符号只是候选，为什么不能展开，为什么必须保留执行线”。

### 23.15 DLL 命名空间与冲突处理

DLL 自身已有程序集身份、命名空间、类型全名和方法签名。多数符号冲突可以依赖 CLR / Roslyn / metadata 发现。

内部身份建议使用：

```text
assembly identity
namespace
type full name
member name
parameter type list
generic arity
```

例如：

```text
PathLib::PathLib.PathFinder.Find(PathLib.Grid,PathLib.Point,PathLib.Point)
```

这比手写短 `FunctionId` 更不容易冲突。

需要区分几类冲突：

```text
CLR assembly identity 冲突
  加载或引用阶段暴露。

C# using / 短名歧义
  Roslyn symbol resolution 暴露。

蓝图显示名冲突
  CLR 不会报错，UI 需要处理。
```

显示名冲突不应阻止注册。

UI 策略：

```text
短名优先
冲突时显示 namespace
仍冲突时显示 assembly
```

例如：

```text
Noise
Noise (A.Math)
Noise (B.Procedural)
```

类型 pin 也一样：

```text
Point
PathLib.Point
Geometry.Point
```

所以不需要因为潜在命名空间冲突而设置过多前置限制。内部使用完整符号身份，UI 层解决可读性。

### 23.16 Unknown Effect 与写回顺序屏障

即使安全性不是硬门槛，写回顺序仍然必须保守。

问题不在于是否允许调用第三方 API，而在于保存蓝图回 C# 时不能改变程序语义。

蓝图节点坐标、扫描顺序、搜索结果顺序都不能作为代码顺序。

正确顺序来源：

```text
exec edge
control flow
data dependency
scope
effect barrier
```

Unknown / Opaque 方法必须当作 impure barrier：

```text
Unknown method
  -> 有执行 pin
  -> 保持用户指定执行顺序
  -> 不自动交换顺序
  -> 不自动合并
  -> 不当成 pure expression 内联
```

例如：

```csharp
var a = Foo();
var b = Bar();
```

如果 `Foo` 和 `Bar` 都来自未知第三方 API，就不能自动写成：

```csharp
var b = Bar();
var a = Foo();
```

它们可能访问全局状态、缓存、随机数、时间或外部资源。即使系统不拦截，也必须保持顺序。

典型顺序约束：

```text
副作用调用顺序
  SetHealth(10) 必须在 Damage(5) 之前或之后，取决于 exec edge。

变量赋值和使用顺序
  Set Local 必须在 Get Local 之前。

同一变量多次赋值
  每次使用必须绑定到正确版本。

控制流顺序
  if / else / loop / return 必须生成结构化代码。

pure 表达式
  可以内联或重组，但前提是确认为 pure。
```

effect 至少需要形成以下保守格局：

```text
Pure
  无副作用，可作为表达式内联。

ReadWorld
  读取世界或服务状态，不写入；不能跨 MutateWorld/Spawn/Destroy/IO 重排。

MutateWorld
  修改世界、组件、字段或资源状态，必须保留 exec 顺序。

SpawnDestroy
  改变对象生命周期，是强顺序屏障。

IO / Blocking / Async
  访问外部资源、可能阻塞或延迟完成，默认强顺序屏障。

UnknownEffect / OpaqueImpure
  不知道副作用，按最保守 impure barrier 处理。
```

写回系统只能在 effect 证明允许时重排。不能因为两个节点看起来只有 data edge，就跨越 Unknown/Opaque 节点合并、内联或交换顺序。

因此宽松引入 API 不等于宽松重排代码。

最低规则：

```text
签名可表达
  -> 可以生成节点

effect 未知
  -> 生成 OpaqueImpure 节点
  -> 保留执行线
  -> 作为写回顺序屏障
```

推荐推进顺序：

```text
1. 固定 V1 调试闭环
   debugSiteId -> DebugMap -> breakpoint / trace / watch / paused frame

2. 定义 Graph Contract schema
   FunctionId / TypeId / pins / effect / context / debug capability / migration

3. 建立 effect lattice 和写回 verifier
   证明哪些节点可内联、哪些节点必须保序

4. 建立 Project Node Index
   先覆盖当前工程 helper method 和注册类型

5. 支持 Opaque helper method 节点
   签名可蓝图化但方法体不可展开时，作为调用边界

6. 支持第三方 DLL 候选
   默认 Opaque / UnknownEffect，只做调用、定位和 Step Into

7. 再扩大 Full Graph
   只有当方法体、类型、effect、debug map 和写回都可验证时才展开
```

这个顺序可以防止“索引扫到了很多 API”反过来扩大 Graph C# 子集，避免 V2 便利性破坏 V1 的源码真相、调试真相和写回安全。
