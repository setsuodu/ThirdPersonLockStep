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

        [Header("Simulation")]
        public GameObject playerViewPrefab; // 留空则用默认 Cube
        public int tickRate = 20;

        private float tickInterval;
        private float tickAccumulator;

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
        private readonly Dictionary<int, List<(int playerId, sbyte dx, sbyte dy)>> receivedFrames = new Dictionary<int, List<(int, sbyte, sbyte)>>();
        private readonly Dictionary<int, PlayerView> playerViews = new Dictionary<int, PlayerView>();

        // ---- 服务器侧状态（只有 isServer==true 时才会被用到）----
        private readonly Dictionary<int, NetPeer> peersByPlayerId = new Dictionary<int, NetPeer>();
        private readonly Dictionary<NetPeer, int> playerIdByPeer = new Dictionary<NetPeer, int>();
        private readonly HashSet<int> activePlayerIds = new HashSet<int>();
        private readonly Dictionary<int, Dictionary<int, (sbyte dx, sbyte dy)>> pendingByTick = new Dictionary<int, Dictionary<int, (sbyte, sbyte)>>();
        private int nextTickToCollect = 0;
        private int nextPlayerId = 0;

        void Awake()
        {
            tickInterval = 1f / tickRate;
            netManager = new NetManager(this) { AutoRecycle = true };
        }

        void Update()
        {
            netManager?.PollEvents();

            if (isClient)
            {
                tickAccumulator += Time.deltaTime;
                while (tickAccumulator >= tickInterval)
                {
                    tickAccumulator -= tickInterval;
                    SampleAndSendInput();
                }
            }

            // 表现层：把逻辑位置（定点）转成 float 显示，单向流动，不回写。
            foreach (var kv in playerViews)
            {
                var pos = world.Positions.TryGetValue(kv.Key, out var p) ? p : FpVec2.Zero;
                kv.Value.SetLogicPosition(pos);
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
            world.AddPlayer(myPlayerId);
            SpawnPlayerView(myPlayerId);
            SetStatus($"Hosting on port {port} as Player {myPlayerId}");
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
            sbyte dx = (sbyte)Mathf.RoundToInt(Mathf.Sign(Input.GetAxisRaw("Horizontal")));
            sbyte dy = (sbyte)Mathf.RoundToInt(Mathf.Sign(Input.GetAxisRaw("Vertical")));

            int tick = myNextInputTick++;

            if (isServer)
            {
                // host 同时是自己的客户端：不走真实 socket，直接喂给服务器聚合逻辑。
                ServerReceiveInput(myPlayerId, tick, dx, dy);
            }
            else
            {
                var writer = new NetDataWriter();
                writer.WritePlayerInput(tick, dx, dy);
                netManager.FirstPeer?.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        // ============ 服务器侧聚合 ============

        private void ServerReceiveInput(int playerId, int tick, sbyte dx, sbyte dy)
        {
            if (!pendingByTick.TryGetValue(tick, out var dict))
            {
                dict = new Dictionary<int, (sbyte, sbyte)>();
                pendingByTick[tick] = dict;
            }
            dict[playerId] = (dx, dy);

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

        private void BroadcastInputFrame(int tick, List<(int playerId, sbyte dx, sbyte dy)> entries)
        {
            var writer = new NetDataWriter();
            writer.WriteInputFrame(tick, entries);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

            // host 自己不会给自己发 socket 包，直接本地应用。
            ApplyInputFrame(tick, entries);
        }

        // ============ 客户端侧：应用一致的输入帧，推进模拟 ============

        private void ApplyInputFrame(int tick, List<(int playerId, sbyte dx, sbyte dy)> entries)
        {
            receivedFrames[tick] = entries;

            // 可能一次性收到多个连续 tick（比如刚连上时补发的），循环把能推进的都推进掉。
            while (receivedFrames.TryGetValue(localSimTick, out var frame))
            {
                var inputsByPlayer = frame.ToDictionary(e => e.playerId, e => ((sbyte)e.dx, (sbyte)e.dy));
                world.Step(inputsByPlayer);

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
            var go = playerViewPrefab != null ? Instantiate(playerViewPrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Player_{playerId}";
            var view = go.GetComponent<PlayerView>();
            if (view == null) view = go.AddComponent<PlayerView>();
            playerViews[playerId] = view;
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
                .Select(id => (id, world.Positions[id].X.Raw, world.Positions[id].Y.Raw))
                .ToList();

            // 简化处理：新玩家从"当前正在收集的 tick"开始参与要求，
            // 不需要补交之前已经 flush 掉的历史 tick 的输入。
            activePlayerIds.Add(newPlayerId);
            world.AddPlayer(newPlayerId);
            SpawnPlayerView(newPlayerId);

            var accept = new NetDataWriter();
            accept.WriteJoinAccept(newPlayerId, nextTickToCollect, existingSnapshot);
            peer.Send(accept, DeliveryMethod.ReliableOrdered);

            var joined = new NetDataWriter();
            joined.WritePlayerJoined(newPlayerId);
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
                        // 用快照坐标初始化，而不是默认的 (0,0) —— 这就是"接上现有状态"的关键一步。
                        world.AddPlayer(pid, new FpVec2(Fp.FromRaw(x), Fp.FromRaw(y)));
                        SpawnPlayerView(pid);
                    }
                    world.AddPlayer(myPlayerId);
                    SpawnPlayerView(myPlayerId);
                    myNextInputTick = startTick;
                    localSimTick = startTick;
                    SetStatus($"Joined as Player {myPlayerId} at tick {startTick}");
                    break;
                }
                case NetMsgType.PlayerJoined:
                {
                    int pid = reader.GetInt();
                    world.AddPlayer(pid);
                    SpawnPlayerView(pid);
                    break;
                }
                case NetMsgType.PlayerLeft:
                {
                    int pid = reader.GetInt();
                    world.RemovePlayer(pid);
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
                    sbyte dx = reader.GetSByte();
                    sbyte dy = reader.GetSByte();
                    int fromPlayerId = playerIdByPeer[peer];
                    ServerReceiveInput(fromPlayerId, tick, dx, dy);
                    break;
                }
                case NetMsgType.InputFrame:
                {
                    // 只有非 host 的纯客户端会走网络收到这个（host 走本地直调）。
                    int tick = reader.GetInt();
                    int count = reader.GetInt();
                    var entries = new List<(int, sbyte, sbyte)>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int pid = reader.GetInt();
                        sbyte dx = reader.GetSByte();
                        sbyte dy = reader.GetSByte();
                        entries.Add((pid, dx, dy));
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
