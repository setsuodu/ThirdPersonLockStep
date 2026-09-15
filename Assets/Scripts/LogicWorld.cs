using System.Collections.Generic;
using System.Linq;

namespace FrameSyncDemo
{
    /// <summary>
    /// 纯逻辑层的世界状态，不引用任何 Unity 类型，方便脱离引擎单独重放/测试。
    /// Step() 必须在所有客户端上，针对同一个 tick、用完全相同的输入集合、按相同顺序调用，
    /// 这样各端算出来的结果才会逐位一致（这是本demo要验证的核心假设）。
    /// </summary>
    public class LogicWorld
    {
        // 每 tick 移动速度，纯定点常量，不用运行时算出来的浮点值。
        public static readonly Fp MoveSpeedPerTick = Fp.FromRaw(3277); // ≈0.05 units/tick

        // 1/sqrt(2) 的 Q16.16 定点近似，编译期写死的常量，不在运行时调用 Math.Sqrt，
        // 避免不同平台 sqrt 实现末位不一致带来的跨端分歧。
        private static readonly Fp DiagFactor = Fp.FromRaw(46341);

        public readonly Dictionary<int, FpVec2> Positions = new Dictionary<int, FpVec2>();

        public void AddPlayer(int playerId)
        {
            if (!Positions.ContainsKey(playerId))
                Positions[playerId] = FpVec2.Zero;
        }

        public void RemovePlayer(int playerId)
        {
            Positions.Remove(playerId);
        }

        /// <summary>
        /// 推进一个逻辑帧。inputsByPlayer 里没有的玩家视为本帧无输入（dx=dy=0）。
        /// 遍历顺序固定按 playerId 排序，避免字典遍历顺序在不同 runtime 下不确定。
        /// </summary>
        public void Step(Dictionary<int, (sbyte dx, sbyte dy)> inputsByPlayer)
        {
            foreach (var playerId in Positions.Keys.OrderBy(id => id).ToList())
            {
                if (!inputsByPlayer.TryGetValue(playerId, out var input))
                    input = (0, 0);

                Fp dx = Fp.FromInt(input.dx);
                Fp dy = Fp.FromInt(input.dy);

                if (input.dx != 0 && input.dy != 0)
                {
                    // 斜向移动用预烘焙常量归一化，不做运行时开方。
                    dx = dx * DiagFactor;
                    dy = dy * DiagFactor;
                }

                var delta = new FpVec2(dx, dy) * MoveSpeedPerTick;
                Positions[playerId] = Positions[playerId] + delta;
            }
        }

        /// <summary>
        /// 整个世界状态的简单哈希，用来快速比对不同客户端在同一 tick 上是否算出了完全相同的结果。
        /// 如果两端在同一 tick 打印出的 Hash 不一致，说明确定性被破坏了。
        /// </summary>
        public long ComputeStateHash()
        {
            long hash = 17;
            foreach (var playerId in Positions.Keys.OrderBy(id => id))
            {
                var p = Positions[playerId];
                hash = hash * 31 + playerId;
                hash = hash * 31 + p.X.Raw;
                hash = hash * 31 + p.Y.Raw;
            }
            return hash;
        }
    }
}
