using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace FrameSyncDemo
{
    /// <summary>
    /// 挂在场景里唯一一个 "NetworkManager" GameObject 上。
    ///
    /// 架构：Host 既是服务器又是自己的客户端（listen-server），Join 的实例只是纯客户端。
    /// 服务器的职责只是"收齐同一 tick 所有玩家的输入后再广播"，本身不做权威模拟——
    /// 每个客户端（包括 host）收到广播后，各自在本地用完全相同的 LogicWorld.Step 推进，
    /// 这就是要验证的核心：只要输入集合和顺序相同，各端算出来的位置和 Hash 必须一致。
    /// </summary>
    public class GameNetwork : MonoBehaviour, INetEventListener
    {
        [Header("UI（在 Inspector 里把场景里的按钮/输入框拖进来）")]
        public InputField ipInputField;
        public InputField portInputField;
        public Text statusText;
        public GameObject connectionPanel;

        [Header("Simulation")]
        public GameObject playerViewPrefab; // 留空则用默认 Cube
        public int tickRate = 20;
        [Tooltip("每秒移动多少 units，换算成 Fp 后写进 LogicWorld.MoveSpeedPerTick。所有客户端必须填一样的值。")]
        public float moveSpeedUnitsPerSecond = 3f;

        [Header("Camera（Input → LogicCamera → EngineCamera）")]
        [Tooltip("场景里的 LogicCamera 组件：只负责把鼠标转成本地 yaw，读它来解析移动方向")]
        public LogicCamera localLogicCamera;
        [Tooltip("场景里挂在 Main Camera 上的表现层跟随脚本，只做视觉平滑，不影响逻辑")]
        public ThirdPersonCameraRig cameraRig;

        private float tickInterval;
        private float tickAccumulator;

        // 距离"上一次世界真正推进了一个tick"过去了多久（渲染时间），用来算插值alpha。
        // 每次 world.Step 被调用后归零；Update() 里持续累加。
        private float timeSinceLastStep;

        private const string ConnectionKey = "framesync-demo";
        private const int DefaultPort = 9050;

        private NetManager netManager;
        private bool isServer;
        private bool isClient;

        // ---- 客户端侧状态 ----
        private int myPlayerId = -1;
        private int myNextInputTick = 0;
        private readonly LogicWorld world = new LogicWorld();
        private int localSimTick = 0;
        private readonly Dictionary<int, List<(int playerId, byte angle, bool moving)>> receivedFrames = new Dictionary<int, List<(int, byte, bool)>>();
        private readonly Dictionary<int, PlayerView> playerViews = new Dictionary<int, PlayerView>();
        private readonly Dictionary<int, Color32> playerColors = new Dictionary<int, Color32>();

        // ---- 服务器侧状态（只有 isServer==true 时才会被用到）----
        private readonly Dictionary<int, NetPeer> peersByPlayerId = new Dictionary<int, NetPeer>();
        private readonly Dictionary<NetPeer, int> playerIdByPeer = new Dictionary<NetPeer, int>();
        private readonly HashSet<int> activePlayerIds = new HashSet<int>();
        private readonly Dictionary<int, Dictionary<int, (byte angle, bool moving)>> pendingByTick = new Dictionary<int, Dictionary<int, (byte, bool)>>();
        private int nextTickToCollect = 0;
        private int nextPlayerId = 0;

        void Awake()
        {
            tickInterval = 1f / tickRate;
            netManager = new NetManager(this) { AutoRecycle = true };

            // 一次性配置转换：units/秒 → 每tick的Fp增量。这是允许的 FromFloatDebugOnly
            // 用法（读配置，不是逐帧运行时换算），但要确保所有客户端这个数填的一样，
            // 否则大家的"同一份输入"会推导出不同的移动距离，直接破坏确定性。
            world.MoveSpeedPerTick = Fp.FromFloatDebugOnly(moveSpeedUnitsPerSecond / tickRate);
        }

        void Update()
        {
            netManager?.PollEvents();

            if (Input.GetKeyDown(KeyCode.Escape))
                localLogicCamera?.SetCursorLocked(false);

            if (isClient)
            {
                tickAccumulator += Time.deltaTime;
                while (tickAccumulator >= tickInterval)
                {
                    tickAccumulator -= tickInterval;
                    SampleAndSendInput();
                }
            }

            timeSinceLastStep += Time.deltaTime;
            // alpha=0 表示"刚推进完上一个tick"，alpha=1表示"马上要到下一个tick了"，
            // 在这两个状态之间做插值，画面上就是连续滑动，而不是逻辑tick那种阶梯跳变。
            float alpha = tickInterval > 0f ? Mathf.Clamp01(timeSinceLastStep / tickInterval) : 1f;

            foreach (var kv in playerViews)
            {
                var currPos = world.Positions.TryGetValue(kv.Key, out var cp) ? cp : FpVec2.Zero;
                var prevPos = world.PreviousPositions.TryGetValue(kv.Key, out var pp) ? pp : currPos;
                var currFacing = world.Facings.TryGetValue(kv.Key, out var cf) ? cf : (byte)0;
                var prevFacing = world.PreviousFacings.TryGetValue(kv.Key, out var pf) ? pf : currFacing;

                // 只在这一步（渲染层的最后一环）才把定点数转成float，插值本身也只服务于显示。
                Vector3 prevV = new Vector3(prevPos.X.ToFloatDebugOnly(), 0f, prevPos.Y.ToFloatDebugOnly());
                Vector3 currV = new Vector3(currPos.X.ToFloatDebugOnly(), 0f, currPos.Y.ToFloatDebugOnly());
                Vector3 renderPos = Vector3.Lerp(prevV, currV, alpha);

                float prevDeg = prevFacing * (360f / FpTrig.STEPS);
                float currDeg = currFacing * (360f / FpTrig.STEPS);
                float yaw = Mathf.LerpAngle(prevDeg, currDeg, alpha); // LerpAngle正确处理360度环绕

                kv.Value.SetRenderState(renderPos, yaw);
            }
        }

        void OnDestroy()
        {
            netManager?.Stop();
        }

        // ============ UI 按钮回调 ============

        public void OnClickHost()
        {
            int port = ParsePortOrDefault();
            isServer = true;
            isClient = true;
            netManager.Start(port);

            myPlayerId = nextPlayerId++;
            activePlayerIds.Add(myPlayerId);
            playerColors[myPlayerId] = GetPlayerColor(myPlayerId);
            world.AddPlayer(myPlayerId);
            SpawnPlayerView(myPlayerId);
            SetStatus($"Hosting on port {port} as Player {myPlayerId}");
            CloseConnectionPanel();
            localLogicCamera?.SetCursorLocked(true);
        }

        public void OnClickJoin()
        {
            string ip = ipInputField != null && !string.IsNullOrEmpty(ipInputField.text) ? ipInputField.text : "127.0.0.1";
            int port = ParsePortOrDefault();
            isServer = false;
            isClient = true;
            netManager.Start();
            netManager.Connect(ip, port, ConnectionKey);
            SetStatus($"Connecting to {ip}:{port} ...");
        }

        private int ParsePortOrDefault()
        {
            if (portInputField != null && int.TryParse(portInputField.text, out var p)) return p;
            return DefaultPort;
        }

        private void SetStatus(string s)
        {
            if (statusText != null) statusText.text = s;
            Debug.Log("[Net] " + s);
        }

        // ============ 输入采样（客户端）============

        private void SampleAndSendInput()
        {
            // 注意：这里直接用 sqrMagnitude 判断"有没有输入"，不走 Mathf.Sign 那条路——
            // 之前那个bug就是 Mathf.Sign(0) 在 Unity 里返回 1 而不是 0 造成的。
            Vector2 raw = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            bool moving = raw.sqrMagnitude > 0.0001f;
            byte angleIndex = 0;

            if (moving)
            {
                raw.Normalize();
                // Input → LogicCamera → 这里：读取本地相机的 yaw（float，纯本地，不需要跨端一致），
                // 把 WASD 的"相对方向"转成"世界方向"。这一步用 float 完全没问题，因为它发生在
                // "进入同步域"之前——真正被广播、被所有端一致处理的，是下面量化出来的 angleIndex。
                float camYaw = localLogicCamera != null ? localLogicCamera.YawRadians : 0f;
                // atan2(x, y) 以 (0,1) 为 0 弧度基准，和 FpTrig.DirFromAngle / ThirdPersonCameraRig
                // 里"yaw=0朝+Z"的约定保持一致。
                float worldAngle = Mathf.Atan2(raw.x, raw.y) + camYaw;
                angleIndex = FpTrig.QuantizeAngle(worldAngle);
            }

            int tick = myNextInputTick++;

            if (isServer)
            {
                // host 同时是自己的客户端：不走真实 socket，直接喂给服务器聚合逻辑。
                ServerReceiveInput(myPlayerId, tick, angleIndex, moving);
            }
            else
            {
                var writer = new NetDataWriter();
                writer.WritePlayerInput(tick, angleIndex, moving);
                netManager.FirstPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        // ============ 服务器侧聚合 ============

        private static Color32 GetPlayerColor(int playerId)
        {
            // 颜色由服务器按 playerId 决定，所有客户端收到同一份 RGBA。
            // 这里只用于表现层，不进入确定性模拟。
            switch (playerId % 6)
            {
                case 0: return new Color32(60, 170, 255, 255);
                case 1: return new Color32(255, 90, 90, 255);
                case 2: return new Color32(90, 220, 120, 255);
                case 3: return new Color32(255, 190, 60, 255);
                case 4: return new Color32(190, 100, 255, 255);
                default: return new Color32(60, 230, 210, 255);
            }
        }

        private void CloseConnectionPanel()
        {
            if (connectionPanel != null)
                connectionPanel.SetActive(false);
        }

        private void ServerReceiveInput(int playerId, int tick, byte angle, bool moving)
        {
            if (!pendingByTick.TryGetValue(tick, out var dict))
            {
                dict = new Dictionary<int, (byte, bool)>();
                pendingByTick[tick] = dict;
            }
            dict[playerId] = (angle, moving);

            TryFlushTicks();
        }

        private void TryFlushTicks()
        {
            // 必须严格按 tick 顺序 flush，且要求当前活跃玩家的输入全部到齐，
            // 这就是"锁步"（lockstep）——任何一端输入没到，大家都等着，不会有人抢跑。
            while (pendingByTick.TryGetValue(nextTickToCollect, out var dict)
                   && activePlayerIds.All(id => dict.ContainsKey(id)))
            {
                var entries = dict.Select(kv => (kv.Key, kv.Value.Item1, kv.Value.Item2)).ToList();
                BroadcastInputFrame(nextTickToCollect, entries);
                pendingByTick.Remove(nextTickToCollect);
                nextTickToCollect++;
            }
        }

        private void BroadcastInputFrame(int tick, List<(int playerId, byte angle, bool moving)> entries)
        {
            var writer = new NetDataWriter();
            writer.WriteInputFrame(tick, entries);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

            // host 自己不会给自己发 socket 包，直接本地应用。
            ApplyInputFrame(tick, entries);
        }

        // ============ 客户端侧：应用一致的输入帧，推进模拟 ============

        private void ApplyInputFrame(int tick, List<(int playerId, byte angle, bool moving)> entries)
        {
            receivedFrames[tick] = entries;

            // 可能一次性收到多个连续 tick（比如刚连上时补发的），循环把能推进的都推进掉。
            while (receivedFrames.TryGetValue(localSimTick, out var frame))
            {
                var inputsByPlayer = frame.ToDictionary(e => e.playerId, e => (e.angle, e.moving));
                world.Step(inputsByPlayer);
                timeSinceLastStep = 0f;

                long hash = world.ComputeStateHash();
                string posStr = string.Join(", ", world.Positions.OrderBy(p => p.Key)
                    .Select(p => $"P{p.Key}=({p.Value.X},{p.Value.Y})"));
                Debug.Log($"[Tick {localSimTick}] Hash={hash} {posStr}");

                receivedFrames.Remove(localSimTick);
                localSimTick++;
            }
        }

        private void SpawnPlayerView(int playerId)
        {
            if (playerViews.ContainsKey(playerId)) return;

            GameObject go;
            if (playerViewPrefab != null)
                go = Instantiate(playerViewPrefab);
            else
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);

            var view = go.GetComponent<PlayerView>();
            if (view == null) view = go.AddComponent<PlayerView>();
            playerViews[playerId] = view;

            if (!playerColors.TryGetValue(playerId, out var color))
                color = GetPlayerColor(playerId);
            view.SetColor(color);

            // 只有"这是我自己的角色"这一件事需要摄像机知道，别人的角色跟摄像机无关。
            if (playerId == myPlayerId && cameraRig != null)
                cameraRig.target = go.transform;
        }

        // ============ LiteNetLib 回调 ============

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (isServer) request.AcceptIfKey(ConnectionKey);
            else request.Reject();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            if (!isServer) return;

            int newPlayerId = nextPlayerId++;
            peersByPlayerId[newPlayerId] = peer;
            playerIdByPeer[peer] = newPlayerId;

            // 关键：不只是ID列表，要把已有玩家"此刻的坐标"一起发过去，
            // 这样新客户端才能接上现有状态，而不是把所有已有玩家初始化到(0,0)。
            var existingSnapshot = activePlayerIds
                .Select(id => (id, world.Positions[id].X.Raw, world.Positions[id].Y.Raw, world.Facings[id], playerColors[id]))
                .ToList();

            // 简化处理：新玩家从"当前正在收集的 tick"开始参与要求，
            // 不需要补交之前已经 flush 掉的历史 tick 的输入。
            activePlayerIds.Add(newPlayerId);
            playerColors[newPlayerId] = GetPlayerColor(newPlayerId);
            world.AddPlayer(newPlayerId);
            SpawnPlayerView(newPlayerId);

            var accept = new NetDataWriter();
            accept.WriteJoinAccept(newPlayerId, nextTickToCollect, existingSnapshot);
            peer.Send(accept, DeliveryMethod.ReliableOrdered);

            var joined = new NetDataWriter();
            joined.WritePlayerJoined(newPlayerId, playerColors[newPlayerId]);
            netManager.SendToAll(joined, DeliveryMethod.ReliableOrdered, peer);

            SetStatus($"Player {newPlayerId} joined ({activePlayerIds.Count} total)");
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (isServer && playerIdByPeer.TryGetValue(peer, out var pid))
            {
                activePlayerIds.Remove(pid);
                peersByPlayerId.Remove(pid);
                playerIdByPeer.Remove(peer);
                playerColors.Remove(pid);
                TryFlushTicks(); // 少了一个人可能正好能把卡住的 tick 放行

                var left = new NetDataWriter();
                left.WritePlayerLeft(pid);
                netManager.SendToAll(left, DeliveryMethod.ReliableOrdered);

                SetStatus($"Player {pid} left");
            }
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            var msgType = (NetMsgType)reader.GetByte();
            switch (msgType)
            {
                case NetMsgType.JoinAccept:
                {
                    myPlayerId = reader.GetInt();
                    int startTick = reader.GetInt();
                    int count = reader.GetInt();
                    for (int i = 0; i < count; i++)
                    {
                        int pid = reader.GetInt();
                        long x = reader.GetLong();
                        long y = reader.GetLong();
                        byte facing = reader.GetByte();
                        var color = new Color32(reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte());
                        playerColors[pid] = color;
                        // 用快照坐标+朝向初始化，而不是默认的 (0,0) / 朝向0。
                        world.AddPlayer(pid, new FpVec2(Fp.FromRaw(x), Fp.FromRaw(y)));
                        world.Facings[pid] = facing;
                        world.PreviousFacings[pid] = facing; // 避免进场瞬间有一次"从默认朝向转过去"的多余动画
                        SpawnPlayerView(pid);
                    }
                    playerColors[myPlayerId] = GetPlayerColor(myPlayerId);
                    world.AddPlayer(myPlayerId);
                    SpawnPlayerView(myPlayerId);
                    myNextInputTick = startTick;
                    localSimTick = startTick;
                    SetStatus($"Joined as Player {myPlayerId} at tick {startTick}");
                    CloseConnectionPanel();
                    localLogicCamera?.SetCursorLocked(true);
                    break;
                }
                case NetMsgType.PlayerJoined:
                {
                    int pid = reader.GetInt();
                    var color = new Color32(reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte());
                    playerColors[pid] = color;
                    world.AddPlayer(pid);
                    SpawnPlayerView(pid);
                    break;
                }
                case NetMsgType.PlayerLeft:
                {
                    int pid = reader.GetInt();
                    world.RemovePlayer(pid);
                    playerColors.Remove(pid);
                    if (playerViews.TryGetValue(pid, out var v))
                    {
                        Destroy(v.gameObject);
                        playerViews.Remove(pid);
                    }
                    break;
                }
                case NetMsgType.PlayerInput:
                {
                    // 只有服务器会收到这个消息类型。
                    int tick = reader.GetInt();
                    byte angle = reader.GetByte();
                    bool moving = reader.GetBool();
                    int fromPlayerId = playerIdByPeer[peer];
                    ServerReceiveInput(fromPlayerId, tick, angle, moving);
                    break;
                }
                case NetMsgType.InputFrame:
                {
                    // 只有非 host 的纯客户端会走网络收到这个（host 走本地直调）。
                    int tick = reader.GetInt();
                    int count = reader.GetInt();
                    var entries = new List<(int, byte, bool)>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int pid = reader.GetInt();
                        byte angle = reader.GetByte();
                        bool moving = reader.GetBool();
                        entries.Add((pid, angle, moving));
                    }
                    ApplyInputFrame(tick, entries);
                    break;
                }
            }
            reader.Recycle();
        }

        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
            Debug.LogError($"[Net] Socket error: {socketError}");
        }
    }
}
