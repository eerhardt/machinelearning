// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Data.DataView
{
    /// <summary>
    /// The class that incrementally builds a <see cref="DataViewSchema.Annotations"/>.
    /// </summary>
    public sealed class MetadataBuilder
    {
        private readonly List<(string Name, DataViewType Type, Delegate Getter, DataViewSchema.Annotations Annotations)> _items;

        public MetadataBuilder()
        {
            _items = new List<(string Name, DataViewType Type, Delegate Getter, DataViewSchema.Annotations Annotations)>();
        }

        /// <summary>
        /// Add some columns from <paramref name="annotations"/> into our new annotations, by applying <paramref name="selector"/>
        /// to all the names.
        /// </summary>
        /// <param name="annotations">The annotations row to take values from.</param>
        /// <param name="selector">The predicate describing which annotations columns to keep.</param>
        public void Add(DataViewSchema.Annotations annotations, Func<string, bool> selector)
        {
            if (annotations == null)
                return;

            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            foreach (var column in annotations.Schema)
            {
                if (selector(column.Name))
                    _items.Add((column.Name, column.Type, annotations.GetGetterInternal(column.Index), column.Annotations));
            }
        }

        /// <summary>
        /// Add one annotation column, strongly-typed version.
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="name">The annotation name.</param>
        /// <param name="type">The annotation type.</param>
        /// <param name="getter">The getter delegate.</param>
        /// <param name="annotations">Annotations of the input column. Note that annotations on a annotations column is somewhat rare
        /// except for certain types (for example, slot names for a vector, key values for something of key type).</param>
        public void Add<TValue>(string name, DataViewType type, ValueGetter<TValue> getter, DataViewSchema.Annotations annotations = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (getter == null)
                throw new ArgumentNullException(nameof(getter));
            if (type.RawType != typeof(TValue))
                throw new ArgumentException($"{nameof(type)}.{nameof(type.RawType)} must be of type '{typeof(TValue).FullName}'.", nameof(type));

            _items.Add((name, type, getter, annotations));
        }

        /// <summary>
        /// Add one annotation column, weakly-typed version.
        /// </summary>
        /// <param name="name">The annotation name.</param>
        /// <param name="type">The annotation type.</param>
        /// <param name="getter">The getter delegate that provides the value. Note that the type of the getter is still checked
        /// inside this method.</param>
        /// <param name="annotations">Annotations of the input column. Note that annotations on an annotations column is somewhat rare
        /// except for certain types (for example, slot names for a vector, key values for something of key type).</param>
        public void Add(string name, DataViewType type, Delegate getter, DataViewSchema.Annotations annotations = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (getter == null)
                throw new ArgumentNullException(nameof(getter));

            Utils.MarshalActionInvoke(AddDelegate<int>, type.RawType, name, type, getter, annotations);
        }

        /// <summary>
        /// Add one annotation column for a primitive value type.
        /// </summary>
        /// <param name="name">The annotation name.</param>
        /// <param name="type">The annotation type.</param>
        /// <param name="value">The value of the annotation.</param>
        /// <param name="annotations">Annotations of the input column. Note that annotations on an annotations column is somewhat rare
        /// except for certain types (for example, slot names for a vector, key values for something of key type).</param>
        public void AddPrimitiveValue<TValue>(string name, PrimitiveDataViewType type, TValue value, DataViewSchema.Annotations annotations = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (type.RawType != typeof(TValue))
                throw new ArgumentException($"{nameof(type)}.{nameof(type.RawType)} must be of type '{typeof(TValue).FullName}'.", nameof(type));

            Add(name, type, (ref TValue dst) => dst = value, annotations);
        }

        /// <summary>
        /// Produce the annotations row that the builder has so far.
        /// Can be called multiple times.
        /// </summary>
        public DataViewSchema.Annotations GetAnnotations()
        {
            var builder = new SchemaBuilder();
            foreach (var item in _items)
                builder.AddColumn(item.Name, item.Type, item.Annotations);
            return new DataViewSchema.Annotations(builder.GetSchema(), _items.Select(x => x.Getter).ToArray());
        }

        private void AddDelegate<TValue>(string name, DataViewType type, Delegate getter, DataViewSchema.Annotations annotations)
        {
            Debug.Assert(!string.IsNullOrEmpty(name));
            Debug.Assert(type != null);
            Debug.Assert(getter != null);

            var typedGetter = getter as ValueGetter<TValue>;
            if (typedGetter == null)
                throw new ArgumentException($"{nameof(getter)} must be of type '{typeof(ValueGetter<TValue>).FullName}'", nameof(getter));
            _items.Add((name, type, typedGetter, annotations));
        }
    }
}
