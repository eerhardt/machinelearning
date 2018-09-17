// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.ML.Runtime.Internal.Utilities;
using Microsoft.ML.Runtime.CommandLine;

// REVIEW: Determine ideal namespace.
namespace Microsoft.ML.Runtime
{
    /// <summary>
    /// This catalogs instantiatable components (aka, loadable classes). Components are registered via
    /// a descendant of <see cref="LoadableClassAttributeBase"/>, identifying the names and signature types under which the component
    /// type should be registered. Signatures are delegate types that return void and specify that parameter
    /// types for component instantiation. Each component may also specify an "arguments object" that should
    /// be provided at instantiation time.
    /// </summary>
    public static class ComponentCatalog
    {
        /// <summary>
        /// Provides information on an instantiatable component, aka, loadable class.
        /// </summary>
        public sealed class LoadableClassInfo
        {
            /// <summary>
            /// Used for dictionary lookup based on signature and name.
            /// </summary>
            internal struct Key : IEquatable<Key>
            {
                public readonly string Name;
                public readonly Type Signature;

                public Key(string name, Type sig)
                {
                    Name = name;
                    Signature = sig;
                }

                public override int GetHashCode()
                {
                    return Hashing.CombinedHash(Name.GetHashCode(), Signature.GetHashCode());
                }

                public override bool Equals(object obj)
                {
                    return obj is Key && Equals((Key)obj);
                }

                public bool Equals(Key other)
                {
                    return other.Name == Name && other.Signature == Signature;
                }
            }

            /// <summary>
            /// Count of component construction arguments, NOT including the arguments object (if there is one).
            /// This matches the number of arguments for the signature type delegate(s).
            /// </summary>
            internal int ExtraArgCount => ArgType == null ? CtorTypes.Length : CtorTypes.Length - 1;

            public Type Type { get; }

            /// <summary>
            /// The type that contains the construction method, whether static Instance property,
            /// static Create method, or constructor.
            /// </summary>
            public Type LoaderType { get; }

            public IReadOnlyList<Type> SignatureTypes { get; }

            /// <summary>
            /// Summary of the component.
            /// </summary>
            public string Summary { get; }

            /// <summary>
            /// UserName may be null or empty, indicating that it should be hidden in UI.
            /// </summary>
            public string UserName { get; }

            /// <summary>
            /// Whether this is a "hidden" component, that generally shouldn't be displayed
            /// to users.
            /// </summary>
            public bool IsHidden => string.IsNullOrWhiteSpace(UserName);

            /// <summary>
            /// All load names. The first is the default.
            /// </summary>
            public IReadOnlyList<string> LoadNames { get; }

            /// <summary>
            /// The static property that returns an instance of this loadable class.
            /// This creation method does not support an arguments class.
            /// Only one of Ctor, Create and InstanceGetter can be non-null.
            /// </summary>
            public MethodInfo InstanceGetter { get; }

            /// <summary>
            /// The constructor to create an instance of this loadable class.
            /// This creation method supports an arguments class.
            /// Only one of Ctor, Create and InstanceGetter can be non-null.
            /// </summary>
            public ConstructorInfo Constructor { get; }

            /// <summary>
            /// The static method that creates an instance of this loadable class.
            /// This creation method supports an arguments class.
            /// Only one of Ctor, Create and InstanceGetter can be non-null.
            /// </summary>
            public MethodInfo CreateMethod { get; }

            public bool RequireEnvironment { get; }

            /// <summary>
            /// A name of an embedded resource containing documentation for this
            /// loadable class. This is non-null only in the event that we have
            /// verified the assembly of <see cref="LoaderType"/> actually contains
            /// this resource.
            /// </summary>
            public string DocName { get; }

            /// <summary>
            /// The type that contains the arguments to the component.
            /// </summary>
            public Type ArgType { get; }

            private Type[] CtorTypes { get; }

