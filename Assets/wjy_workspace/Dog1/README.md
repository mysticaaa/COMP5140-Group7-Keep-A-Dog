# Dog1 写实德牧互动版本

打开 Assets/Scenes/Dog1.unity 后运行。原 SampleScene 和 DogTrainingPrototype.cs 保持不变。
新配置位于 WJY Dog1 Training 的 Dog1Training 组件；面板也支持填写 API Key、model、URL。
Key 可能保存在场景中，公开提交前清空。

德牧在用户（Main Camera）附近自由走动，定时主动靠近。
按住鼠标右键环顾，右键 + WASD 移动。它也可能走到身后，请转身观察。
选择蹲、坐、转圈后等待真实 LLM 返回，合法 action 才触发动画。
坐/蹲完成后保持姿态 3 秒；转圈完成后停 3 秒，再恢复漫步。
refuse 会摇头、头顶短暂冒烟，结束后等待 3 秒恢复漫步。
网络失败或非法回复不会冒烟，也不会伪装成拒绝。
执行期间禁用新命令，避免动作重叠。

动作由程序驱动实际骨骼，不是球体变色或整只狗缩放；真实感受限于当前程序化步态。
模型来自 3dskillup.art，CC BY 4.0，署名见 ThirdParty/ATTRIBUTION.md。
源模型没有现成训练动画；后续可替换为针对同一骨架制作的动画片段。
当前是平坦空场地移动，无障碍物导航；加入家具后需要接入 NavMesh 或其他避障。
全部新增资源和脚本位于 Assets/wjy_workspace/Dog1。
## 验证结果
通过独立 Unity 2022.3.62f3 项目的 Play 模式自动验证：资源加载、自主位移、坐/蹲/转圈/拒绝四种动作、冒烟粒子、动作后恢复、合法与非法 LLM 动作解析。
已渲染并检查动作截图，Preview.png 为实际运行的拒绝冒烟预览。
本次验证注入结构化动作，不调用真实 LLM，不消耗 API 额度；真实模型调用沿用已跑通的请求逻辑，需自行配置 Key/model/URL。
SampleScene 修改前后 SHA-256 一致：1A21989B2677267887FC8EB13AE87BAF5EBBD706F51CD7CF30345279801FA113。