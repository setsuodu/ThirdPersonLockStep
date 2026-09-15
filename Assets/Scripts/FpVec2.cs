namespace FrameSyncDemo
{
    public struct FpVec2
    {
        public Fp X;
        public Fp Y;

        public FpVec2(Fp x, Fp y) { X = x; Y = y; }

        public static readonly FpVec2 Zero = new FpVec2(Fp.Zero, Fp.Zero);

        public static FpVec2 operator +(FpVec2 a, FpVec2 b) => new FpVec2(a.X + b.X, a.Y + b.Y);
        public static FpVec2 operator -(FpVec2 a, FpVec2 b) => new FpVec2(a.X - b.X, a.Y - b.Y);
        public static FpVec2 operator *(FpVec2 a, Fp s) => new FpVec2(a.X * s, a.Y * s);
    }
}
