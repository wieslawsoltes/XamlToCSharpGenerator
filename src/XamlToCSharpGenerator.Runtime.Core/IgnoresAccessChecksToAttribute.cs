// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;

namespace System.Runtime.CompilerServices;

/// <exclude />
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute : Attribute
{
    public IgnoresAccessChecksToAttribute(string assemblyName)
    {
        AssemblyName = assemblyName;
    }

    public string AssemblyName { get; }
}
