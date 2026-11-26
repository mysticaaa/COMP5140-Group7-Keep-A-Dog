# 5.0 VR 玩家交互走查

日期：2026-11-26。目标文件：`docs/qa/dog1-v5-vr-audit.md`。

**状态：本轮本地走查完成。** 23 个验证入口全部通过，真实 DeepSeek 六例协议联调、Windows 5.0 构建以及无图形／带图形启动检查分别通过。发布入口为 `main` 与 `5.0` 标签，实体头显仍未实测。

本版以 `douga-3.0` 的统一 AI 与生活系统为基础，整合 main 的概率训练、搬碗和扳机抓取。目标是把同一个游戏从开始陪伴、照护、训练、出门玩到第一周完成，同时验证取消、暂停、失去追踪和目标变化时不会乱动或误领奖。

## 玩家流程与覆盖矩阵

| 玩家交互 | 实际行为与应检查结果 | 对应验证入口 | 本版结果 |
| --- | --- | --- | --- |
| 开始陪伴、选择犬种 | 新档默认边牧；原犬种可选；更换后交互目标和骨架仍绑定当前狗 | [Headset](../../Assets/wjy_workspace/Dog1/Editor/Dog1HeadsetValidation.cs)、[DefaultBreed](../../Assets/wjy_workspace/Dog1/Editor/DogDefaultBreedValidation.cs)、[V5 Journey](../../Assets/wjy_workspace/Dog1/Editor/Dog1V5JourneyValidation.cs) | 通过，覆盖界面、犬种骨架及跨流程绑定 |
| 行走、转向、空间边界 | 左摇杆沿头部水平方向行走，右摇杆一次转 30°，松杆后可再次转；移动受当前场景边界约束 | Headset、V5 Journey；真实摇杆与房间追踪需设备 | Unity／虚拟输入验证通过；实机未测 |
| 掉线、晚启动与重连 | XR 运行时不可用时保留桌面；会话恢复后重启手柄／射线，F9 或设置可重试；切换时取消当前语音 | Headset、V5 Journey、`Dog2OpenXR` 状态检查 | 桌面回退与模拟恢复边界通过；真实 Quest Link 运行时未验收 |
| X 收放手账、七个页面 | 故事、照料、训练、出门、设置、犬种、动作；手账隐藏后不能继续射线点中；有互动时可显示近处提示 | Headset、V5 Journey | 通过，包含收放跨流程和真实几何射线 |
| 射线点击与抓取互斥 | 必须按下、松开完成点击；抓取扳机、暂停／追踪重置后的释放不应误点菜单 | [PhysicalProps](../../Assets/wjy_workspace/Dog1/Editor/Dog1PhysicalPropsValidation.cs)、Headset、V5 Journey | 通过，包含连续失踪、保持按键恢复和释放后的新点击 |
| 抓球、毛绒玩具、场景小玩具 | 双手和狗之间保持唯一持有；抓取跟随手，释放后受重力碰撞；投掷速度有上限；切场景释放旧物品 | PhysicalProps、V5 Journey | 通过，包含跨输入及切场景释放 |
| 展示手里的玩具 | 在狗可见、距离合适且手速较慢时短暂展示，狗看向玩具并摇尾，不把手中物品当作可叼取 | PhysicalProps、场景观察、V5 Journey | 场景观察与模拟交互通过 |
| 搬动食水碗 | 提起保持水平，放下直立；狗的观察、探索和照护读取碗当前位置，不继续在旧位置吃喝 | [Integration](../../Assets/wjy_workspace/Dog1/Editor/Dog1IntegrationValidation.cs)、PhysicalProps | 通过，包含最终 Integration 复验 |
| 喂食、换水与自然语言吃喝 | 照护按钮添粮／水；模型 `eat`／`drink` 只消费已有内容。手持、移动、空、过高或不可达的碗不能完成，低头过程被中断也不计成功 | Integration、V5 Journey | 通过，包含移动碗消费和设施补充输入 |
| 扔球、要求叼回 | 球落稳后按实际位置接近、叼起、返回并放下；交付必须真实到达。当前抛出动作本身不会自动启动追取 | PhysicalProps、[Experience](../../Assets/wjy_workspace/Dog1/Editor/Dog1ExperienceValidation.cs)、[Controller](../../Assets/wjy_workspace/Dog1/Editor/Dog1ControllerValidation.cs) | 通过，含跨层拒绝；自动追取未实现 |
| 头侧抚摸 | 空手缓慢移动一段时间才回应，持物／按抓取键不触发；探头和摇尾有冷却，不添加未经验证的翻滚动作 | V5 Journey、实际 `ReactToPet`；真实手部位置与触摸阈值需设备 | 模拟输入与实际反馈动作通过；真实手部阈值未实测 |
| 邀玩、安静与稍后 | 伏身邀玩后自然等待；玩家可陪玩、安静陪伴或稍后。狗自主休息时可轻声陪伴或留出空间 | Experience、[Conversation](../../Assets/wjy_workspace/Dog1/Editor/Dog1ConversationValidation.cs)、Headset | 通过 |
| 趴下、狗屋、软垫休息 | 实际完成才恢复体力、记录相应陪伴任务；自主休息不自动替玩家领任务或奖励 | Experience、[RestLife](../../Assets/wjy_workspace/Dog1/Editor/DogRestLifeValidation.cs)、[HouseRest](../../Assets/wjy_workspace/Dog1/Editor/DogHouseRestValidation.cs) | Experience 与 HouseRest 通过 |
| 遛弯、取消与清理 | 到路标后等狗真正到达再算完成；可取消，清除路标／跟随且不领奖；清理与废物数量一致 | V5 Journey、Experience、[Life](../../Assets/wjy_workspace/Dog1/Editor/DogLifeValidation.cs) | 通过，包含遛弯取消跨流程 |
| 门、花洒、电梯、设施补充食水 | 看向并在范围／无遮挡条件下操作；电梯移动 Origin，不覆盖真实头部上下移动；不同楼层不会假召回或假交付 | V5 Journey 的 `RunFacilities`、Headset、OwnerOnSameFloor 守卫 | 通过：设施 37 项、模拟跨层及补充输入均有验证 |
| 六技能概率训练 | 40% 起，实际完成才 +10%，90% 封顶；未抽中或动作失败执行安全备选，不假成功；按钮所选技能不能被错误模型计划替换 | [TrainingProtocol](../../Assets/wjy_workspace/Dog1/Editor/DogTrainingProtocolValidation.cs)、Integration | 通过 |
| 表扬与跨活动边界 | 十二秒内开始讲话可强化；真诚、否定、举例、重复、过期各自处理；新活动不能把表扬串到旧训练 | Integration、V5 Journey、真实模型协议联调 | Unity 窗口、延迟和跨活动边界通过；真实模型语义另有 6/6 结果 |
| 模糊表达、多动作与排队 | 自由输入先交模型；动作目录、目标、需求、空间条件均校验。三个待执行槽位、五秒原始期限、同句顺序、过期取消、当前动作先完成 | Controller、[Queue](../../Assets/wjy_workspace/Dog1/Editor/Dog1CommandQueueValidation.cs)、[Parser](../../Assets/wjy_workspace/Dog1/Editor/ControllerParserValidation.cs) | Controller、Queue、Parser 通过；真实模型语义另有 6/6 结果 |
| 条件教学与存档 | 明确教授未来规则才建立试练，实际成功并表扬后保存；重载可唤回，不因说“我现在难过”就新建约定 | [LearningCondition](../../Assets/wjy_workspace/Dog1/Editor/DogLearningConditionValidation.cs)、Experience、Conversation | 通过 |
| 五章目标、积分、场景解锁 | 照护完成与奖励冷却分开；等级和骨头限制解锁，场景到访与章节目标保存；第五章完成后自由生活 | Life、Experience | 通过 |
| 暂停与恢复 | 游戏时间和生活消耗暂停；恢复不重复任务；持有物放下，原抓取扳机释放不误点；电梯移动保留头部追踪 | Headset、V5 Journey | 通过，包含菜单、持物和设施恢复 |
| 开发者动作浏览 | 全身、微动作和步态可筛选／翻页／连播，当前动作完成后切换；演示不推进任务或发奖 | Headset、[MotionRecovery](../../Assets/wjy_workspace/Dog1/Editor/DogMotionRecoveryValidation.cs)、V5 Journey | 通过 |
| 语音失败与模型故障 | 固定 WAV 经本机 Whisper 进入同一 Agent；取消／静音不发动作；401、限流、无效计划、服务断开不伪装成功；逐轮转写保留 | Headset、[Auth](../../Assets/wjy_workspace/Dog1/Editor/Dog1AuthValidation.cs)、Controller、真实模型联调 | Headset、Auth、Controller 通过；真实模型语义另有 6/6 结果 |

