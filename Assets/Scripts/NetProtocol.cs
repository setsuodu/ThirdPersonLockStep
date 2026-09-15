using System.Collections.Generic;
using LiteNetLib.Utils;

namespace FrameSyncDemo
{
    public enum NetMsgType : byte
    {
        JoinAccept = 1,   // Server -> Client: 分配 playerId + 起始 tick + 当前已有玩家列表
        PlayerJoined = 2, // Server -> Client: 广播有新玩家加入
        PlayerLeft = 3,   // Server -> Client: 广播有玩家离开
        PlayerInput = 4,  // Client -> Server: 本客户端某一 tick 的输入
        InputFrame = 5,   // Server -> Client: 服务器汇总后广播的、某一 tick 的全体玩家输入
    }

    public static class NetWriteExtensions
    {
        public static void WriteJoinAccept(this NetDataWriter w, int yourPlayerId, int startTick, int[] existingPlayerIds)
        {
            w.Put((byte)NetMsgType.JoinAccept);
            w.Put(yourPlayerId);
            w.Put(startTick);
            w.Put(existingPlayerIds.Length);
            foreach (var id in existingPlayerIds) w.Put(id);
        }

        public static void WritePlayerJoined(this NetDataWriter w, int playerId)
        {
            w.Put((byte)NetMsgType.PlayerJoined);
            w.Put(playerId);
        }

        public static void WritePlayerLeft(this NetDataWriter w, int playerId)
        {
            w.Put((byte)NetMsgType.PlayerLeft);
            w.Put(playerId);
        }

        public static void WritePlayerInput(this NetDataWriter w, int tick, sbyte dx, sbyte dy)
        {
            w.Put((byte)NetMsgType.PlayerInput);
            w.Put(tick);
            w.Put(dx);
            w.Put(dy);
        }

        public static void WriteInputFrame(this NetDataWriter w, int tick, List<(int playerId, sbyte dx, sbyte dy)> entries)
        {
            w.Put((byte)NetMsgType.InputFrame);
            w.Put(tick);
            w.Put(entries.Count);
            foreach (var e in entries)
            {
                w.Put(e.playerId);
                w.Put(e.dx);
                w.Put(e.dy);
            }
        }
    }
}
