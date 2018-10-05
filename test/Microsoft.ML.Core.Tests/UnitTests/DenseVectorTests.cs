// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Runtime.Data;
using Microsoft.ML.TestFramework;
using System;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.Core.Tests.UnitTests
{
    public class DenseVectorTests : BaseTestClass
    {
        public DenseVectorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void DenseVectorWorksWithGetter()
        {
            DenseVector<float> v = default;
            ValueGetter<DenseVector<float>> getter = GetVector;

            getter(ref v);

            Assert.Equal(8, v.Length);
            Assert.Equal(8, v.Values.Length);

            Assert.Equal(.4f, v.Values[0]);
            Assert.Equal(.5f, v.Values[1]);
            Assert.Equal(.55f, v.Values[2]);
            Assert.Equal(.2f, v.Values[3]);
            Assert.Equal(.35f, v.Values[4]);
            Assert.Equal(.12f, v.Values[5]);
            Assert.Equal(.42f, v.Values[6]);
            Assert.Equal(.42f, v.Values[7]);

            Assert.Equal(10, v.Buffer.Length);
        }

        private void GetVector(ref DenseVector<float> src)
        {
            if (src.Capacity < 10)
            {
                src = new DenseVector<float>(new float[10], 8);
            }

            var values = src.Buffer.Span;

            values[0] = .4f;
            values[1] = .5f;
            values[2] = .55f;
            values[3] = .2f;
            values[4] = .35f;
            values[5] = .12f;
            values[6] = .42f;
            values[7] = .42f;
            values[8] = .42f;
            values[9] = .42f;
        }

        [Fact]
        public void DenseVectorThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>("length", () => new DenseVector<float>(new float[1], 2));
        }
    }
}

