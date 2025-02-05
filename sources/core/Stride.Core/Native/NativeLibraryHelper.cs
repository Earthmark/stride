// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Stride.Core
{
    public static class NativeLibraryHelper
    {
        private const string UNIX_LIB_PREFIX = "lib";
        private static readonly Dictionary<string, IntPtr> LoadedLibraries = new Dictionary<string, IntPtr>();

        /// <summary>
        /// Try to preload the library.
        /// This is useful when we want to have AnyCPU .NET and CPU-specific native code.
        /// Only available on Windows for now.
        /// </summary>
        /// <param name="libraryName">Name of the library, without the extension.</param>
        /// <param name="owner">Type whose assembly location is related to the native library (we can't use GetCallingAssembly as it might be wrong due to optimizations).</param>
        /// <exception cref="System.InvalidOperationException">Library could not be loaded.</exception>
        public static void PreloadLibrary(string libraryName, Type owner)
        {
#if STRIDE_PLATFORM_DESKTOP
            lock (LoadedLibraries)
            {
                // If already loaded, just exit as we want to load it just once
                if (LoadedLibraries.ContainsKey(libraryName))
                {
                    return;
                }

                string cpu;
                string platform;
                string extension;

                switch (RuntimeInformation.ProcessArchitecture)
                {
                    case Architecture.X86:
                        cpu = "x86";
                        break;
                    case Architecture.X64:
                        cpu = "x64";
                        break;
                    case Architecture.Arm:
                        cpu = "ARM";
                        break;
                    default:
                        throw new PlatformNotSupportedException();
                }

                switch (Platform.Type)
                {
                    case PlatformType.Windows:
                        platform = "win";
                        break;
                    case PlatformType.Linux:
                        platform = "linux";
                        break;
                    case PlatformType.macOS:
                        platform = "osx";
                        break;
                    default:
                        throw new PlatformNotSupportedException();
                }

                switch (Platform.Type)
                {
                    case PlatformType.Windows:
                        extension = ".dll";
                        break;
                    case PlatformType.Linux:
                        extension = ".so";
                        break;
                    case PlatformType.macOS:
                        extension = ".dylib";
                        break;
                    default:
                        throw new PlatformNotSupportedException();
                }

                var libraryNameWithExtension = libraryName + extension;

                if (Platform.Type != PlatformType.Windows)
                {
                    // on linux/macos opening a library without a path will look it up in the global library locations
                    // e.g. /lib/x86_64-linux-gnu, /lib, /usr/lib, etc. 
                    if (NativeLibrary.TryLoad(libraryNameWithExtension, out var result))
                    {
                        LoadedLibraries.Add(libraryName.ToLowerInvariant(), result);
                        return;
                    }
                    else if (!libraryName.StartsWith(UNIX_LIB_PREFIX)
                        && NativeLibrary.TryLoad(UNIX_LIB_PREFIX + libraryNameWithExtension, out result))
                    {
                        LoadedLibraries.Add(libraryName.ToLowerInvariant(), result);
                        return;
                    }
                }

                // We are trying to load the dll from a shadow path if it is already registered, otherwise we use it directly from the folder
                {
                    var platformNativeLibsFolder = $"{platform}-{cpu}";
                    foreach (var libraryPath in new[]
                    {
                        Path.Combine(Path.GetDirectoryName(owner.GetTypeInfo().Assembly.Location) ?? string.Empty, platformNativeLibsFolder),
                        Path.Combine(Environment.CurrentDirectory ?? string.Empty, platformNativeLibsFolder),
                        Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) ?? string.Empty, platformNativeLibsFolder),
                        // Also try without platform for Windows-only packages (backward compat for editor packages)
                        Path.Combine(Path.GetDirectoryName(owner.GetTypeInfo().Assembly.Location) ?? string.Empty, cpu),
                        Path.Combine(Environment.CurrentDirectory ?? string.Empty, cpu),
                    })
                    {
                        var libraryFilename = Path.Combine(libraryPath, libraryNameWithExtension);
                        if (NativeLibrary.TryLoad(libraryFilename, out var result))
                        {
                            LoadedLibraries.Add(libraryName.ToLowerInvariant(), result);
                            return;
                        }
                    }
                }

                // Attempt to load it from PATH
                foreach (var p in Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator))
                {
                    var libraryFilename = Path.Combine(p, libraryNameWithExtension);
                    if (NativeLibrary.TryLoad(libraryFilename, out var result))
                    {
                        LoadedLibraries.Add(libraryName.ToLowerInvariant(), result);
                        return;
                    }
                }

                throw new InvalidOperationException($"Could not load native library {libraryName} using CPU architecture {cpu}.");
            }
#endif
        }

        /// <summary>
        /// UnLoad a specific native dynamic library loaded previously by <see cref="LoadLibrary" />.
        /// </summary>
        /// <param name="libraryName">Name of the library to unload.</param>
        public static void UnLoad(string libraryName)
        {
#if STRIDE_PLATFORM_DESKTOP
            lock (LoadedLibraries)
            {
                IntPtr libHandle;
                if (LoadedLibraries.TryGetValue(libraryName, out libHandle))
                {
                    NativeLibrary.Free(libHandle);
                    LoadedLibraries.Remove(libraryName);
                }
            }
#endif
        }

        /// <summary>
        /// UnLoad all native dynamic library loaded previously by <see cref="LoadLibrary"/>.
        /// </summary>
        public static void UnLoadAll()
        {
#if STRIDE_PLATFORM_DESKTOP
            lock (LoadedLibraries)
            {
                foreach (var libraryItem in LoadedLibraries)
                {
                    NativeLibrary.Free(libraryItem.Value);
                }
                LoadedLibraries.Clear();
            }
#endif
        }
    }
}
