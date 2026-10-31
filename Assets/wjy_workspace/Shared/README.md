# DeepSeek connection presets

SampleScene、Dog1、Dog2 共用同一套连接面板，运行面板和 Inspector 均提供下拉预设及手动编辑。
URL 下拉选项只有两项：
- https://genai.comp.polyu.edu.hk/api/v1//chat/completions 自动匹配 DeepSeek-V4-Flash
- https://api.deepseek.com/chat/completions 自动匹配 deepseek-flash
Model 下拉选项也只有上述两个模型名。
切换 URL 预设，或手动编辑 URL 为上述已知地址时，自动匹配模型。
之后可独立选择/手动编辑 Model，程序不会每帧覆盖手动值。
自定义 URL 保留手动模型。API Key 不会因为选择预设而修改。
面板可滚动，避免展开下拉列表后遮住训练或语音按钮。
运行中输入仅在本次 Play 生效。非运行时在 Inspector 修改后保存场景可持久保存。
真实 Key 不要提交到公开仓库。