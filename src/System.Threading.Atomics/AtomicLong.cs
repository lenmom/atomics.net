using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Threading.Atomics
{
    /// <summary>
    /// An <see cref="long"/> value wrapper with atomic operations
    /// </summary>
    [DebuggerDisplay("{Value}")]
    public sealed class AtomicLong : IAtomicRef<long>, IEquatable<long>, IEquatable<AtomicLong>
    {
        private MemoryOrder _order;
        private readonly BoxedInt64 _storage;

        /// <summary>
        /// Creates new instance of <see cref="AtomicLong"/>
        /// </summary>
        /// <param name="order">Affects the way store operation occur. Default is <see cref="MemoryOrder.AcqRel"/> semantics</param>
        /// <param name="align">True to store the underlying value aligned, otherwise False</param>
        public AtomicLong(MemoryOrder order = MemoryOrder.SeqCst, bool align = false)
            : this(0, order, align)
        {
            
        }

        /// <summary>
        /// Creates new instance of <see cref="AtomicLong"/>
        /// </summary>
        /// <param name="value">The value to store</param>
        /// <param name="order">Affects the way store operation occur. Load operations are always use <see cref="MemoryOrder.Acquire"/> semantics</param>
        /// <param name="align">True to store the underlying value aligned, otherwise False</param>
        public AtomicLong(long value, MemoryOrder order = MemoryOrder.SeqCst, bool align = false)
        {
            if (!order.IsSpported()) throw new ArgumentException(string.Format("{0} is not supported", order));

            _order = order;
            this._storage = BoxedInt64.Create(value, align);
        }

        [StructLayout(LayoutKind.Explicit)]
        class BoxedInt64
        {
            private const int CacheLineSize = 64;

            [FieldOffset(0)]
            public long value;

            private BoxedInt64(long value)
            {
                this.value = value;
            }

            [StructLayout(LayoutKind.Explicit)]
            class AlignedBoxedInt64 : BoxedInt64
            {
                [FieldOffset(0)]
                public new long value;

                [FieldOffset(sizeof(long))]
                private __64BitAlignedValue _alignedValue;

                public AlignedBoxedInt64(long value) : base(value)
                {
                }
            }

            [StructLayout(LayoutKind.Explicit, Size = CacheLineSize - sizeof(long))]
            unsafe struct __64BitAlignedValue
            {
                [FieldOffset(0)]
                private fixed byte pad[CacheLineSize - sizeof(long)];
            }

            public static BoxedInt64 Create(bool aligned)
            {
                return Create(0, aligned);
            }

            public static BoxedInt64 Create(long value, bool aligned)
            {
                return aligned ? new AlignedBoxedInt64(value) : new BoxedInt64(value);
            }
        }

        /// <summary>
        /// Gets or sets atomically the underlying value
        /// </summary>
        /// <remarks>This method does use CAS approach for value setting. To avoid this use <see cref="Load"/> and <see cref="Store"/> methods pair for get/set operations respectively</remarks>
        public long Value
        {
            get
            {
                if (_order != MemoryOrder.SeqCst) return IntPtr.Size == 8 ? _storage.value : Volatile.Read(ref _storage.value);

                return Volatile.Read(ref _storage.value);
            }
            set
            {
                if (_order == MemoryOrder.SeqCst)
                {
                    Interlocked.Exchange(ref _storage.value, value);
                    return;
                }

                long currentValue;
                long tempValue;
                do
                {
                    currentValue = _storage.value;
                    tempValue = value;
                } while (_storage.value != currentValue ||
                         Interlocked.CompareExchange(ref _storage.value, tempValue, currentValue) != currentValue);
            }
        }

        /// <summary>
        /// Sets the underlying value with provided <paramref name="order"/>
        /// </summary>
        /// <param name="value">The value to store</param>
        /// <param name="order">The <see cref="MemoryOrder"/> to achieve</param>
        public void Store(long value, MemoryOrder order)
        {
            switch (order)
            {
                case MemoryOrder.Relaxed:
                    this._storage.value = value;
                    break;
                case MemoryOrder.Consume:
                    throw new NotSupportedException();
                case MemoryOrder.Acquire:
                    throw new InvalidOperationException("Cannot set (store) value with Acquire semantics");
                case MemoryOrder.Release:
                case MemoryOrder.AcqRel:
#if ARM_CPU || ITANIUM_CPU
                    Platform.MemoryBarrier();
                    _storage.value = value;
#else
                    Interlocked.Exchange(ref _storage.value, value);
#endif
                    break;
                case MemoryOrder.SeqCst:
#if ARM_CPU || ITANIUM_CPU
                    Platform.MemoryBarrier();
                    _storage.value = value;
                    Platform.MemoryBarrier();
#else
                    Interlocked.Exchange(ref _storage.value, value);
#endif
                    break;
                default:
                    throw new ArgumentOutOfRangeException("order");
            }
        }

        /// <summary>
        /// Gets the underlying value with provided <paramref name="order"/>
        /// </summary>
        /// <param name="order">The <see cref="MemoryOrder"/> to achieve</param>
        /// <returns>The underlying value with provided <paramref name="order"/></returns>
        public long Load(MemoryOrder order)
        {
            if (order == MemoryOrder.Consume)
                throw new NotSupportedException();
            if (order == MemoryOrder.Release)
                throw new InvalidOperationException("Cannot get (load) value with Release semantics");
            switch (order)
            {
                case MemoryOrder.Relaxed:
                    return _storage.value;
                case MemoryOrder.Acquire:
                case MemoryOrder.AcqRel:
                case MemoryOrder.SeqCst:
#if ARM_CPU
                    var tmp = _storage.value;
                    Platform.MemoryBarrier();
                    return tmp;
#else
                    return Volatile.Read(ref _storage.value);
#endif
                default:
                    throw new ArgumentOutOfRangeException("order");
            }
        }

        /// <summary>
        /// Gets value whether the object is lock-free
        /// </summary>
        public bool IsLockFree
        {
            get { return true; }
        }

        /// <summary>
        /// Atomically compares underlying value with <paramref name="comparand"/> for equality and, if they are equal, replaces the first value.
        /// </summary>
        /// <param name="value">The value that replaces the underlying value if the comparison results in equality</param>
        /// <param name="comparand">The value that is compared to the underlying value.</param>
        /// <returns>The original underlying value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long CompareExchange(long value, long comparand)
        {
            return Interlocked.CompareExchange(ref _storage.value, value, comparand);
        }

        void IAtomicRef<long>.Store(ref long value, MemoryOrder order)
        {
            Store(value, order);
        }

        /// <summary>
        /// Increments the <see cref="AtomicLong"/> operand by one.
        /// </summary>
        /// <param name="atomicLong">The value to increment.</param>
        /// <returns>The value of <paramref name="atomicLong"/> incremented by 1.</returns>
        public static AtomicLong operator ++(AtomicLong atomicLong)
        {
            Interlocked.Increment(ref atomicLong._storage.value);
            return atomicLong;
        }

        /// <summary>
        /// Decrements the <see cref="AtomicLong"/> operand by one.
        /// </summary>
        /// <param name="atomicLong">The value to decrement.</param>
        /// <returns>The value of <paramref name="atomicLong"/> decremented by 1.</returns>
        public static AtomicLong operator --(AtomicLong atomicLong)
        {
            Interlocked.Decrement(ref atomicLong._storage.value);
            return atomicLong;
        }

        /// <summary>
        /// Adds specified <paramref name="value"/> to <see cref="AtomicLong"/> and returns the result as a <see cref="long"/>.
        /// </summary>
        /// <param name="atomicLong">The <paramref name="atomicLong"/> used for addition.</param>
        /// <param name="value">The value to add</param>
        /// <returns>The result of adding value to <paramref name="atomicLong"/></returns>
        public static long operator +(AtomicLong atomicLong, long value)
        {
            return atomicLong.Load(MemoryOrder.SeqCst) + value;
        }

        /// <summary>
        /// Subtracts <paramref name="value"/> from <paramref name="atomicLong"/> and returns the result as a <see cref="long"/>.
        /// </summary>
        /// <param name="atomicLong">The <paramref name="atomicLong"/> from which <paramref name="value"/> is subtracted.</param>
        /// <param name="value">The value to subtract from <paramref name="atomicLong"/></param>
        /// <returns>The result of subtracting value from <see cref="AtomicLong"/></returns>
        public static long operator -(AtomicLong atomicLong, long value)
        {
            return atomicLong.Load(MemoryOrder.SeqCst) - value;
        }

        /// <summary>
        /// Multiplies <see cref="AtomicLong"/> by specified <paramref name="value"/> and returns the result as a <see cref="long"/>.
        /// </summary>
        /// <param name="atomicLong">The <see cref="AtomicLong"/> to multiply.</param>
        /// <param name="value">The value to multiply</param>
        /// <returns>The result of multiplying <see cref="AtomicLong"/> and <paramref name="value"/></returns>
        public static long operator *(AtomicLong atomicLong, long value)
        {
            return atomicLong.Load(MemoryOrder.SeqCst) * value;
        }

        /// <summary>
        /// Divides the specified <see cref="AtomicLong"/> by the specified <paramref name="value"/> and returns the resulting as <see cref="long"/>.
        /// </summary>
        /// <param name="atomicLong">The <see cref="AtomicLong"/> to divide</param>
        /// <param name="value">The value by which <paramref name="atomicLong"/> will be divided.</param>
        /// <returns>The result of dividing <paramref name="atomicLong"/> by <paramref name="value"/>.</returns>
        public static long operator /(AtomicLong atomicLong, long value)
        {
            if (value == 0) throw new DivideByZeroException();

            return atomicLong.Load(MemoryOrder.SeqCst) / value;
        }

        /// <summary>
        /// Defines an implicit conversion of a <see cref="AtomicLong"/> to a 64-bit signed integer.
        /// </summary>
        /// <param name="atomicLong">The <see cref="AtomicLong"/> to convert.</param>
        /// <returns>The converted <see cref="AtomicLong"/>.</returns>
        public static implicit operator long(AtomicLong atomicLong)
        {
            return atomicLong.Value;
        }

        /// <summary>
        /// Defines an implicit conversion of a 64-bit signed integer to a <see cref="AtomicLong"/>.
        /// </summary>
        /// <param name="value">The 64-bit signed integer to convert.</param>
        /// <returns>The converted 64-bit signed integer.</returns>
        public static implicit operator AtomicLong(long value)
        {
            return new AtomicLong(value);
        }

        /// <summary>
        /// Serves as the default hash function
        /// </summary>
        /// <returns>A hash code for the current <see cref="AtomicLong"/></returns>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Returns a value that indicates whether <see cref="AtomicLong"/> and <see cref="long"/> are equal.
        /// </summary>
        /// <param name="x">The first value (<see cref="AtomicLong"/>) to compare.</param>
        /// <param name="y">The second value (<see cref="long"/>) to compare.</param>
        /// <returns><c>true</c> if <paramref name="x"/> and <paramref name="y"/> are equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(AtomicLong x, long y)
        {
            return (x != null && x.Value == y);
        }

        /// <summary>
        /// Returns a value that indicates whether <see cref="AtomicLong"/> and <see cref="long"/> have different values.
        /// </summary>
        /// <param name="x">The first value (<see cref="AtomicLong"/>) to compare.</param>
        /// <param name="y">The second value (<see cref="long"/>) to compare.</param>
        /// <returns><c>true</c> if <paramref name="x"/> and <paramref name="y"/> are not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(AtomicLong x, long y)
        {
            return (x != null && x.Value != y);
        }

        /// <summary>
        /// Returns a value indicating whether this instance and a specified Object represent the same type and value.
        /// </summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns><c>true</c> if <paramref name="obj"/> is a <see cref="AtomicLong"/> and equal to this instance; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            AtomicLong other = obj as AtomicLong;
            if (other == null) return false;

            return object.ReferenceEquals(this, other) || this.Value == other.Value;
        }

        /// <summary>
        /// Returns a value indicating whether this instance and a specified <paramref name="other"/> represent the same value.
        /// </summary>
        /// <param name="other">An object to compare to this instance.</param>
        /// <returns><c>true</c> if <paramref name="other"/> is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(long other)
        {
            return this.Value == other;
        }

        /// <summary>
        /// Returns a value indicating whether this instance and a specified <see cref="AtomicLong"/> object represent the same value.
        /// </summary>
        /// <param name="other">An object to compare to this instance.</param>
        /// <returns><c>true</c> if <paramref name="other"/> is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(AtomicLong other)
        {
            return this.Value == other.Value;
        }

        long IAtomic<long>.Value
        {
            get { return this.Value; }
            set { this.Value = value; }
        }

        long IAtomicOperators<long>.CompareExchange(ref long location1, long value, long comparand)
        {
            return Interlocked.CompareExchange(ref location1, value, comparand);
        }

        bool IAtomicOperators<long>.Supports<TType>()
        {
            return typeof(TType) == typeof(long);
        }
    }
}