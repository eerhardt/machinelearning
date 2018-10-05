// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.ML.Runtime.Data
{
    /// <summary>
    /// A buffer that supports dense vector representation. When an instance of this
    /// is passed to a row cursor getter, the callee is free to take ownership of
    /// and re-use the backing data structure (Buffer).
    /// </summary>
    public readonly struct DenseVector<T>
    {
        /// <summary>
        /// The logical length of the buffer.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the total number of elements the backing data structure can hold.
        /// </summary>
        public int Capacity => Buffer.Length;

        /// <summary>
        /// Gets a <see cref="Span{T}"/> of all the values in this vector.
        /// </summary>
        /// <remarks>
        /// Calling Values multiple times should be avoided if possible. Instead, cache the
        /// resulting <see cref="Span{T}"/> in a local variable if it is required more than once.
        /// </remarks>
        public Span<T> Values
        {
            get
            {
                return Buffer.Span.Slice(0, Length);
            }
        }

        /// <summary>
        /// The backing <see cref="Memory{T}"/> data structure that holds the values for this vector.
        /// </summary>
        public Memory<T> Buffer { get; }

        /// <summary>
        /// Initializes a new instance of a DenseVector.
        /// </summary>
        /// <param name="buffer">
        /// The backing data structure of values.
        /// </param>
        /// <param name="length">
        /// An optional length of the vector, which must be less than or equal to the length of <paramref name="buffer"/>.
        /// </param>
        public DenseVector(Memory<T> buffer, int? length = null)
        {
            Contracts.CheckParam(!length.HasValue || length.Value <= buffer.Length, nameof(length));

            Length = length ?? buffer.Length;
            Buffer = buffer;
        }
    }
}
