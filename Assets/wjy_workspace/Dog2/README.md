# Dog2 Quest 3 PC VR 语音版本

平台：Windows PC，Quest 3 通过 Meta Quest Link 连接电脑。本版不是 Android APK。
打开 Assets/Scenes/Dog2.unity；其他两个场景及脚本保持原样。
全部新增脚本、UI 与 OpenXR 设置资源位于 Assets/wjy_workspace/Dog2。
OpenXR 依赖需要修改既有 Packages/manifest.json、packages-lock.json、ProjectSettings 输入设置和 EditorBuildSettings 配置引用。
输入系统为 Both，保留原有场景的旧 Input API。

## 第一次运行
1. 等待 Unity 下载 OpenXR、XR Management、Input System。输入后端变化后关闭并重新打开 Unity。
2. 电脑 Meta Horizon Link 的 Settings / General 中，设置为活动 OpenXR Runtime。
3. 戴上 Quest 3，在头显里启动 Quest Link。
4. Windows 声音设置中，把默认输入和默认通信输入都设为 Quest 麦克风，确认输入音量正常。
5. 开启 Windows 麦克风访问和允许桌面应用访问麦克风。本版本不再依赖 Windows 在线语音识别或 DictationRecognizer。
6. 如 Inspector 中缺少 XR 配置，执行 WJY / Configure Dog2 PC VR。
7. 在 WJY Dog2 VR Voice Training 组件或电脑 Game 面板填写 DeepSeek Key。model 和 URL 保留可编辑。
8. Play 后，头显显示双眼立体场景和空间状态面板。

默认 URL：https://api.deepseek.com/chat/completions
默认模型：deepseek-flash

## 按键
右手 A：第一次按下开始语音，再按一次停止。停止后自动收尾识别并发送给 DeepSeek，无需再点发送。
右手 B：取消语音，不发送。
左摇杆：平面移动。右摇杆：每次拨动转向 30 度。
F8 和 Esc 是桌面调试备用键。
电脑面板还提供文字测试，便于区分语音识别故障和 DeepSeek 请求故障。

## 语音链路（本地 Whisper）
Unity Microphone 直接录音 → 停止后上传 PCM WAV 到本机 Whisper → 返回中文 → 自动调用 DeepSeek。
不再调用 Windows DictationRecognizer，已移除导致 0x80045509 的旧听写路径。
本机语音服务默认 http://127.0.0.1:8765/transcribe，桌面面板可修改；与 DeepSeek URL 是两个独立地址。
语音服务仅监听回环地址，音频在本机识别，不向云端上传。识别文字才发送给配置的 DeepSeek 接口。
默认自动优先 Quest/Oculus 麦克风，也可从面板手动选择其他设备。面板和头显显示录音设备及输入音量。
A/F8 或语音按钮开始录音，再按一次停止、识别并自动发送；B/Esc 取消。每轮最长约 60 秒。
停止后识别，不提供逐字流式字幕；Whisper 加载和 CPU 转写需要等待。无识别文字不发送。
只需 Windows 允许桌面应用访问麦克风，不需要开启 Win+H 或旧 Windows 听写服务。
本机已安装依赖及 Whisper base 模型。开始录音时会检查并自动启动本地服务，无需另开窗口。

### 换电脑或从 GitHub 下载
先安装 Python 3.10+，在 SpeechService 目录执行：
powershell -ExecutionPolicy Bypass -File .\Setup.ps1
首次安装需要下载 Python 依赖和 Whisper base 模型；完成后转写可以离线运行。
可用 .\Start.ps1 手动启动服务；日志在 SpeechService/Logs~/service.log。
Runtime~/、Cache~/、Logs~/ 已被 Git 忽略，Unity 也不会导入这些以 ~ 结尾的目录。
Windows 打包发布时需将 SpeechService 文件夹及已安装运行环境放到 exe 同级的 SpeechService 文件夹，或配置另一个兼容服务 URL。
不要把 Runtime~/ 和模型直接提交到 GitHub。

