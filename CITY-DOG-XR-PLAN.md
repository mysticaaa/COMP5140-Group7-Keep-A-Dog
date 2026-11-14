# 《城市里有一只狗》语音与 XR 三阶段计划

## 第一阶段 · 先能自然地和麦粒说话

桌面继续保留五章养犬生活。把主界面改成暖色养犬手记，收起手记时仍能看到狗和字幕。键盘按住 V 讲话，松开转写，保留鼠标语音按钮和键入。语音与文字统一走 DogAgent，先用本机 Whisper 转写，再用现有本地规则理解已知表达；开启兼容模型后，模糊表达可由模型结合训练摘要判定。Unity 负责动作白名单、两到三步动作链和成功后的奖励写入。

验收包括已知口令、模糊表达、语音取消、无文本、服务未就绪、有边界的动作链，及界面在不同窗口下能读。可选模型应单独验证真实服务。

## 第二阶段 · Quest Link 做一段完整的相处

在现有 Dog2 和 OpenXR 手柄绑定上，先做一间可站可坐的公寓。右手 A 按住讲话、松开转写，B 取消；保留 F8 和桌面文字入口用于调试。观察字幕和麦粒的转头、靠近、抬爪、取物或回应。用空间手记反馈和表扬，通过近距离移动手柄抚摸，空间面板可重新摆到眼前。移动保留左摇杆和 30° 分段转向。先验证双眼、A/B、动作对时、近距离交互，再把完整照护和地点流程迁入头显。

Quest Link 和 Windows 共用 PC 上的转写服务。实际验收要戴上头显跑完「呼唤 → 表扬 → 教一条约定 → 再说一次」并检查麦克风、手柄追踪、休眠恢复、坐姿可达和眩晕。

## 第三阶段 · 一体机与发布质量

根据实际头显型号补 Android OpenXR 设置、ARM64 构建和麦克风运行时权限。Quest 一体机的回环地址指向头显自身，需要单独选定离线转写方案，或用授权的 HTTPS 语音服务，并测延迟、资源和隐私。补传送、舒适性选项、几种手大小和可达范围、低配性能、图形与音效、长期存档与多场景。做实机试玩后，再决定 Steam PCVR 和 Quest 独立发布范围。

## 参考

- Hello, Dot 用实体操作件切换模式，并把麦克风输入显示成文字；取其明确的录音反馈和近手交互。 [Niantic 操作说明](https://nianticspatial.helpshift.com/hc/en/4-peridot/faq/878-how-to-play-hello-dot/)
- 移动 AR 版 Peridot 已公开用模型选择动画库里的表现，适合作为「语义决定、骨架受约束」的参考。 [Meta 技术案例](https://ai.meta.com/blog/niantic-peridot-llama/)
- Stay: Forever Home 用宠物的个性和近距离互动建立陪伴感；Pets & Stuff 把照护做成可拿取的道具。 [Stay 官网](https://stay-forever-home.com/) · [Pets & Stuff 官方页面](https://www.meta.com/experiences/pets-stuff/7818625474831202/)
- 手记配色采用暖米纸、松绿墨、琥珀录音状态。画面保留麦粒周围的空间，主要语音反馈放在字幕条。 [Meta 空间界面指南](https://developers.meta.com/vr/design/mr-design-guideline/) · [Unity OpenXR Quest 支持](https://docs.unity.cn/Packages/com.unity.xr.openxr@1.17/manual/features/metaquest.html) · [Unity Android 权限](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Android.Permission.html)
