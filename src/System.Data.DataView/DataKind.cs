// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text;

namespace Microsoft.ML.Runtime.Data
{
    /// <summary>
    /// Data type specifier.
    /// </summary>
    public enum DataKind : byte
    {
        // Notes:
        // * These values are serialized, so changing them breaks binary formats.
        // * We intentionally skip zero.
        // * Some code depends on sizeof(DataKind) == sizeof(byte).

        I1 = 1,
        U1 = 2,
        I2 = 3,
        U2 = 4,
        I4 = 5,
        U4 = 6,
        I8 = 7,
        U8 = 8,
        R4 = 9,
        R8 = 10,
        Num = R4,

        TX = 11,
#pragma warning disable TLC_GeneralName // The data kind enum has its own logic, independnet of C# naming conventions.
        TXT = TX,
        Text = TX,

        BL = 12,
        Bool = BL,

        TS = 13,
        TimeSpan = TS,
        DT = 14,
        DateTime = DT,
        DZ = 15,
        DateTimeZone = DZ,

        UG = 16, // Unsigned 16-byte integer.
        U16 = UG,
#pragma warning restore TLC_GeneralName
    }

}