            internal LoadableClassInfo(LoadableClassAttributeBase attr, MethodInfo getter, ConstructorInfo ctor, MethodInfo create, bool requireEnvironment)
            {
                Contracts.AssertValue(attr);
                Contracts.AssertValue(attr.InstanceType);
                Contracts.AssertValue(attr.LoaderType);
                Contracts.AssertValueOrNull(attr.Summary);
                Contracts.AssertValueOrNull(attr.DocName);
                Contracts.AssertValueOrNull(attr.UserName);
                Contracts.AssertNonEmpty(attr.LoadNames);
                Contracts.Assert(getter == null || Utils.Size(attr.CtorTypes) == 0);

                Type = attr.InstanceType;
                LoaderType = attr.LoaderType;
                Summary = attr.Summary;
                UserName = attr.UserName;
                LoadNames = attr.LoadNames.AsReadOnly();

                if (getter != null)
                    InstanceGetter = getter;
                else if (ctor != null)
                    Constructor = ctor;
                else if (create != null)
                    CreateMethod = create;
                ArgType = attr.ArgType;
                SignatureTypes = attr.SigTypes.AsReadOnly();
                CtorTypes = attr.CtorTypes ?? Type.EmptyTypes;
                RequireEnvironment = requireEnvironment;

                if (!string.IsNullOrWhiteSpace(attr.DocName))
                    DocName = attr.DocName;

                Contracts.Assert(ArgType == null || CtorTypes.Length > 0 && CtorTypes[0] == ArgType);
            }

            internal object CreateInstanceCore(object[] ctorArgs)
            {
                Contracts.Assert(Utils.Size(ctorArgs) == CtorTypes.Length + ((RequireEnvironment) ? 1 : 0));

                if (InstanceGetter != null)
                {
                    Contracts.Assert(Utils.Size(ctorArgs) == 0);
                    return InstanceGetter.Invoke(null, null);
                }
                if (Constructor != null)
                    return Constructor.Invoke(ctorArgs);
                if (CreateMethod != null)
                    return CreateMethod.Invoke(null, ctorArgs);
                throw Contracts.Except("Can't instantiate class '{0}'", Type.Name);
            }

            /// <summary>
            /// Create an instance, given the arguments object and arguments to the signature delegate.
            /// The args should be non-null iff ArgType is non-null. The length of the extra array should
            /// match the number of paramters for the signature delgate. When that number is zero, extra
            /// may be null.
            /// </summary>
            public object CreateInstance(IHostEnvironment env, object args, object[] extra)
            {
                Contracts.CheckValue(env, nameof(env));
                env.Check((ArgType != null) == (args != null));
                env.Check(Utils.Size(extra) == ExtraArgCount);

                List<object> prefix = new List<object>();
                if (RequireEnvironment)
                    prefix.Add(env);
                if (ArgType != null)
                    prefix.Add(args);
                var values = Utils.Concat(prefix.ToArray(), extra);
                return CreateInstanceCore(values);
            }

            /// <summary>
            /// Create an instance, given the arguments object and arguments to the signature delegate.
            /// The args should be non-null iff ArgType is non-null. The length of the extra array should
            /// match the number of paramters for the signature delgate. When that number is zero, extra
            /// may be null.
            /// </summary>
            public TRes CreateInstance<TRes>(IHostEnvironment env, object args, object[] extra)
            {
                if (!typeof(TRes).IsAssignableFrom(Type))
                    throw Contracts.Except("Loadable class '{0}' does not derive from '{1}'", LoadNames[0], typeof(TRes).FullName);
                return (TRes)CreateInstance(env, args, extra);
            }

            /// <summary>
            /// Create an instance with default arguments.
            /// </summary>
            public TRes CreateInstance<TRes>(IHostEnvironment env)
            {
                if (!typeof(TRes).IsAssignableFrom(Type))
                    throw Contracts.Except("Loadable class '{0}' does not derive from '{1}'", LoadNames[0], typeof(TRes).FullName);
                return (TRes)CreateInstance(env, CreateArguments(), null);
            }

            /// <summary>
            /// If <see cref="ArgType"/> is not null, returns a new default instance of <see cref="ArgType"/>.
            /// Otherwise, returns null.
            /// </summary>
            public object CreateArguments()
            {
                if (ArgType == null)
                    return null;

