# 帧同步确定性验证 Demo（Unity + LiteNetLib）

## 这个 demo 验证什么

不是"两个玩家最后是不是走到同一个点"，而是验证：**所有客户端在同一个 tick，对同一个玩家算出的位置和整体状态 Hash，是否逐位一致**。这才是帧同步确定性的真正判据。

架构：Host 既是服务器又是自己的客户端（listen-server）；服务器只做"收齐同一 tick 全部玩家输入后再广播"，不做权威模拟；每个客户端（含 host）收到广播后各自在本地跑 `LogicWorld.Step`，用的是纯定点数（Q16.16），不含任何 float 逻辑运算。

## 1. 导入 LiteNetLib

任选其一：
- Unity Package Manager → Add package from git URL：`https://github.com/RevenantX/LiteNetLib.git`
- 或从 [LiteNetLib Releases](https://github.com/RevenantX/LiteNetLib/releases) 下载编译好的 dll，放进 `Assets/Plugins`

（不确定你说的"LiteNetLib2"具体指哪个版本/分支，如果和这里默认版本 API 对不上，主要留意 `NetDataWriter/NetDataReader` 的 `sbyte` 读写方法名是否是 `Put(sbyte)` / `GetSByte()`，不同版本可能略有差异，改一下方法名即可，其余架构不受影响。）

## 2. 把脚本导入项目

把 `Scripts/` 下的 5 个 `.cs` 文件拖进 `Assets/Scripts/FrameSyncDemo/`（或任意路径，命名空间已经隔离好了）。

## 3. 搭场景

1. 新建一个空场景，创建一个空 GameObject，命名 `NetworkManager`，挂上 `GameNetwork.cs`。
2. 创建一个 Canvas，加两个 Button（"Host"、"Join"），两个 InputField（IP，默认填 `127.0.0.1`；Port，默认填 `9050`），一个 Text（状态显示）。
3. 把这些 UI 对象拖到 `GameNetwork` 组件的 `ipInputField` / `portInputField` / `statusText` 字段上。
4. 给 Host 按钮的 `OnClick` 绑定 `GameNetwork.OnClickHost`，Join 按钮绑定 `GameNetwork.OnClickJoin`。
5. `playerViewPrefab` 可以留空（会自动生成一个 Cube 代替），也可以自己做个 prefab 挂上 `PlayerView.cs`。

## 4. 多开测试

Unity 单个 Editor 进程一次只能跑一份场景，测多实例需要：
- **推荐**：用 [ParrelSync](https://github.com/VeriorPies/ParrelSync) 克隆出第二个 Editor 实例；或者
- Build 出一个 standalone 可执行文件，运行两份 `.exe`（Build Settings 里记得开 **"Run In Background"**，否则失焦的窗口会暂停 tick，导致误判"不同步"）。

测试流程：
1. 实例 A：点 **Host**（成为 Player 0）。
2. 实例 B（甚至 C）：IP 填 `127.0.0.1`，Port 填 `9050`，点 **Join**（成为 Player 1、2…）。
3. 两边分别用 WASD 移动自己的角色（各自随便按，不需要按一样的）。
4. 打开两边的 Console 窗口，逐行看形如：
   ```
   [Tick 42] Hash=-9187132612 P0=(0.6500) , (0.0000) P1=(-0.3500, 0.6500)
   ```
   的日志。**对比两个实例在同一个 Tick 号下打印出的 Hash 和每个 Player 的坐标，必须完全一致**。只要出现同一 tick 号 Hash 不一样，就说明确定性被破坏了（要去查是不是哪里手滑用了 float、或者字典遍历顺序没锁死之类的问题）。

## 已知的简化 / 没做的部分（如果要往生产级做，这些是下一步）

- **没做输入延迟缓冲（input delay buffer）**：本地网络延迟极低，服务器收齐即广播，没有刻意插入 N 帧缓冲去掩盖真实网络延迟。真上线需要加。
- **没做真正的重连/快照恢复**：这个 demo 只处理"正常加入"，断线重连、以及你之前提到的"每 1024 帧存快照"都还没实现，是自然的下一步扩展点。
- **没做仇恨/技能这类离散事件的独立高优先级通道**：目前只有位置这一种连续状态，事件通道的设计留给你按需扩展。
- **没有相机相关系统（LogicCamera）**：目前是直接用世界坐标系下的 WASD 方向移动，不涉及"镜头朝向影响移动方向"的解析，这是刻意简化，方便你先验证最基础的确定性链路，跑通以后再叠加 LogicCamera 这层。
