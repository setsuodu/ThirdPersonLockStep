using System;

namespace FrameSyncDemo
{
    /// <summary>
    /// Q16.16 定点数，底层用 long 存储，避免乘法中间结果溢出。
    /// 所有逻辑层（同步域）运算必须只用这个类型，不能用 float/double。
    /// </summary>
    public struct Fp : IEquatable<Fp>
    {
        public const int FRACTION_BITS = 16;
        public const long ONE_RAW = 1L << FRACTION_BITS;

        public long Raw;

        public static readonly Fp Zero = FromRaw(0);
        public static readonly Fp One = FromRaw(ONE_RAW);

        public static Fp FromRaw(long raw) => new Fp { Raw = raw };
        public static Fp FromInt(int v) => new Fp { Raw = (long)v << FRACTION_BITS };

        /// <summary>
        /// 仅用于从 Inspector/配置读取初始常量或做调试显示，
        /// 绝不能在逐帧同步逻辑里用它做实时转换（那样又变回浮点不确定性了）。
        /// </summary>
        public static Fp FromFloatDebugOnly(float v) => new Fp { Raw = (long)Math.Round(v * ONE_RAW) };
        public float ToFloatDebugOnly() => (float)Raw / ONE_RAW;

        public static Fp operator +(Fp a, Fp b) => FromRaw(a.Raw + b.Raw);
        public static Fp operator -(Fp a, Fp b) => FromRaw(a.Raw - b.Raw);
        public static Fp operator -(Fp a) => FromRaw(-a.Raw);

        public static Fp operator *(Fp a, Fp b)
        {
            // 先在 64 位宽度下相乘，再右移回 Q16.16，防止 32 位溢出。
            long result = (a.Raw * b.Raw) >> FRACTION_BITS;
            return FromRaw(result);
        }

        public static Fp operator /(Fp a, Fp b)
        {
            // 分子先左移保留精度，再做整数除法。
            long numerator = a.Raw << FRACTION_BITS;
            return FromRaw(numerator / b.Raw);
        }

        public static bool operator ==(Fp a, Fp b) => a.Raw == b.Raw;
        public static bool operator !=(Fp a, Fp b) => a.Raw != b.Raw;

        public bool Equals(Fp other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fp other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public override string ToString() => ToFloatDebugOnly().ToString("F4");
    }
}