## 本轮发现并修复的跨功能问题

### 抓取扳机在连续追踪丢失后可能误点 UI

原重置逻辑会在第二个失踪帧清掉输入归属，恢复追踪后仍按着的扳机可能被当成新的菜单按下。修复后，未知追踪不证明已释放，重置会保留阻断状态；真实跟踪恢复后，释放由物品流程消费，再允许新的菜单按键。

验证应包括多帧失踪、保持扳机恢复、实际释放、随后新的按下与松开，不能只测单次 `ResetGrip`。

### 新活动后的表扬可能强化旧训练

原训练表扬窗口只看技能和时间。训练成功后紧接着休息、陪玩、故事回应或场景切换，再按表扬可能仍给旧技能加成功率。现在新活动会撤销旧待确认训练，避免跨活动错强化；正常窗口、语音开始时间和一次性强化仍保留。

### 电梯跨层后可能假靠近、假交付

原靠近成功仅使用水平距离，玩家和狗在不同楼层、同一水平位置时也可能算完成。现在 Dog1 为狗提供玩家的地面高度：VR 读 Origin，桌面从固定眼高换算。靠近、取物入口、跟随途中和交付完成都检查同层，保留原有水平成功距离。

本次不会让狗自行搭电梯；不同层时说明不可达，玩家回到同一层后可继续互动。正常佩戴眼高和蹲下不能被误判成跨层。

