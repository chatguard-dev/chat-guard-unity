#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>
    /// One probability per <see cref="VerdictCategory"/>, each clamped to [0, 1].
    /// Jev fills these with calibrated Noul probabilities; the local filter only ever writes 0 or 1.
    /// </summary>
    public sealed class VerdictSet
    {
        private readonly double[] _p = new double[VerdictCategories.Count];

        public VerdictSet()
        {
        }

        public VerdictSet(double insult, double threat, double hate, double sexual, double spam, double trading)
        {
            this[VerdictCategory.Insult] = insult;
            this[VerdictCategory.Threat] = threat;
            this[VerdictCategory.Hate] = hate;
            this[VerdictCategory.Sexual] = sexual;
            this[VerdictCategory.Spam] = spam;
            this[VerdictCategory.Trading] = trading;
        }

        public double this[VerdictCategory category]
        {
            get { return _p[(int)category]; }
            set { _p[(int)category] = Clamp01(value); }
        }

        public double Insult { get { return this[VerdictCategory.Insult]; } }
        public double Threat { get { return this[VerdictCategory.Threat]; } }
        public double Hate { get { return this[VerdictCategory.Hate]; } }
        public double Sexual { get { return this[VerdictCategory.Sexual]; } }
        public double Spam { get { return this[VerdictCategory.Spam]; } }
        public double Trading { get { return this[VerdictCategory.Trading]; } }

        /// <summary>The largest probability across all categories.</summary>
        public double Max()
        {
            double max = 0;
            for (int i = 0; i < _p.Length; i++)
            {
                if (_p[i] > max)
                {
                    max = _p[i];
                }
            }

            return max;
        }

        public VerdictSet Clone()
        {
            var copy = new VerdictSet();
            Array.Copy(_p, copy._p, _p.Length);
            return copy;
        }

        internal static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            return value < 0 ? 0 : (value > 1 ? 1 : value);
        }
    }
}
