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
        // 每 tick 移动速度。默认值≈0.05 units/tick；GameNetwork.Awake() 会用
        // Fp.FromFloatDebugOnly() 按 Inspector 里配置的"units/秒"重新换算并覆盖这个值——
        // 这属于允许的一次性配置转换，不是运行时逐帧的浮点运算，跟 Fp.cs 里的约定一致。
        // 注意：这个值必须在所有客户端上配置成一样的，它本质上也是"同步契约"的一部分。
        public Fp MoveSpeedPerTick = Fp.FromRaw(3277);

        public readonly Dictionary<int, FpVec2> Positions = new Dictionary<int, FpVec2>();

        // 朝向也是同步状态的一部分——不能只在表现层本地转，否则各客户端看到
        // 同一个角色的朝向会对不上。只在"确实在移动"的tick才更新，停下来后
        // 保留最后一次移动方向（标准TPS行为），默认朝向 angleIndex=0。
        public readonly Dictionary<int, byte> Facings = new Dictionary<int, byte>();

        // "上一个tick结束时"的快照，专门给表现层做插值用（解决tick频率<渲染帧率
        // 导致的阶梯式跳变/抖动）。这两份数据本身不参与任何同步逻辑判定，纯粹是
        // 渲染层的输入，所以放在这里而不是单独搞一份、还能保证生命周期和主状态一致。
        public readonly Dictionary<int, FpVec2> PreviousPositions = new Dictionary<int, FpVec2>();
        public readonly Dictionary<int, byte> PreviousFacings = new Dictionary<int, byte>();

        public void AddPlayer(int playerId, FpVec2? initialPosition = null)
        {
            if (!Positions.ContainsKey(playerId))
            {
                Positions[playerId] = initialPosition ?? FpVec2.Zero;
                Facings[playerId] = 0;
                PreviousPositions[playerId] = Positions[playerId];
                PreviousFacings[playerId] = 0;
            }
        }

        public void RemovePlayer(int playerId)
        {
            Positions.Remove(playerId);
            Facings.Remove(playerId);
            PreviousPositions.Remove(playerId);
            PreviousFacings.Remove(playerId);
        }

        /// <summary>
        /// 推进一个逻辑帧。inputsByPlayer 里没有的玩家，或 moving=false 的玩家，本帧不移动。
        /// 遍历顺序固定按 playerId 排序，避免字典遍历顺序在不同 runtime 下不确定。
        /// 方向通过 FpTrig 查表得到，天然是归一化的单位向量，不需要再对角线特判。
        /// 开头先把"这一tick之前"的状态存进 Previous，供表现层插值。
        /// </summary>
        public void Step(Dictionary<int, (byte angle, bool moving)> inputsByPlayer)
        {
            foreach (var kv in Positions) PreviousPositions[kv.Key] = kv.Value;
            foreach (var kv in Facings) PreviousFacings[kv.Key] = kv.Value;

            foreach (var playerId in Positions.Keys.OrderBy(id => id).ToList())
            {
                if (!inputsByPlayer.TryGetValue(playerId, out var input) || !input.moving)
                    continue;

                var dir = FpTrig.DirFromAngle(input.angle);
                var delta = dir * MoveSpeedPerTick;
                Positions[playerId] = Positions[playerId] + delta;
                Facings[playerId] = input.angle;
            }
        }

        /// <summary>
        /// 整个世界状态的简单哈希，用来快速比对不同客户端在同一 tick 上是否算出了完全相同的结果。
        /// 朝向也纳入哈希——这样"朝向是否也保持一致"同样能被这一个数字验证到，不用额外肉眼比对。
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
                hash = hash * 31 + Facings[playerId];
            }
            return hash;
        }
    }
}
