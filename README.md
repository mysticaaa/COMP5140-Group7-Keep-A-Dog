# 城市里有一只狗 · 麦粒的第一周

**3.0 · Unity 2022.3.62f3 · Dog1 VR 优先试玩**

你在城市公寓里迎来麦粒。新存档默认选用原犬种列表里的边境牧羊犬，犬种页可选择柴犬等既有犬种，已有存档读取已选择的犬种。喂食、换水、散步、清理、玩耍和休息逐步组成你们的日常。完成五章照护任务，积攒小骨头，解锁街区公园和露台小屋。也可以用中文对麦粒说话，教它记住一项动作、一串口令或一个陪伴约定。

这是课程小组项目 [COMP5140-Group7-Keep-A-Dog](https://github.com/mysticaaa/COMP5140-Group7-Keep-A-Dog) 的延续。仓库保留了最早的 `SampleScene.unity`。Dog1 现已支持桌面与 Quest Link PCVR，Dog2 保留为较轻量的 VR 交互场景。

![本版默认犬种为原有边境牧羊犬，Unity 画面](docs/assets/border-collie-preview.png)

## 3.0 更新内容（2026-11-26）

- **一个试玩程序。** Dog1 的 Windows 构建支持 Quest Link PCVR，以及无头显时的桌面测试窗口。两种方式共用照护、训练、地图进度和存档。
- **头显手账。** 侧置故事页显示当前章节、目标和进度。麦粒邀玩时可以陪它玩球、安静待一会儿，或晚点再玩。它自己歇下后，也可以轻声回应或留出空间。[头显菜单渲染截图](docs/assets/dog1-headset-menu.png)
- **边牧与动作。** 新档默认使用原有边牧，柴犬等既有犬种仍可选，老存档保留已选犬种。调整弧线转弯和站定，加入原地注视、歪头、闻嗅和理毛等自主行为。「动作」页可逐项或连续预览 29 项动作和步态。[边牧动作验证](docs/qa/dog1-default-breed.txt) · [运动学记录与截图](docs/MOTION_RECOVERY.md)
- **三场景物理玩具。** 球和棒棒糖毛绒玩具可拿起、展示、抛出，也可让麦粒叼回。公寓有小布骨、公园有软飞盘、露台有云朵布偶。右手握把优先抓取附近玩具，落空时操作正对的门、碗或其他设施。切场景会释放已抓取的玩具。[物理交互验证](docs/qa/dog1-vr-interactions.txt)
- **语音、场景回应和记忆。** 本地中文语音接入同一套 Dog1 训练系统。可选 Agent 根据玩家、玩具和手柄的位置与遮挡，从当前可执行的动作里选择。模型服务不可用时由本地规则回应。麦粒能记住经表扬确认的毛绒玩具约定，重开存档后仍可唤回。[语音与 Agent 验证](docs/qa/dog1-vr-interactions.txt)
- **第一周叙事。** 当前可玩版接入了原有五章进度与两类主动互动。新的五章事件、四条可选支线和第七晚收尾整理成一版评审稿。[第一周故事方案](docs/DOG1-FIRST-WEEK-PROPOSAL.md)

3.0 已通过 Unity 动作、场景抓取、手柄射线模拟、中文 WAV 转写、模拟模型选择与本地回退，以及第五章重载验证。Windows 试玩程序已构建并在无头显时启动。实体 Quest 手柄、头显麦克风和线上模型待实测。[头显与语音结果](docs/qa/dog1-headset-validation.txt) · [交互与存档结果](docs/qa/dog1-vr-interactions.txt)

## 试玩入口

从源码检出时，先按[构建与验证](#构建与验证)生成 Windows 试玩程序，再启动 `Builds/CityDog1PCVR/CityDog1PCVR.exe`。连上 Quest Link 后进入 VR，无可用头显时同一个程序显示电脑调试窗口。两个入口读取同一个 Dog1 场景与本地存档。

用 Unity Hub 添加仓库根目录，选择 **Unity 2022.3.62f3**，等待 Packages 导入，打开 `Assets/Scenes/Dog1.unity` 后点击 Play。`3.0` 标签固定此版源码。运行本地构建时，将可执行文件、数据目录、Unity 运行库、`SpeechService` 和模型署名留在同一目录。

**电脑测试窗口。** 左键操作手记。按住鼠标右键环顾，配合 WASD 移动。瞄准玩具按 **G** 拿起，再按 **G** 放下。按住 **V** 或手记录音按钮讲话，松开转写。Esc 可取消本轮语音，闲置时打开暂停菜单。也可在「说话」页键入。

**PCVR 操作。** 在 Windows 的 Meta Horizon Link 中设为活动 OpenXR Runtime，在 Quest 里进入 Quest Link，并在 Windows 确认头显麦克风有输入。左摇杆移动，右摇杆分段转向，手柄射线扳机松开时选中手记按钮。右手 **A** 按住讲话，松开转写。**B** 取消。在麦粒头侧缓慢移动近处手柄可触发抚摸回应。电脑窗口保留文本测试、F8 录音切换和 F9 重连。游戏在连接头显的 Windows PC 上运行。

**Dog1 头显入口。** 在 Unity 打开 `Assets/Scenes/Dog1.unity` 后点击 Play，也可以启动统一构建。左手 **X** 收起或召回侧置空间手账。首页「故事」显示当前章、目标进度，以及麦粒发起的回应选项。靠近玩具后用任一手柄握把拿起，松开可扔出；把玩具移到麦粒视野内可让它留意。空手靠近头侧缓慢移动可触发回应。手边没有玩具可抓时，看向门、食水盆、花洒或电梯控制器，按右手握把操作。电梯移动时保留头部追踪。

桌面或头显打开「动作」进入开发者模式。目录包括 11 个带标签的微动作、15 个全身与生活动作，以及慢走、弧线转弯、对角小跑三个演示。支持逐项播放和连播，头显可按类别筛选。「显示当前动作」可查看自己触发的闲时动作和探索目的。长距离取物和快跑需要场景中可用的道具和净空。

麦粒闲下来会自行歪头、侧头回望、闻嗅或伏身理毛。它会挑选当前可达的玩具、食水碗或软垫，走到附近停下观察。判断轨迹使用实际场景的活动范围与障碍。疑惑时用侧倾和立体问号表达。

**试玩场景回应。** 说「看看我」或「场景回应，你在看什么」。Dog1 从头骨位置观察玩家和道具的距离、朝向及遮挡，手柄可用时观察手与头部的距离。开启设置里的 Agent 模型并配置服务后，这些数据和本轮可用的动作标签会发给模型。游戏随后检查选中的动作与目标仍可用，再执行动作。未配置模型时，场景回应由本地规则选择。普通探索和自发微动作也由本地程序控制。

麦粒伏身邀玩后，可在故事页选「陪它玩球」「陪它安静一会儿」或「等一会儿再玩」，也可以问「你想玩球还是安静一会儿？」。本地规则根据体力、饮水和玩具状态回应；启用模型后，可在可执行的选项内作选择。它自己歇下时也可以选择轻声陪它或给它留空间。

Unity 配置通过共享的 `Dog2XrSetup.Configure` 设置 Standalone OpenXR、Touch/Touch Plus 手柄和 Single Pass Instanced，关闭自动启动，由场景在输入动作注册后启动。Dog1 使用独立的 XR 相机；无头显时不会接管桌面相机。设备稍后就绪可自动恢复，也可在设置中重连或按 F9。支持范围是 **Windows + Quest Link/Air Link 的 PCVR**，尚未提供 Quest 独立 APK。

![Dog1 空间菜单渲染验证（非头显实拍）](docs/assets/dog1-headset-menu.png)

## 第一周故事方案与道具

[第一周故事方案](docs/DOG1-FIRST-WEEK-PROPOSAL.md)供评审，包含五个不同事件、四条可选主动支线及第七晚收尾。当前预览保留五章照护存档，头显手账已接入章节目标、邀玩和休息回应。

三个场景都有可抓取球和棒棒糖毛绒玩具。公寓另有小布骨，公园有软飞盘，露台有云朵布偶。共用玩具可以让麦粒叼回，新增场景玩具可以抓、抛和展示。家具和建筑继续提供碰撞，场景设施按各自的用途操作。玩具由 Unity 原创几何构成。 [KONG 毛绒玩具参考](https://www.kongcompany.com/dog-toys/plush/) · [Petmate 软飞盘参考](https://www.petmate.com/products/chuckit-amphibious-zipflight)

## 本版动作调试

转弯沿可通行的弧线继续走。胸腰、骨盆和肩胛分担幅度，脚掌分别在身体两侧落地。停下时逐爪回到自然站位。默认边牧已独立通过弧线行走与站定检查。德牧的双向急转对照中，落脚从 49 次降到 44 次。连续弯道和急转均未测到小于 5.5 厘米的摆步，停步后四足连续支撑 33 个采样帧。双向急转的最大脚滑由 6.2 厘米增到 11.6 厘米，连续弯道最大 10.8 厘米。滑移仍需调校。[步态结果](docs/qa/dog1-curved-walk.txt)

安静时可自己转头、歪头、竖耳、轻转单耳、侧身探看、抬鼻闻、在人与玩具之间换视线、轻甩头、舔前爪、回头和贴地嗅闻。它走向一个实际可达的场景目标，途中遇到附近可见的玩家会短暂扭头，抵达后停步观察。抚摸反馈使用探头和摇尾；躯干翻滚留待验证。

开发者预览独立于训练和奖励。设置可以显示世界空间骨链，方便对照头颈、胸腰与四足接触。骨架恢复过程、论文和 Stay 参考资料见[犬类骨架恢复与动作研究](docs/MOTION_RECOVERY.md)。犬只自我理毛中有舔舐行为，本项目把它写成短时、低频、受姿态约束的动作。[犬类行为观察研究](https://www.mdpi.com/2076-2615/6/11/64)

<table>
<tr><td><img src="docs/assets/motion-recovery/puzzled-latest.png" width="430" alt="歪头与立体问号" /></td><td><img src="docs/assets/motion-recovery/groom-paw-latest.png" width="430" alt="俯身舔前爪" /></td></tr>
<tr><td><img src="docs/assets/motion-recovery/curve-latest-top.png" width="430" alt="连续弯道四足接触" /></td><td><img src="docs/assets/motion-recovery/stand-latest.png" width="430" alt="转弯后的站定姿态" /></td></tr>
</table>

`DogMotionRecoveryValidation.Run` 检查 15 种全身动作、11 种微动作、可见角度、脚掌支撑、取物、自动探索和 11 种犬种的骨架映射。[本轮动作验证](docs/qa/dog1-action-validation.txt)

## 中文语音与 Agent

**首次使用麦克风。** Windows 安装 Python 3.10–3.12。在本地构建旁的 `SpeechService` 目录运行下列命令。用 Unity 编辑器时，安装目录为 `Assets/wjy_workspace/Dog2/SpeechService`。

```powershell
.\Setup.ps1
```

首次安装下载 Python 依赖和 Whisper **small** 多语言模型。Dog1 在进入场景时预热服务，减少第一句的启动等待；也可运行 `Start.ps1` 手动启动。若 Python 不在 PATH，可使用 `-PythonPath` 指定 Python 可执行文件。默认语音地址为 `http://127.0.0.1:18765/transcribe`，程序通过健康接口校验服务标识。安装完成后可以离线转写，音频留在这台电脑。

设置页可切换并记住麦克风。桌面默认使用系统输入，只有进入实际 XR 会话后才自动优先 Quest/Oculus 麦克风；手动选择始终优先。录音按设备支持的采样率启动，显示输入音量，松开按键后保留 180ms 句尾。服务进行直流偏移去除、有限音量增益、Silero 人声检测、中文词汇提示和低置信度过滤。实现依据：[faster-whisper 官方说明](https://github.com/SYSTRAN/faster-whisper)、[转写参数](https://github.com/SYSTRAN/faster-whisper/blob/master/faster_whisper/transcribe.py)、[VAD 参数](https://github.com/SYSTRAN/faster-whisper/blob/master/faster_whisper/vad.py)。

本机实测新版 ONNX Runtime 1.31.1 在加载时发生原生访问冲突，因此将语音环境依赖固定到已验证的 **1.20.1**；`Setup.ps1` 会安装此版本，服务启动时先预热 VAD 再报告就绪。small 优先准确率，CPU 转写仍需数秒；需要较快响应时可安装 base 并在启动游戏/服务前选择它：

```powershell
.\Setup.ps1 -Model base
$env:CITYDOG_WHISPER_MODEL = 'base'
.\Start.ps1
```

默认 `auto` 优先使用已安装的 small，没有 small 时兼容原来的 base。语音结果仍需经原有 DogAgent 的动作白名单与执行结果检查，静音不会发送给 Agent。

**本地训练。** 语音转写和文本进入同一套 DogAgent。第一次给不熟悉的口令时麦粒可能犹豫，等它做成以后再表扬。试着说「晚安：坐下、抬右爪、拜一拜」，动作完成后表扬，下次只说「晚安」。也可以教它「我心情不好的时候，给我叼来棒棒糖毛绒玩具」，完成并表扬后，重开游戏再说「我心情不好」。可练习「轻扑一下」和「闻一闻」。桌面与 PCVR 在同一 Windows 用户下使用相同的口令记忆。

**可选语言模型。** 基本照护、训练、中文转写和存档无需 API Key。若需要处理更自由的措辞，可在运行时开启兼容模型，填写完整 Chat Completions 地址、模型名和该服务所需的临时凭据。本机 Ollama 可使用 `http://127.0.0.1:11434/v1/chat/completions`。训练由原有记忆与口令逻辑管理，模型可收到当前文本与有限的训练摘要。场景回应发送当前文本、已注册对象的距离和方向、视线遮挡及可用动作标签。游戏在播放前重新测量并校验，模型失败时退回本地回应。动作串最多三步。凭据只放在运行环境或当前会话。

## 构建与验证

在仓库根目录，将 `$Unity` 设为本机 **Unity 2022.3.62f3** 的 `Unity.exe`，构建统一输出 `Builds/CityDog1PCVR`。

```powershell
& $Unity -batchmode -projectPath (Get-Location).Path -buildTarget Win64 -executeMethod Dog1DemoValidation.BuildDog1PcVr -logFile dog1-pcvr-build.log
```

Unity Play 模式已覆盖步态与停步、取物、自发动作、定点探索、PCVR 空间手记、中文转写及 Agent 动作链。使用本地模拟模型响应验证了带标签的请求、收到选择后的动作播放与无效选择的拒绝。Quest 双眼显示、实体手柄、头显麦克风和线上模型仍需实测。[头显 UI 与 Agent 测试记录](docs/qa/dog1-headset-validation.txt)

Dog1 新增 `Dog1HeadsetValidation.Run` 集成验证：独立相机和桌面回退、真实几何射线按下/松开、训练调用、照料/出门/犬种入口、暂停状态下继续，以及本地中文 WAV 经 Unity HTTP 转写后进入同一个 Dog1 Agent。使用隔离的测试存档；`SpeechService/Tests~/sit.wav` 是 Windows 中文合成声音生成的固定样本，不是用户录音。输出默认在 `Logs/Dog1HeadsetValidation`，也可通过 `CITYDOG_QA_OUTPUT` 指定。

```powershell
& $Unity -batchmode -projectPath (Get-Location).Path -executeMethod Dog1HeadsetValidation.Run -logFile dog1-headset-validation.log
```

2026-11-25 合成语音回归：六句普通中文、其中一句降低到原音量的 1.5%、静音和固定随机噪声共九例。新方案识别出低音量的「麦粒，坐下」，将旧版「踏下」「抬起右招」分别修正为「趴下」「抬起右爪」，静音和噪声均返回空文本。该结果仅证明这些固定样本的改善，不代表真实口音、房间噪声或头显麦克风的总体准确率；后续仍需佩戴设备实测。

[v1.0 更新与验证](docs/V1.0.md) · [桌面玩法细节](Assets/wjy_workspace/Dog1/README.md) · [语音与 Quest Link 说明](Assets/wjy_workspace/Dog2/README.md) · [游戏策划](CITY-DOG-ROADMAP.md) · [语音与 XR 路线](CITY-DOG-XR-PLAN.md)

## 署名与资产

德牧原始骨架、FBX 和 PBR 贴图来自 **3dskillup.art / Francesco Saviano**，使用 CC BY 4.0。项目在原资产上制作了 Unity 程序化动作、姿态修正和尾部毛发层。分发包含该资产的工程或构建时请保留署名文件。 [模型来源](https://3dskillup.art/free-assets/german-shepherd/) · [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) · [仓库署名文件](Assets/wjy_workspace/Dog1/ThirdParty/ATTRIBUTION.md)
