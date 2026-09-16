# 帧同步确定性验证 Demo（Unity + LiteNetLib）

## 这个 demo 验证什么

不是"两个玩家最后是不是走到同一个点"，而是验证：**所有客户端在同一个 tick，对同一个玩家算出的位置和整体状态 Hash，是否逐位一致**。这才是帧同步确定性的真正判据。

架构：Host 既是服务器又是自己的客户端（listen-server）；服务器只做"收齐同一 tick 全部玩家输入后再广播"，不做权威模拟；每个客户端（含 host）收到广播后各自在本地跑 `LogicWorld.Step`，用的是纯定点数（Q16.16），不含任何 float 逻辑运算。

移动方向现在是**相机相对**的：WASD 先按本地相机 yaw 旋转，量化成 0~255 的角度索引再发出去，同步域里用写死的定点三角函数表（`FpTrig.cs`）算出实际位移，256 方向自由移动，不再是原来的 8 方向。

## 1. 导入 LiteNetLib

任选其一：
- Unity Package Manager → Add package from git URL：`https://github.com/RevenantX/LiteNetLib.git`
- 或从 [LiteNetLib Releases](https://github.com/RevenantX/LiteNetLib/releases) 下载编译好的 dll，放进 `Assets/Plugins`

广播统一用 `netManager.SendToAll(writer, deliveryMethod)`，排除某个 peer 时用重载 `SendToAll(writer, deliveryMethod, excludePeer)`。如果你的版本里 `bool`/`byte` 的读写方法名对不上（`Put(bool)` / `GetBool()`），改一下方法名即可，其余架构不受影响。

## 2. 把脚本导入项目

把 `Scripts/` 下的所有 `.cs` 文件拖进 `Assets/Scripts/FrameSyncDemo/`：

- `Fp.cs` / `FpVec2.cs` —— 定点数基础类型
- `FpTrig.cs` —— 定点三角函数查表（256份，编译期写死的常量数组）
- `LogicWorld.cs` —— 确定性模拟核心
- `NetProtocol.cs` —— 网络消息协议
- `GameNetwork.cs` —— 主网络+帧同步驱动
- `PlayerView.cs` —— 纯表现层（位置+颜色）
- `LogicCamera.cs` —— Input → LogicCamera：鼠标转本地 yaw
- `ThirdPersonCameraRig.cs` —— LogicCamera → EngineCamera：表现层跟随/平滑

## 3. 搭场景

### 基础部分（同之前）
1. 新建一个空场景，创建一个空 GameObject，命名 `NetworkManager`，挂上 `GameNetwork.cs`。
2. 创建一个 Canvas，加两个 Button（"Host"、"Join"），两个 InputField（IP，默认填 `127.0.0.1`；Port，默认填 `9050`），一个 Text（状态显示），可选一个 Panel 把这些包起来（拖到 `connectionPanel` 字段，进场后自动隐藏）。
3. 把这些 UI 对象拖到 `GameNetwork` 组件对应字段上；Host/Join 按钮的 `OnClick` 分别绑定 `OnClickHost` / `OnClickJoin`。
4. `playerViewPrefab` 可以留空（自动生成 Cube 代替）。

### 新增：相机部分
1. 场景里 `Main Camera` 保持默认位置即可（会被 `ThirdPersonCameraRig` 接管位置），在它上面挂 `ThirdPersonCameraRig.cs`。
2. 新建一个空 GameObject，命名 `LogicCamera`，挂上 `LogicCamera.cs`（这个物体本身不需要在场景里有实际位置意义，纯粹是个数据载体，放哪都行）。
3. 把 `LogicCamera` 物体拖到 `Main Camera` 上 `ThirdPersonCameraRig` 组件的 `Logic Camera` 字段。
4. 把 `LogicCamera` 物体也拖到 `NetworkManager` 上 `GameNetwork` 组件的 `Local Logic Camera` 字段。
5. 把 `Main Camera` 拖到 `GameNetwork` 组件的 `Camera Rig` 字段（这个字段类型是 `ThirdPersonCameraRig`，拖物体过去 Unity 会自动找到那个组件）。

到这一步，`GameNetwork` 在本地玩家生成时会自动把 `cameraRig.target` 指向本地玩家的 Transform（见 `SpawnPlayerView`），不需要手动连线。

### 操作方式
- 点击 Host/Join 进场后鼠标会自动锁定并隐藏（`LogicCamera.SetCursorLocked(true)`），移动鼠标转视角，WASD 按相机朝向移动。
- 按 **Esc** 解锁鼠标（方便切出去看 Console/点别的窗口）。

## 4. 多开测试

Unity 单个 Editor 进程一次只能跑一份场景，测多实例需要：
- **推荐**：用 [ParrelSync](https://github.com/VeriorPies/ParrelSync) 克隆出第二个 Editor 实例；或者
- Build 出一个 standalone 可执行文件，运行两份 `.exe`（Build Settings 里记得开 **"Run In Background"**，否则失焦的窗口会暂停 tick，导致误判"不同步"）。

测试流程：
1. 实例 A：点 **Host**（成为 Player 0）。
2. 实例 B（甚至 C）：IP 填 `127.0.0.1`，Port 填 `9050`，点 **Join**（成为 Player 1、2…）。
3. 两边分别转镜头、用 WASD 移动自己的角色（各自随便按，不需要按一样的；自己的方块和别人的方块颜色不同，方便肉眼区分谁是谁）。
4. 打开两边的 Console 窗口，逐行看形如：
   ```
   [Tick 42] Hash=-9187132612 P0=(0.6500, 0.0000) P1=(-0.3500, 0.6500)
   ```
   的日志。**对比两个实例在同一个 Tick 号下打印出的 Hash 和每个 Player 的坐标，必须完全一致**。只要出现同一 tick 号 Hash 不一样，就说明确定性被破坏了。

## 关于"只能控制自己"

逻辑上一直是这样的——每个客户端的 WASD 只会给自己的 `playerId` 生成输入，广播回来的 InputFrame 里，别人的输入只用于推进别人的方块，本地按键永远不会影响别的 playerId。之前显得"分不清谁是谁"其实是纯视觉问题，现在方块按 `playerId` 上了颜色（`GetPlayerColor`），加上镜头默认只跟着自己的角色走，应该看得清楚了。

## 关于移动速度

`moveSpeedUnitsPerSecond`（Inspector 里可调，默认 3）在 `Awake()` 时换算成 `LogicWorld.MoveSpeedPerTick`：
```
每tick位移 = moveSpeedUnitsPerSecond / tickRate
```
**这个值必须所有客户端填一样**，本质上它也是"同步契约"的一部分——两边这个数不一致，同一份输入会走出不同距离，直接破坏确定性。

## 关于之前"没有输入也在移动"的bug

原来 `SampleAndSendInput` 用了 `Mathf.Sign(Input.GetAxisRaw(...))`。Unity 的 `Mathf.Sign(0)` 返回的是 `1`，不是 `0`（这是 Unity 自己的特殊行为，跟标准 `Math.Sign` 不一样），导致哪怕没有任何按键，也会被当成"朝正方向有输入"，角色因此一直在自动漂移。现在的输入判断改成先看 `Vector2.sqrMagnitude` 是否接近 0 来决定 `moving`，彻底避开了这个陷阱，用到 `Mathf.Sign`/`Mathf.Sign`-类似的"0该不该算作有符号"的地方要格外小心。

## 关于"追帧"（第二个玩家进来能不能接上）

**能接上**——`JoinAccept` 消息里带的不只是已有玩家的 ID 列表，还带了他们"此刻的坐标快照"。新客户端收到后用这份快照初始化 `LogicWorld`，而不是把所有已有玩家摆在 (0,0)。

这本质上就是最早聊的"周期性快照"思路的一个最小实现——只不过触发时机是"有新客户端连接"，而不是"每1024帧固定存一份"。如果要支持中途断线重连，这套机制直接就能复用。

## 关于角色转向

朝向现在是 `LogicWorld` 里被同步的状态（`Facings` 字典），不是表现层自己转的：`Step()` 里玩家真正移动的那个tick，会把这次输入的 `angle` 记成朝向；停下来后保留最后一次朝向（标准TPS行为，不会瞬间转回初始方向）。`ComputeStateHash` 也把朝向纳入了，所以两端 Hash 一致就同时保证了"位置和朝向都同步对了"，不用额外肉眼比对朝向。

`PlayerView.SetLogicFacing` 只是把这个已经同步好的角度索引转成 Unity 的 Y 轴旋转并做平滑插值（`rotationSmoothSpeed`，纯视觉，随便调不影响同步）。

**Prefab 朝向要对齐**：`FpTrig` 的约定是 `angleIndex=0` 对应世界 `+Z` 方向，Unity 里 `Quaternion.Euler(0,0,0)` 的 forward 正好也是 `+Z`，两者天然一致——**前提是你的角色模型本身"面朝"的方向要摆成局部 `+Z`**（比如上一轮说的 Capsule + 一个箭头/鼻子标记子物体，那个标记要放在 `+Z` 那一侧）。如果模型建模时朝向不是 `+Z`（比如朝 `-Z` 或 `+X`），角色会一直"歪着"转，不是同步出了问题，是模型摆放和这个约定没对齐，把 Visual 子物体的本地旋转转个 90/180 度补偿一下即可。

新玩家加入时，`JoinAccept` 快照现在也带上了已有玩家的朝向，不会出现"新客户端一进来看到别人朝向都是初始默认值，直到对面下一次移动才纠正"的短暂不一致。

## 关于相机抖动

**不是所有TPC相机的通病，是tick频率(20Hz) < 渲染帧率(60fps+)导致的经典问题**：原来 `PlayerView` 每帧都是把位置直接"瞬间跳变"到最新的逻辑坐标，角色本身其实是阶梯式移动的（每隔几帧才挪一下），相机的平滑跟随在追一个会跳的目标，跳变经过相机的滞后感放大后就是抖动——业内一般叫 "Fix Your Timestep" 问题，换哪种相机、哪种跟随算法都躲不掉，因为问题根源不在相机，在"渲染的是没插值过的原始tick结果"。

现在的修法是**渲染插值**：`LogicWorld` 额外保留一份"上一个tick结束时"的位置/朝向快照（`PreviousPositions`/`PreviousFacings`），`GameNetwork.Update()` 每帧根据"距离上一次真正Step过去了多久"算出一个 `alpha`（0~1），在"上一tick"和"这一tick"的结果之间插值，喂给 `PlayerView.SetRenderState`。位置和朝向用同一个 `alpha`，保证两者视觉上不会脱节。

几点要注意：
- **这是纯表现层的处理**，`LogicWorld.Positions/Facings` 本身还是阶梯式推进的（同步逻辑完全不受影响，`ComputeStateHash` 比对的还是tick级别的精确值），插值只影响“画面上怎么显示”。
- **代价是引入了大约一个tick间隔的显示延迟**（本地看到的永远是"上一tick"到"这一tick"之间的某个中间状态，不是"当前最新tick"），20Hz下大约是50ms左右的延迟，这是业界这类插值方案普遍的取舍——用一点延迟换取画面流畅度。如果想要更跟手但接受抖动，可以把 `tickRate` 调高（比如从20提到30、60），阶梯的间隔变短，抖动本身也会减轻。
- **catch-up场景（网络卡顿后一次性补了好几个tick）没有做多阶段插值**，只是简单地"最后一次Step前 vs 最后一次Step后"两点插值，突发補帧那几个中间tick会有一次瞬间跳跃，这个简化对localhost demo够用，真实网络下如果卡顿频繁，需要更完整的插值缓冲区设计。

## 已知的简化 / 没做的部分（如果要往生产级做，这些是下一步）

- **没做输入延迟缓冲（input delay buffer）**：本地网络延迟极低，服务器收齐即广播，没有刻意插入 N 帧缓冲去掩盖真实网络延迟。真上线需要加。
- **重连流程没有完整走通**：快照机制本身已经有了，但客户端主动断线重连时如何恢复 `myPlayerId`、如何让服务器识别"这是老玩家回来了"，这部分身份识别逻辑还没做，目前每次连接都会分配一个全新的 `playerId`。
- **没做仇恨/技能这类离散事件的独立高优先级通道**：目前只有位置这一种连续状态。
- **镜头俯仰角（pitch）不影响移动方向**：`LogicCamera.PitchDegrees` 只用于摄像机视觉表现（抬头看/低头看），移动方向解析只用 yaw（水平朝向），这是常规第三人称游戏的标准做法，不是遗漏。
- **没做镜头遮挡检测（camera collision）**：镜头如果被墙体/地形挡住会直接穿模，真上线通常需要加一个 SphereCast 之类的遮挡检测把镜头拉近，这个纯属表现层细节，加在 `ThirdPersonCameraRig` 里即可，不影响任何同步逻辑。