### 花洒状态不能依赖粒子当前是否发射

原先使用 `ParticleSystem.isEmitting` 决定花洒开关，在视野外或受到粒子裁剪时可能与玩家上次选择不一致。现在使用 `showerRunning` 保存权威状态，水流与飞溅两组粒子一起启停；关闭调用 `StopEmittingAndClear`，同时清掉残留粒子。设施专项验证三扇门、开关两组粒子及暂停恢复，共 37 项通过。

## 证据与最终结果

运行输出位于本机本轮隔离走查目录，分组结果为 `v5-core-results.json`、`v5-experience-results.json`、`v5-motion-results.json`、`v5-visual-results.json`。当前标准回归 20 套已全部 PASS：核心 8 套、玩家体验与异常 8 套、骨架／犬种／狗屋 3 套、Headset Graphics 1 套。首次失败与重测证据分别保留，不能把未完成的复验直接改写为 PASS。

| 额外专项 | 当前结果 | 证据 |
| --- | --- | --- |
| 最终整合场景复验 | 通过，81 项断言 | `Dog1IntegrationValidation.Run` 最终复验 |
| 三门、花洒双粒子及暂停恢复 | 通过，37 项断言 | `integration-20261126-221024-730/Dog1V5JourneyValidation.RunFacilities.log`，成功标志 `DOG_V5_FACILITIES_OK` |
| V5 Journey 核心虚拟输入 | 通过，87 项断言 | `integration-20261126-222047-069/Dog1V5JourneyValidation.Run.log:576` |
| 虚拟输入探针 | 通过，4 项断言 | `integration-20261126-221547-106/Dog1V5JourneyValidation.RunInputProbe.log:478` |
| 本轮真实 DeepSeek 六例 | 通过，6/6 | `v5-live-protocol.log:797`，成功标志 `DOG_TRAINING_LIVE_PROTOCOL_OK cases=6 scope=protocol-only` |
| Windows 5.0 构建 | 通过 | `v5-windows-build.log`：`KEEP_A_DOG_BUILD: Succeeded`；`v5-build-result.json` |
| 独立程序启动检查 | 无图形、带图形两次均通过 | 各十五秒，只验证初始化；完整交互来自上述场景回归 |