                var ctor = ArgType.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    throw Contracts.Except("Loadable class '{0}' has ArgType '{1}', which has no suitable constructor",
                        UserName, ArgType);
                }

                return ctor.Invoke(null);
            }
        }

        // Do not initialize this one - the initial null value is used as a "flag" to prime things.
        private static ConcurrentQueue<Assembly> _assemblyQueue;

        // The assemblies that are loaded by Reflection.LoadAssembly or Assembly.Load* after we started tracking
        // the load events. We will provide assembly resolving for these assemblies. This is created simultaneously
        // with s_assemblyQueue.
        private static ConcurrentDictionary<string, Assembly> _loadedAssemblies;

        // This lock protects s_cachedAssemblies and s_cachedPaths only. The collection of ClassInfos is concurrent
        // so needs no protection.
        private static object _lock = new object();
        private static HashSet<string> _cachedAssemblies = new HashSet<string>();
        private static HashSet<string> _cachedPaths = new HashSet<string>();

        // Map from key/name to loadable class. Note that the same ClassInfo may appear
        // multiple times. For the set of unique infos, use s_classes.
        private static ConcurrentDictionary<LoadableClassInfo.Key, LoadableClassInfo> _classesByKey = new ConcurrentDictionary<LoadableClassInfo.Key, LoadableClassInfo>();

        // The unique ClassInfos and Signatures.
        private static ConcurrentQueue<LoadableClassInfo> _classes = new ConcurrentQueue<LoadableClassInfo>();
        private static ConcurrentDictionary<Type, bool> _signatures = new ConcurrentDictionary<Type, bool>();

        /// <summary>
        /// This caches information for the loadable classes in loaded assemblies.
        /// </summary>
        private static void CacheLoadedAssemblies()
        {
            // The target assembly is the one containing LoadableClassAttributeBase. If an assembly doesn't reference
            // the target, then we don't want to scan its assembly attributes (there's no point in doing so).
            var target = typeof(LoadableClassAttributeBase).Assembly;

            lock (_lock)
            {
                if (_assemblyQueue == null)
                {
                    // Create the loaded assembly queue and dictionary, set up the AssemblyLoad / AssemblyResolve
                    // event handlers and populate the queue / dictionary with all assemblies that are currently loaded.
                    Contracts.Assert(_assemblyQueue == null);
                    Contracts.Assert(_loadedAssemblies == null);

                    _assemblyQueue = new ConcurrentQueue<Assembly>();
                    _loadedAssemblies = new ConcurrentDictionary<string, Assembly>();

                    AppDomain.CurrentDomain.AssemblyLoad += CurrentDomainAssemblyLoad;
                    AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainAssemblyResolve;

                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        // Ignore dynamic assemblies.
                        if (a.IsDynamic)
                            continue;

                        _assemblyQueue.Enqueue(a);
                        if (!_loadedAssemblies.TryAdd(a.FullName, a))
                        {
                            // Duplicate loading.
                            Console.Error.WriteLine("Duplicate loaded assembly '{0}'", a.FullName);
                        }
                    }
                }

                Contracts.AssertValue(_assemblyQueue);
                Contracts.AssertValue(_loadedAssemblies);

                Assembly assembly;
                while (_assemblyQueue.TryDequeue(out assembly))
                {
                    if (!_cachedAssemblies.Add(assembly.FullName))
                        continue;

                    if (assembly != target)
                    {
                        bool found = false;
                        var targetName = target.GetName();
                        foreach (var name in assembly.GetReferencedAssemblies())
                        {
                            if (name.Name == targetName.Name)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                            continue;
                    }

                    int added = 0;
                    foreach (LoadableClassAttributeBase attr in assembly.GetCustomAttributes(typeof(LoadableClassAttributeBase)))
                    {
                        MethodInfo getter = null;
                        ConstructorInfo ctor = null;
                        MethodInfo create = null;
                        bool requireEnvironment = false;
                        if (attr.InstanceType != typeof(void) && !TryGetIniters(attr.InstanceType, attr.LoaderType, attr.CtorTypes, out getter, out ctor, out create, out requireEnvironment))
                        {
                            Console.Error.WriteLine(
                                "CacheClassesFromAssembly: can't instantiate loadable class {0} with name {1}",
                                attr.InstanceType.Name, attr.LoadNames[0]);
                            Contracts.Assert(getter == null && ctor == null && create == null);
                        }
                        var info = new LoadableClassInfo(attr, getter, ctor, create, requireEnvironment);

                        AddClass(info, attr.LoadNames);
                        added++;
                    }
                }
            }
        }

