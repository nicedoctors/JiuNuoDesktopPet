# 双宠日常互动

## 当前交付与退出条件

2026-09-28 已采用：新增小跟屁虫、路过蹭一下、一起玩小球、一起看热闹，复用现有双窗口协调、跑步、摘帽与中断清理。糯糯跟的是伙伴而非鼠标；蹭蹭由近距离相遇触发；小球独立绘制并与踢球帧同步；看热闹只观察窗口位置变化，不读取内容或操作目标窗口。安静模式不发起这四种互动。吃图标、拖拽和主动窗口捣蛋优先。

退出条件：四个手动入口与自主触发接入；六套原图直出的角色序列完成裁切与锚点校验；后台无声验证轨迹、冷却、中断清理与素材；生成离屏预览和正式免安装包。实际桌面观感由用户测试，不自动打开应用。

仅服务本项目两位角色，由本项目维护，不新增通用行为框架。复用来源均为项目既有代码和原始素材，无新增外部运行依赖。生成图不得作为后续模型输入；选定原表与哈希沿用 pair-sprite-manifest.json。

## 已接入的触发与表现

| 互动 | 自主条件（还须通过公共忙碌/同屏/同地面检查） | 表现与冷却 |
| --- | --- | --- |
| 小跟屁虫 | 菲比普通走动且正远离糯糯，脚部水平距离为角色窗口宽度的 1.3–4 倍 | 糯糯晚 0.8 秒起步，菲比中途停一下回看；约 8.2 秒。菲比已在边缘时停下等她，不原地空走；独立冷却 65 秒 |
| 路过蹭一下 | 至少一人普通走动，距离为宽度的 0.3–1.05 倍 | 保持原左右次序，靠近、闭眼轻蹭、回位；到位后 3.6 秒，冷却 80 秒 |
| 一起玩小球 | 常规邀请到期、没人很困或很饿、场地足够、玩球冷却结束 | 摘帽出球、六次传球、收球戴帽，到位后约 20.1 秒；冷却 100 秒。玩球不可用时仍可邀请原有互动 |
| 一起看热闹 | 同屏出现新窗口，或采样间窗口位置变化累计横纵至少 64 DIP；事件在 8 秒内 | 向事件方向看，菲比先反应、糯糯晚 0.55 秒，约 4.2 秒；冷却 45 秒 |

四种共享至少 18 秒的场景间隔，上述独立冷却从开始时计算。安静模式不发起；任一角色困倦达到 70 或糯糯饥饿达到 55（非禁食/无限）时，由既有睡觉/投喂邀请优先。常规邀请间隔沿用 30–55 秒，玩球参与这一路径；跟随、相遇和看热闹每秒评估一次上下文。刚开始观察时的现存窗口不算新事件；普通窗口与当前前台的最大化窗口都可成为观察对象，但这里不会移动、最小化或截取它们。

手动入口不受自主冷却限制，仍遵守角色可用、同一显示器、同高度且有连续支撑的约束。窗台移动/关闭/最小化时结束；跨屏、尺寸变化、锁屏、退出、关闭共存、拖拽、删除任务或手动捣蛋沿用统一清理。球仅用一个透明、穿透鼠标且不抢焦点的道具窗口，清理后不残留。失败原因记录在 PairLifeUnavailable（不同高度、支撑不共用、空间不足、无可见窗口），不记录窗口标题、截图或文件内容。

## 美术与验证维护

notice、nuzzle、ball 各有两位角色的 16 帧序列，共 96 帧，源表位于 assets/pair_interactions/sheets。每套以同角色待机中性脸宽校准单一缩放率、脚底对齐，透明清理仅作确定性裁切；当前六张源表全部从原始立绘由内置 imagegen 直接生成。跑步、摘帽与戴帽复用项目选定素材。轻蹭与踢球使用专属姿势；球为本项目原生绘制道具，不是生成角色图再编辑。

小球轨迹与动作时钟由本项目的 CompanionBallTimeline 维护，仅供运行时和离屏验证使用。鞋尖标记为选定 pair_ball 第 7 帧的 512 画布坐标：糯糯 (404,392)，菲比 (100,384)；球心向外偏移一个球半径。帽口沿用 hat_open 末帧 (235,424)。角色镜像、体型和 DPI 一起转换标记，不能以窗口中心代替鞋尖。循环传球时显式重置同名动画时间轴，防止第二脚停在上一脚末帧。

