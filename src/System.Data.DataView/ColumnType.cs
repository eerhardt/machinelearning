// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma warning disable 420 // volatile with Interlocked.CompareExchange

using Microsoft.ML.Runtime.Internal;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Microsoft.ML.Runtime.Data
{
    /// <summary>
    /// ColumnType is the abstract base class for all types in the IDataView type system.
    /// </summary>
    public abstract class ColumnType : IEquatable<ColumnType>
    {
        private readonly Type _rawType;
        private readonly DataKind _rawKind;

        // We cache these for speed and code size.
        private readonly bool _isPrimitive;
        private readonly bool _isVector;
        private readonly bool _isNumber;
        private readonly bool _isKey;

        // This private constructor sets all the _isXxx flags. It is invoked by other ctors.
        private ColumnType()
        {
            _isPrimitive = this is PrimitiveType;
            _isVector = this is VectorType;
            _isNumber = this is NumberType;
            _isKey = this is KeyType;
        }

        protected ColumnType(Type rawType)
            : this()
        {
            Contracts.CheckValue(rawType, nameof(rawType));
            _rawType = rawType;
            _rawType.TryGetDataKind(out _rawKind);
        }

        /// <summary>
        /// Internal sub types can pass both the rawType and rawKind values. This asserts that they
        /// are consistent.
        /// </summary>
        internal ColumnType(Type rawType, DataKind rawKind)
            : this()
        {
            Contracts.AssertValue(rawType);
#if DEBUG
            DataKind tmp;
            rawType.TryGetDataKind(out tmp);
            Contracts.Assert(tmp == rawKind);
#endif
            _rawType = rawType;
            _rawKind = rawKind;
        }

        /// <summary>
        /// The raw System.Type for this ColumnType. Note that this is the raw representation type
        /// and NOT the complete information content of the ColumnType. Code should not assume that
        /// a RawType uniquely identifiers a ColumnType.
        /// </summary>
        public Type RawType { get { return _rawType; } }

        /// <summary>
        /// The DataKind corresponding to RawType, if there is one (zero otherwise). It is equivalent
        /// to the result produced by DataKindExtensions.TryGetDataKind(RawType, out kind).
        /// </summary>
        public DataKind RawKind { get { return _rawKind; } }

        /// <summary>
        /// Whether this is a primitive type.
        /// </summary>
        public bool IsPrimitive { get { return _isPrimitive; } }

        /// <summary>
        /// Equivalent to "this as PrimitiveType".
        /// </summary>
        public PrimitiveType AsPrimitive { get { return _isPrimitive ? (PrimitiveType)this : null; } }

        /// <summary>
        /// Whether this type is a standard numeric type.
        /// </summary>
        public bool IsNumber { get { return _isNumber; } }

        /// <summary>
        /// Whether this type is the standard text type.
        /// </summary>
        public bool IsText
        {
            get
            {
                if (!(this is TextType))
                    return false;
                // TextType is a singleton.
                Contracts.Assert(this == TextType.Instance);
                return true;
            }
        }

        /// <summary>
        /// Whether this type is the standard boolean type.
        /// </summary>
        public bool IsBool
        {
            get
            {
                if (!(this is BoolType))
                    return false;
                // BoolType is a singleton.
                Contracts.Assert(this == BoolType.Instance);
                return true;
            }
        }

        /// <summary>
        /// Whether this type is the standard timespan type.
        /// </summary>
        public bool IsTimeSpan
        {
            get
            {
                if (!(this is TimeSpanType))
                    return false;
                // TimeSpanType is a singleton.
                Contracts.Assert(this == TimeSpanType.Instance);
                return true;
            }
        }

        /// <summary>
        /// Whether this type is a DvDateTime.
        /// </summary>
        public bool IsDateTime
        {
            get
            {
                if (!(this is DateTimeType))
                    return false;
                // DateTimeType is a singleton.
                Contracts.Assert(this == DateTimeType.Instance);
                return true;
            }
        }

        /// <summary>
        /// Whether this type is a DvDateTimeZone.
        /// </summary>
        public bool IsDateTimeZone
        {
            get
            {
                if (!(this is DateTimeZoneType))
                    return false;
                // DateTimeZoneType is a singleton.
                Contracts.Assert(this == DateTimeZoneType.Instance);
                return true;
            }
        }

        /// <summary>
        /// Whether this type is a standard scalar type completely determined by its RawType
        /// (not a KeyType or StructureType, etc).
        /// </summary>
        public bool IsStandardScalar
        {
            get { return IsNumber || IsText || IsBool || IsTimeSpan || IsDateTime || IsDateTimeZone; }
        }

        /// <summary>
        /// Whether this type is a key type, which implies that the order of values is not significant,
        /// and arithmetic is non-sensical. A key type can define a cardinality.
        /// </summary>
        public bool IsKey { get { return _isKey; } }

        /// <summary>
        /// Equivalent to "this as KeyType".
        /// </summary>
        public KeyType AsKey { get { return _isKey ? (KeyType)this : null; } }

        /// <summary>
        /// Zero return means either it's not a key type or the cardinality is unknown.
        /// </summary>
        public int KeyCount { get { return KeyCountCore; } }

        /// <summary>
        /// The only sub-class that should override this is KeyType!
        /// </summary>
        internal virtual int KeyCountCore { get { return 0; } }

        /// <summary>
        /// Whether this is a vector type.
        /// </summary>
        public bool IsVector { get { return _isVector; } }

        /// <summary>
        /// Equivalent to "this as VectorType".
        /// </summary>
        public VectorType AsVector { get { return _isVector ? (VectorType)this : null; } }

        /// <summary>
        /// For non-vector types, this returns the column type itself (ie, return this).
        /// </summary>
        public ColumnType ItemType { get { return ItemTypeCore; } }

        /// <summary>
        /// Whether this is a vector type with known size. Returns false for non-vector types.
        /// Equivalent to VectorSize > 0.
        /// </summary>
        public bool IsKnownSizeVector { get { return VectorSize > 0; } }

        /// <summary>
        /// Zero return means either it's not a vector or the size is unknown. Equivalent to
        /// IsVector ? ValueCount : 0 and to IsKnownSizeVector ? ValueCount : 0.
        /// </summary>
        public int VectorSize { get { return VectorSizeCore; } }

        /// <summary>
        /// For non-vectors, this returns one. For unknown size vectors, it returns zero.
        /// Equivalent to IsVector ? VectorSize : 1.
        /// </summary>
        public int ValueCount { get { return ValueCountCore; } }

        /// <summary>
        /// The only sub-class that should override this is VectorType!
        /// </summary>
        internal virtual ColumnType ItemTypeCore { get { return this; } }

        /// <summary>
        /// The only sub-class that should override this is VectorType!
        /// </summary>
        internal virtual int VectorSizeCore { get { return 0; } }

        /// <summary>
        /// The only sub-class that should override this is VectorType!
        /// </summary>
        internal virtual int ValueCountCore { get { return 1; } }

        public abstract bool Equals(ColumnType other);

        /// <summary>
        /// Equivalent to calling Equals(ColumnType) for non-vector types. For vector type,
        /// returns true if current and other vector types have the same size and item type.
        /// </summary>
        public bool SameSizeAndItemType(ColumnType other)
        {
            if (other == null)
                return false;

            if (Equals(other))
                return true;

            // For vector types, we don't care about the factoring of the dimensions.
            if (!IsVector || !other.IsVector)
                return false;
            if (!ItemType.Equals(other.ItemType))
                return false;
            return VectorSize == other.VectorSize;
        }
    }

    /// <summary>
    /// The abstract base class for all non-primitive types.
    /// </summary>
    public abstract class StructuredType : ColumnType
    {
        protected StructuredType(Type rawType)
            : base(rawType)
        {
            Contracts.Assert(!IsPrimitive);
        }

        internal StructuredType(Type rawType, DataKind rawKind)
            : base(rawType, rawKind)
        {
            Contracts.Assert(!IsPrimitive);
        }
    }

    /// <summary>
    /// The abstract base class for all primitive types. Values of these types can be freely copied
    /// without concern for ownership, mutation, or disposing.
    /// </summary>
    public abstract class PrimitiveType : ColumnType
    {
        protected PrimitiveType(Type rawType)
            : base(rawType)
        {
            Contracts.Assert(IsPrimitive);
            Contracts.CheckParam(!typeof(IDisposable).IsAssignableFrom(RawType), nameof(rawType),
                "A PrimitiveType cannot have a disposable RawType");
        }

        internal PrimitiveType(Type rawType, DataKind rawKind)
            : base(rawType, rawKind)
        {
            Contracts.Assert(IsPrimitive);
            Contracts.Assert(!typeof(IDisposable).IsAssignableFrom(RawType));
        }

        //public static PrimitiveType FromKind(DataKind kind)
        //{
        //    if (kind == DataKind.TX)
        //        return TextType.Instance;
        //    if (kind == DataKind.BL)
        //        return BoolType.Instance;
        //    if (kind == DataKind.TS)
        //        return TimeSpanType.Instance;
        //    if (kind == DataKind.DT)
        //        return DateTimeType.Instance;
        //    if (kind == DataKind.DZ)
        //        return DateTimeZoneType.Instance;
        //    return NumberType.FromKind(kind);
        //}
    }




    ///// <summary>
    ///// The standard boolean type.
    ///// </summary>
    //public sealed class BoolType : PrimitiveType
    //{
    //    private volatile static BoolType _instance;
    //    public static BoolType Instance
    //    {
    //        get
    //        {
    //            if (_instance == null)
    //                Interlocked.CompareExchange(ref _instance, new BoolType(), null);
    //            return _instance;
    //        }
    //    }

    //    private BoolType()
    //        : base(typeof(DvBool), DataKind.BL)
    //    {
    //    }

    //    public override bool Equals(ColumnType other)
    //    {
    //        if (other == this)
    //            return true;
    //        Contracts.Assert(!(other is BoolType));
    //        return false;
    //    }

    //    public override string ToString()
    //    {
    //        return "Bool";
    //    }
    //}

    //public sealed class DateTimeType : PrimitiveType
    //{
    //    private volatile static DateTimeType _instance;
    //    public static DateTimeType Instance
    //    {
    //        get
    //        {
    //            if (_instance == null)
    //                Interlocked.CompareExchange(ref _instance, new DateTimeType(), null);
    //            return _instance;
    //        }
    //    }

    //    private DateTimeType()
    //        : base(typeof(DvDateTime), DataKind.DT)
    //    {
    //    }

    //    public override bool Equals(ColumnType other)
    //    {
    //        if (other == this)
    //            return true;
    //        Contracts.Assert(!(other is DateTimeType));
    //        return false;
    //    }

    //    public override string ToString()
    //    {
    //        return "DateTime";
    //    }
    //}

 

}