标准回归已涵盖 Headset 图形渲染、真实几何射线、固定 WAV 通过本机语音服务和模型桩后实际坐下；MotionRecovery、DefaultBreed 与 HouseRest 也均已通过。标准 20 个入口，加 V5 Journey 核心、虚拟输入探针和设施三个入口，共 **23/23 通过**。最终 Integration 的 81 项断言是同一整合入口的复验结果，不另加一个入口计数。

Journey 首次遇到脚本编译和 Editor 焦点隔离问题，已修复测试环境并重新运行核心与输入探针，保留首次失败和最终通过证据。模拟设备、事件派发和真实运行时硬件仍分开记录。

真实 DeepSeek 本轮六例覆盖明确练坐下、普通“麦粒，坐下”、真诚表扬、引用／否定表扬、回狗屋休息和手持碗时吃饭请求；返回模式、动作 ID、拒绝和表扬布尔均通过协议校验。该联调只验证真实模型输出与解析，不把协议上下文中的模拟完成声明成实际场景动作。

Windows 构建的 `bundleVersion=5.0`，公司名和产品名不变，因此没有另建一套玩家用户目录。可发布的结果汇总见 [dog1-v5-vr-results.json](dog1-v5-vr-results.json)。

**独立程序启动检查通过。** 已将新包放回原项目 `Builds/CityDog1PCVR`，分别启动十五秒，检查无图形和带图形初始化。无图形模式预期会打印不支持着色器的提示，带图形启动没有该错误；均未发现 C# 初始化异常。它们只证明启动，完整交互来自上述 Unity 场景回归。源码由 [`5.0` 标签](https://github.com/mysticaaa/COMP5140-Group7-Keep-A-Dog/tree/5.0)固定，可在 GitHub 查看实际提交。

## 图形验证截图

以下画面由 Unity 图形验证生成，不是实体头显实拍，不证明左右眼显示或佩戴舒适度。

- [VR 手账与界面](dog1-v5-headset.png)
- [伏身邀玩](dog1-v5-invite.png)
- [歪头互动](dog1-v5-head-tilt.png)

## 实机缺口与未实现功能

- 当前验证依赖 Unity 场景、真实几何射线、刚体过程及受控输入／模型；没有实体 Quest 证据时，不能认定左右眼显示、真实手柄追踪丢失恢复、房间尺度移动、触觉、佩戴舒适度或目标帧率已验收。
- 固定合成中文 WAV 只能验证这些样本的链路，不能代表真人口音、房间噪声和头显麦克风的总体识别率。真实 DeepSeek 协议联调与模型桩驱动场景的证据分开记录。
- 扔球后自动追取未实现；狗不会自主跨楼层搭电梯；搬碗位置尚未跨场景／跨存档保存。
- Windows 独立试玩包已构建并完成两次启动检查；不把构建、启动或 Editor 测试直接称为实体 PCVR 验收。

[5.0 发布说明](../V5.0.md) · [项目 README](../../README.md) · [上轮本地整合验证](dog1-main-integration.md)