后台测试涵盖跟随间距和帧率、事件有效期、五档比例/两种方向下的小球轨迹连续性、六次真实动画重启及重复取消。tools/companion_life_preview.py 使用真实运行帧生成四段无声 GIF；其中玩球读取测试程序 --companion-preview 导出的正式时间轴/真实播放器帧号，不另写模拟播放。预览位于 dist/双宠日常互动预览，不进入正式包。正式版本和发布哈希只维护于 CHARACTERS.md 的当前进度；实机验收清单见 SELF_TEST.md。

## 动作提示词

### notice_feibi

原始输入：`assets/characters/feibijiubi/original.png`

Create ONE professional 4 by 4 spritesheet containing exactly 16 sequential full-body animation poses of the supplied original character, row-major. Transparent background, no grid, labels, text, objects, companions or shadows. All cells equal size with wide transparent gutters; every hat, hair, hand and foot fully inside its own cell. Fixed orthographic camera, same chibi proportions, crisp smooth outlines and original colors. Fixed world-space foot baseline, identical neutral body size across frames; movement comes from articulated poses, never camera zoom. Frame 0 and frame 15 neutral standing with two visible feet. Playful, cute and expressive but anatomically consistent, no creepy stretched faces. High resolution square sheet.
Feibijiubi: energetic adorable blonde, original giant white hat with black/blue band ALWAYS on head, purple glossy eyes, blue cross clip, white/black/blue outfit, black gloves, short striped socks and small black shoes. Main facing three-quarter towards screen LEFT. Preserve original identity.
16-pose noticing animation: 0 neutral; 1-3 head perks up and eyes become attentive; 4-6 turn gaze sideways toward companion while hand lifts a little; 7-9 rise onto toes and lean forward to peek; 10-12 small delighted response with hands near chest; 13-15 settle gently back to neutral. Standing, head upright, subtle bounce not size changes.

### notice_nuonuo

原始输入：`assets/characters/nuonuo/original.png`

Create ONE professional 4 by 4 spritesheet containing exactly 16 sequential full-body animation poses of the supplied original character, row-major. Transparent background, no grid, labels, text, objects, companions or shadows. All cells equal size with wide transparent gutters; every hat, hair, hand and foot fully inside its own cell. Fixed orthographic camera, same chibi proportions, crisp smooth outlines and original colors. Fixed world-space foot baseline, identical neutral body size across frames; movement comes from articulated poses, never camera zoom. Frame 0 and frame 15 neutral standing with two visible feet. Playful, cute and expressive but anatomically consistent, no creepy stretched faces. High resolution square sheet.
Nuonuo: soft sleepy cute grey-blue long-haired mochi girl, half-lidded FLAT eyes split pink above pale blue below, NO pupils or dark circles, original red/white outfit and red bow, small body and two little black shoes below red skirt. Complete original upper-body reference to full-body chibi. Main facing slightly towards screen RIGHT. Preserve flat original eye design.
16-pose noticing animation: 0 neutral; 1-3 head perks up and eyes become attentive; 4-6 turn gaze sideways toward companion while hand lifts a little; 7-9 rise onto toes and lean forward to peek; 10-12 small delighted response with hands near chest; 13-15 settle gently back to neutral. Standing, head upright, subtle bounce not size changes.

### nuzzle_feibi

原始输入：`assets/characters/feibijiubi/original.png`

Create ONE professional 4 by 4 spritesheet containing exactly 16 sequential full-body animation poses of the supplied original character, row-major. Transparent background, no grid, labels, text, objects, companions or shadows. All cells equal size with wide transparent gutters; every hat, hair, hand and foot fully inside its own cell. Fixed orthographic camera, same chibi proportions, crisp smooth outlines and original colors. Fixed world-space foot baseline, identical neutral body size across frames; movement comes from articulated poses, never camera zoom. Frame 0 and frame 15 neutral standing with two visible feet. Playful, cute and expressive but anatomically consistent, no creepy stretched faces. High resolution square sheet.
Feibijiubi: energetic adorable blonde, original giant white hat with black/blue band ALWAYS on head, purple glossy eyes, blue cross clip, white/black/blue outfit, black gloves, short striped socks and small black shoes. Main facing three-quarter towards screen LEFT. Preserve original identity.
16-pose affectionate shoulder/cheek nuzzle: 0 neutral; 1-3 tiny side step closer with shy smile; 4-6 lean shoulder and cheek toward partner offscreen; 7-9 eyes close and cheek presses softly, hands tucked; 10-12 two tender side-to-side rubbing poses; 13-15 spring gently upright. NO partner drawn. No distorted mouth or giant hands. Lean LEFT for Feibi, RIGHT for Nuonuo.