## AI 行为
LLM 理解自然语言，从 crouch/sit/spin/refuse/idle 中选择。
idle 用于闲聊或不支持的口令，不把网络错误当成拒绝。
Dog2 默认停在固定位置；执行真实骨骼动作后等待 3 秒回到原地待命，拒绝会短暂冒烟。
保留最近 6 轮有效互动。面板显示输入、输出和状态，不记录密钥。
OpenXR 只在 Dog2 主动初始化，离开时停止自身启动的 XR Loader。
真实 Key 会随场景保存，发布源码前清空。

## 验证边界
自动化可验证项目配置、录音文本拼接、请求与动作链路、UI 资源和失败处理。
Quest 双眼显示、A/B 实物按键、Windows 语音服务和头显麦克风必须连接硬件后实际验收，不能由无头显的自动测试代替。
自动验证结果：Unity 2022.3.62f3 完成编译及 Play 模式测试。
验证覆盖：OpenXR/Quest 手柄配置、空间 Canvas、识别文本拼接、识别完成回调自动发起真实本机 HTTP 请求、合法动作执行、动作后恢复、空文本不发送、非法动作拒绝和 HTTP 401 不触发动作。
验证未调用学校 DeepSeek，未消耗 API 额度。麦克风真实识别和 Quest 立体显示/实物按键尚未通过硬件测试。
验证截图为无头显的单眼调试预览，不代表已验证 Quest 双眼效果。
## 手柄射线与 VR 面板
左右手柄有蓝色射线；指向可用按钮变绿，扳机按下并在同一按钮上松开后点击一次。
按住不会连续触发；移出按钮后松开或丢失追踪会取消点击。
VR 面板包含语音开始/停止、取消、蹲、坐、转圈、将面板移到眼前按钮。
训练口令按钮通过 DeepSeek 理解后才执行，未绕过模型直接驱动动画。
右手 A/B 仍然保留；动作、请求或配置阻止启动时会显示原因。
左右手柄追踪同时支持 Input System 和 XR CommonUsages。
语音已替换为本地 Whisper；Quest 实物麦克风、按键和双眼显示仍需实际验收。
## OpenXR 初始化修复
OpenXR 已从 1.14.3 升级为兼容 Unity 2022.3 的 1.17.1。
旧版本在 GetActiveLoaderLibraryPath 扫描时会忽略自定义资源路径，并以关闭状态的副本替换已启用手柄配置，导致 No action sets registered。
升级版本支持扫描自定义目录。配置修复会保留实际引用的资源并清理旧版本生成的未引用重复功能。
首次升级后等待导入完成，重启 Unity；进入 Quest Link 后再运行 Dog2。
测试已覆盖两次配置后再执行真实 loader-library 扫描及功能刷新，确认 Quest profiles 仍然启用。
## 固定位置模式
Dog2 场景已开启 Keep Dog Stationary，出生位置由 Dog Start Position 控制，默认世界坐标 (1.5, 0, -2)。
不再随机散步或靠近玩家；玩家移动不会带着狗移动。
蹲、坐、原地转圈和拒绝冒烟照常执行，动作结束并等待 3 秒后原地待命。
以后如需恢复移动，可在 Dog2 控制器 Inspector 取消 Keep Dog Stationary 后重新运行。
Dog1 仍默认自由散步，SampleScene 保持原样。
## 头显延迟就绪与射线恢复
XR Loader 初始化完成不代表头显会话立即进入运行状态。
Dog2 每帧跟踪实际 XRDisplaySubsystem 状态。即使头显在初次等待 8 秒后才就绪，也会自动启用手柄姿态、模型和射线。
头显休眠或 Link 暂停时隐藏射线；显示恢复后自动恢复。F9 仍可手动重新初始化。
自动化覆盖等待、晚就绪、暂停、恢复的状态转换；真实 Quest 就绪时序仍需实物验证。