using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

namespace FrameSyncDemo
{
    public enum NetMsgType : byte
    {
        JoinAccept = 1,
        PlayerJoined = 2,
        PlayerLeft = 3,
        PlayerInput = 4,
        InputFrame = 5,
    }

    public static class NetWriteExtensions
    {
        public static void WriteJoinAccept(this NetDataWriter w, int yourPlayerId, int startTick, List<(int playerId, long x, long y, Color32 color)> existingPlayers)
        {
            w.Put((byte)NetMsgType.JoinAccept);
            w.Put(yourPlayerId);
            w.Put(startTick);
            w.Put(existingPlayers.Count);
            foreach (var p in existingPlayers)
            {
                w.Put(p.playerId);
                w.Put(p.x);
                w.Put(p.y);
                w.Put(p.color.r);
                w.Put(p.color.g);
                w.Put(p.color.b);
                w.Put(p.color.a);
            }
        }

        public static void WritePlayerJoined(this NetDataWriter w, int playerId, Color32 color)
        {
            w.Put((byte)NetMsgType.PlayerJoined);
            w.Put(playerId);
            w.Put(color.r);
            w.Put(color.g);
            w.Put(color.b);
            w.Put(color.a);
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