### nuzzle_nuonuo

原始输入：`assets/characters/nuonuo/original.png`

Create ONE professional 4 by 4 spritesheet containing exactly 16 sequential full-body animation poses of the supplied original character, row-major. Transparent background, no grid, labels, text, objects, companions or shadows. All cells equal size with wide transparent gutters; every hat, hair, hand and foot fully inside its own cell. Fixed orthographic camera, same chibi proportions, crisp smooth outlines and original colors. Fixed world-space foot baseline, identical neutral body size across frames; movement comes from articulated poses, never camera zoom. Frame 0 and frame 15 neutral standing with two visible feet. Playful, cute and expressive but anatomically consistent, no creepy stretched faces. High resolution square sheet.
Nuonuo: soft sleepy cute grey-blue long-haired mochi girl, half-lidded FLAT eyes split pink above pale blue below, NO pupils or dark circles, original red/white outfit and red bow, small body and two little black shoes below red skirt. Complete original upper-body reference to full-body chibi. Main facing slightly towards screen RIGHT. Preserve flat original eye design.
16-pose affectionate shoulder/cheek nuzzle: 0 neutral; 1-3 tiny side step closer with shy smile; 4-6 lean shoulder and cheek toward partner offscreen; 7-9 eyes close and cheek presses softly, hands tucked; 10-12 two tender side-to-side rubbing poses; 13-15 spring gently upright. NO partner drawn. No distorted mouth or giant hands. Lean LEFT for Feibi, RIGHT for Nuonuo.

### ball_feibi

原始输入：`assets/characters/feibijiubi/original.png`

Create ONE professional 4 by 4 spritesheet containing exactly 16 sequential full-body animation poses of the supplied original character, row-major. Transparent background, no grid, labels, text, objects, companions or shadows. All cells equal size with wide transparent gutters; every hat, hair, hand and foot fully inside its own cell. Fixed orthographic camera, same chibi proportions, crisp smooth outlines and original colors. Fixed world-space foot baseline, identical neutral body size across frames; movement comes from articulated poses, never camera zoom. Frame 0 and frame 15 neutral standing with two visible feet. Playful, cute and expressive but anatomically consistent, no creepy stretched faces. High resolution square sheet.
Feibijiubi: energetic adorable blonde, original giant white hat with black/blue band ALWAYS on head, purple glossy eyes, blue cross clip, white/black/blue outfit, black gloves, short striped socks and small black shoes. Main facing three-quarter towards screen LEFT. Preserve original identity.
16-pose playing with a SMALL BALL THAT IS NOT DRAWN: 0 neutral; 1-3 notice ball approaching feet and crouch slightly; 4-6 lift forward foot; 7-8 gently kick with crisp visible foot contact pose; 9-11 follow through with arms balancing, delighted eyes; 12-15 recoil into soft bounce then neutral. Feet stay low, grounded body, not running. NO ball or effects. Kick LEFT for Feibi, RIGHT for Nuonuo.

### ball_nuonuo

原始输入：`assets/characters/nuonuo/original.png`

Create ONE professional 4 by 4 spritesheet containing exactly 16 sequential full-body animation poses of the supplied original character, row-major. Transparent background, no grid, labels, text, objects, companions or shadows. All cells equal size with wide transparent gutters; every hat, hair, hand and foot fully inside its own cell. Fixed orthographic camera, same chibi proportions, crisp smooth outlines and original colors. Fixed world-space foot baseline, identical neutral body size across frames; movement comes from articulated poses, never camera zoom. Frame 0 and frame 15 neutral standing with two visible feet. Playful, cute and expressive but anatomically consistent, no creepy stretched faces. High resolution square sheet.
Nuonuo: soft sleepy cute grey-blue long-haired mochi girl, half-lidded FLAT eyes split pink above pale blue below, NO pupils or dark circles, original red/white outfit and red bow, small body and two little black shoes below red skirt. Complete original upper-body reference to full-body chibi. Main facing slightly towards screen RIGHT. Preserve flat original eye design.
16-pose playing with a SMALL BALL THAT IS NOT DRAWN: 0 neutral; 1-3 notice ball approaching feet and crouch slightly; 4-6 lift forward foot; 7-8 gently kick with crisp visible foot contact pose; 9-11 follow through with arms balancing, delighted eyes; 12-15 recoil into soft bounce then neutral. Feet stay low, grounded body, not running. NO ball or effects. Kick LEFT for Feibi, RIGHT for Nuonuo.