        private static bool TryGetIniters(Type instType, Type loaderType, Type[] parmTypes,
            out MethodInfo getter, out ConstructorInfo ctor, out MethodInfo create, out bool requireEnvironment)
        {
            getter = null;
            ctor = null;
            create = null;
            requireEnvironment = false;
            var parmTypesWithEnv = Utils.Concat(new Type[1] { typeof(IHostEnvironment) }, parmTypes);
            if (Utils.Size(parmTypes) == 0 && (getter = FindInstanceGetter(instType, loaderType)) != null)
                return true;
            if (instType.IsAssignableFrom(loaderType) && (ctor = loaderType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parmTypes ?? Type.EmptyTypes, null)) != null)
                return true;
            if (instType.IsAssignableFrom(loaderType) && (ctor = loaderType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parmTypesWithEnv ?? Type.EmptyTypes, null)) != null)
            {
                requireEnvironment = true;
                return true;
            }
            if ((create = FindCreateMethod(instType, loaderType, parmTypes ?? Type.EmptyTypes)) != null)
                return true;
            if ((create = FindCreateMethod(instType, loaderType, parmTypesWithEnv ?? Type.EmptyTypes)) != null)
            {
                requireEnvironment = true;
                return true;
            }

            return false;
        }

        private static void AddClass(LoadableClassInfo info, string[] loadNames)
        {
            _classes.Enqueue(info);
            foreach (var sigType in info.SignatureTypes)
            {
                _signatures.TryAdd(sigType, true);

                foreach (var name in loadNames)
                {
                    string nameCi = name.ToLowerInvariant();

                    var key = new LoadableClassInfo.Key(nameCi, sigType);
                    if (!_classesByKey.TryAdd(key, info))
                    {
                        var infoCur = _classesByKey[key];
                        // REVIEW: Fix this message to reflect the signature....
                        Console.Error.WriteLine(
                            "CacheClassesFromAssembly: can't map name {0} to {1}, already mapped to {2}",
                            name, info.Type.Name, infoCur.Type.Name);
                    }
                }
            }
        }

        private static MethodInfo FindInstanceGetter(Type instType, Type loaderType)
        {
            // Look for a public static property named Instance of the correct type.
            var prop = loaderType.GetProperty("Instance", instType);
            if (prop == null)
                return null;
            if (prop.DeclaringType != loaderType)
                return null;
            var meth = prop.GetGetMethod(false);
            if (meth == null)
                return null;
            if (meth.ReturnType != instType)
                return null;
            if (!meth.IsPublic || !meth.IsStatic)
                return null;
            return meth;
        }

        private static MethodInfo FindCreateMethod(Type instType, Type loaderType, Type[] parmTypes)
        {
            var meth = loaderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy, null, parmTypes ?? Type.EmptyTypes, null);
            if (meth == null)
                return null;
            if (meth.DeclaringType != loaderType)
                return null;
            if (meth.ReturnType != instType)
                return null;
            if (!meth.IsStatic)
                return null;
            return meth;
        }

        private static void CurrentDomainAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            // Don't try to index dynamic generated assembly
            if (args.LoadedAssembly.IsDynamic)
                return;
            _assemblyQueue.Enqueue(args.LoadedAssembly);
            if (!_loadedAssemblies.TryAdd(args.LoadedAssembly.FullName, args.LoadedAssembly))
            {
                // Duplicate loading.
                Console.Error.WriteLine("Duplicate loading of the assembly '{0}'", args.LoadedAssembly.FullName);
            }
        }

        private static Assembly CurrentDomainAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // REVIEW: currently, the resolving happens on exact matches of the full name.
            // This has proved to work with the C# transform. We might need to change the resolving logic when the need arises.
            Assembly found;
            if (_loadedAssemblies.TryGetValue(args.Name, out found))
                return found;
            return null;
        }

        /// <summary>
        /// Given a fully qualified assembly name, load the assembly.
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (_loadedAssemblies == null || !_loadedAssemblies.ContainsKey(assembly.FullName))
            {
                CacheLoadedAssemblies();
            }
        }

        /// <summary>
        /// Return an array containing information for all instantiatable components.
        /// If provided, the given set of assemblies is loaded first.
        /// </summary>
        public static LoadableClassInfo[] GetAllClasses()
        {
            CacheLoadedAssemblies();

            return _classes.ToArray();
        }

        /// <summary>
        /// Return an array containing information for instantiatable components with the given
        /// signature and base type. If provided, the given set of assemblies is loaded first.
        /// </summary>
        public static LoadableClassInfo[] GetAllDerivedClasses(Type typeBase, Type typeSig)
        {
            Contracts.CheckValue(typeBase, nameof(typeBase));
            Contracts.CheckValueOrNull(typeSig);

            CacheLoadedAssemblies();

            // Apply the default.
            if (typeSig == null)
                typeSig = typeof(SignatureDefault);

            return _classes
                .Where(info => info.SignatureTypes.Contains(typeSig) && typeBase.IsAssignableFrom(info.Type))
                .ToArray();
        }

        /// <summary>
        /// Return an array containing all the known signature types. If provided, the given set of assemblies
        /// is loaded first.
        /// </summary>
        public static Type[] GetAllSignatureTypes()
        {
            CacheLoadedAssemblies();

            return _signatures.Select(kvp => kvp.Key).ToArray();
        }

        /// <summary>
        /// Returns a string name for a given signature type.
        /// </summary>
        public static string SignatureToString(Type sig)
        {
            Contracts.CheckValue(sig, nameof(sig));
            Contracts.CheckParam(sig.BaseType == typeof(MulticastDelegate), nameof(sig), "Must be a delegate type");
            string kind = sig.Name;
            if (kind.Length > "Signature".Length && kind.StartsWith("Signature"))
                kind = kind.Substring("Signature".Length);
            return kind;
        }

        private static LoadableClassInfo FindClassCore(LoadableClassInfo.Key key)
        {
            LoadableClassInfo info;
            if (_classesByKey.TryGetValue(key, out info))
                return info;

            CacheLoadedAssemblies();

            if (_classesByKey.TryGetValue(key, out info))
                return info;

            return null;
        }

        public static LoadableClassInfo[] FindLoadableClasses(string name)
        {
            name = name.ToLowerInvariant().Trim();

            CacheLoadedAssemblies();

            var res = _classes
                .Where(ci => ci.LoadNames.Select(n => n.ToLowerInvariant().Trim()).Contains(name))
                .ToArray();
            return res;
        }

        public static LoadableClassInfo[] FindLoadableClasses<TSig>()
        {
            CacheLoadedAssemblies();

            return _classes
                .Where(ci => ci.SignatureTypes.Contains(typeof(TSig)))
                .ToArray();
        }

        public static LoadableClassInfo[] FindLoadableClasses<TArgs, TSig>()
        {
            // REVIEW: this and above methods perform a linear search over all the loadable classes.
            // On 6/15/2015, TLC release build contained 431 of them, so adding extra lookups looks unnecessary at this time.
            CacheLoadedAssemblies();

            return _classes
                .Where(ci => ci.ArgType == typeof(TArgs) && ci.SignatureTypes.Contains(typeof(TSig)))
                .ToArray();
        }

        public static LoadableClassInfo GetLoadableClassInfo<TSig>(string loadName)
        {
            return GetLoadableClassInfo(loadName, typeof(TSig));
        }

        public static LoadableClassInfo GetLoadableClassInfo(string loadName, Type signatureType)
        {
            Contracts.CheckParam(signatureType.BaseType == typeof(MulticastDelegate), nameof(signatureType), "signatureType must be a delegate type");
            Contracts.CheckValueOrNull(loadName);
            loadName = (loadName ?? "").ToLowerInvariant().Trim();
            return FindClassCore(new LoadableClassInfo.Key(loadName, signatureType));
        }

        /// <summary>
        /// Create an instance of the indicated component with the given extra parameters.
        /// </summary>
        public static TRes CreateInstance<TRes>(IHostEnvironment env, Type signatureType, string name, string options, params object[] extra)
            where TRes : class
        {
            TRes result;
            if (TryCreateInstance(env, signatureType, out result, name, options, extra))
                return result;
            throw Contracts.Except("Unknown loadable class: {0}", name).MarkSensitive(MessageSensitivity.None);
        }

        /// <summary>
        /// Try to create an instance of the indicated component and settings with the given extra parameters.
        /// If there is no such component in the catalog, returns false. Any other error results in an exception.
        /// </summary>
        public static bool TryCreateInstance<TRes, TSig>(IHostEnvironment env, out TRes result, string name, string options, params object[] extra)
            where TRes : class
        {
            return TryCreateInstance<TRes>(env, typeof(TSig), out result, name, options, extra);
        }

        private static bool TryCreateInstance<TRes>(IHostEnvironment env, Type signatureType, out TRes result, string name, string options, params object[] extra)
            where TRes : class
        {
            Contracts.CheckValue(env, nameof(env));
            env.Check(signatureType.BaseType == typeof(MulticastDelegate));
            env.CheckValueOrNull(name);

            string nameLower = (name ?? "").ToLowerInvariant().Trim();
            LoadableClassInfo info = FindClassCore(new LoadableClassInfo.Key(nameLower, signatureType));
            if (info == null)
            {
                result = null;
                return false;
            }

            if (!typeof(TRes).IsAssignableFrom(info.Type))
                throw env.Except("Loadable class '{0}' does not derive from '{1}'", name, typeof(TRes).FullName);

            int carg = Utils.Size(extra);

            if (info.ExtraArgCount != carg)
            {
                throw env.Except(
                    "Wrong number of extra parameters for loadable class '{0}', need '{1}', given '{2}'",
                    name, info.ExtraArgCount, carg);
            }

            if (info.ArgType == null)
            {
                if (!string.IsNullOrEmpty(options))
                    throw env.Except("Loadable class '{0}' doesn't support settings", name);
                result = (TRes)info.CreateInstance(env, null, extra);
                return true;
            }

            object args = info.CreateArguments();
            if (args == null)
                throw Contracts.Except("Can't instantiate arguments object '{0}' for '{1}'", info.ArgType.Name, name);

            ParseArguments(env, args, options, name);
            result = (TRes)info.CreateInstance(env, args, extra);
            return true;
        }

        /// <summary>
        /// Parses arguments using CmdParser. If there's a problem, it throws an InvalidOperationException,
        /// with a message giving usage.
        /// </summary>
        /// <param name="env">The host environment</param>
        /// <param name="args">The argument object</param>
        /// <param name="settings">The settings string</param>
        /// <param name="name">The name is used for error reporting only</param>
        private static void ParseArguments(IHostEnvironment env, object args, string settings, string name)
        {
            Contracts.AssertValue(args);
            Contracts.AssertNonEmpty(name);

            if (string.IsNullOrWhiteSpace(settings))
                return;

            string errorMsg = null;
            try
            {
                string err = null;
                string helpText;
                if (!CmdParser.ParseArguments(env, settings, args, e => { err = err ?? e; }, out helpText))
                    errorMsg = err + Environment.NewLine + "Usage For '" + name + "':" + Environment.NewLine + helpText;
            }
            catch (Exception e)
            {
                Contracts.Assert(false);
                throw Contracts.Except(e, "Unexpected exception thrown while parsing: " + e.Message);
            }

            if (errorMsg != null)
                throw Contracts.Except(errorMsg);
        }
    }
}
