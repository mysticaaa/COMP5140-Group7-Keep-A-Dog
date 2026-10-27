# AI 小狗训练初始原型

使用 Unity 2022.3.62f3 打开 Assets/Scenes/SampleScene.unity，点击 Play。
面板输入 API Key 和 model（默认留空），点击“蹲 / 坐 / 转圈”即发送真实 DeepSeek 请求。
配置均位于场景对象 WJY Dog Training Prototype 的 Inspector 中：apiKey、model、endpoint。
在非 Play 模式下填入 Inspector 并保存场景，可持久保存配置。运行面板的临时输入在停止 Play 后不保留。真实 Key 会随场景保存，公开 GitHub 前必须清空。
model 也可以在场景 WJY Dog Training Prototype 组件上配置，它不是秘密。

颜色：初始白；crouch 红；sit 黄；spin 绿；refuse 黑。
只有合法模型回复才改变颜色；缺配置、网络错误、HTTP 错误、空内容及非法动作均保留原色并显示提示。
按钮等待响应期间禁用，超时 60 秒。保留最近 6 轮有效对话，重置按钮清除记录并恢复白色。
本版使用屏幕 OnGUI 面板和颜色演示，不包含真实动作动画、VR 或语音。

API：POST https://api.deepseek.com/chat/completions
使用 response_format=json_object，要求返回 action 和 reason。
点击口令生成提示词：你现在是小狗的身份，我要训练你（所选口令）。
代码严格校验 action 只能是本轮请求动作或 refuse，不通过关键词匹配猜测结果。

此版本按要求支持在场景中配置 Key（初始为空），仅用于本机开发；不要把真实 Key 提交 GitHub。
公开分发时应改为后端代理，客户端不携带服务密钥。

维护：WJY > Build Dog Training Prototype 可重建占位球及场景配置（会保存 SampleScene）。
Editor/WjyPrototypeSetup.cs 同时包含有效/拒绝/异常响应的解析校验。
运行面板支持直接修改 URL（endpoint），填写完整的 Chat Completions 接口地址，例如 https://api.deepseek.com/chat/completions。不会自动补全路径。其他地址须兼容当前 Chat Completions 请求/响应格式和 JSON 输出参数。运行中输入的配置在停止 Play 后不保留；需持久保存时在非运行状态下通过 Inspector 修改并保存场景。
