#if UNITY_EDITOR
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace EpicTransport
{
    //TODO: pull in EOSManager logger level
    //TODO: call Free() on Editor exit, just in case
    public class EditorSDKLoadHelper : IDisposable
    {
        public IntPtr LibraryPointer { get; private set; } = IntPtr.Zero;
        private const string INT_PTR_STORAGE_KEY = "EOSSDK Library Pointer";

        private static Func<IntPtr, string, IntPtr> ProcAddressFunction
        {
            get
            {
#if UNITY_EDITOR_WIN
                return GetProcAddress;
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                return UnixGetProcAddress;
#endif
            }
        }

        public bool Load()
        {
            if (LibraryPointer != IntPtr.Zero) return true;
            string input = SessionState.GetString(INT_PTR_STORAGE_KEY, string.Empty);

            if (!string.IsNullOrWhiteSpace(input) && long.TryParse(input, out long val))
            {
                Debug.Log($"Hooking SDK that's already loaded in memory. Pointer: (IntPtr){val}");
                LibraryPointer = new IntPtr(val);

                Bindings.Hook(LibraryPointer, ProcAddressFunction);
                return true;
            }

            string[] libs = Directory.GetFiles(Application.dataPath, Epic.OnlineServices.Common.LIBRARY_NAME + "*", SearchOption.AllDirectories);
            if (!libs.Any())
            {
                Debug.LogError($"Failed to find EOS library '{Epic.OnlineServices.Common.LIBRARY_NAME}' in {Application.dataPath}");
                return false;
            }

            string libraryName = libs[0].Replace("\\", "/");
            //Debug.Log(libraryName);

            LibraryPointer = LoadEOS(libraryName);
            if (LibraryPointer == IntPtr.Zero)
            {
                Debug.LogError($"Failed to load EOS library '{libraryName}'. Reason: {GetNativeError()}");
                return false;
            }
            
            Debug.Log($"Hooking SDK. Pointer: (IntPtr){LibraryPointer}");
            Bindings.Hook(LibraryPointer, ProcAddressFunction);

            SessionState.SetString(INT_PTR_STORAGE_KEY, LibraryPointer.ToString());
            return true;
        }


        public bool Free()
        {
            if (LibraryPointer == IntPtr.Zero) return true;
            PlatformInterface.Shutdown();

            bool success = FreeEOS();
            if (!success)
            {
                Debug.LogError($"Failed to unload EOS. Reason: {GetNativeError()}");
                return false;
            }

            Bindings.Unhook();

            Dispose();

            Debug.Log("EOSSDK freed");
            return true;
        }

        public void Dispose()
        {
            LibraryPointer = IntPtr.Zero;
        }

        private string GetNativeError()
        {
#if UNITY_EDITOR_WIN
            int errorCode = Marshal.GetLastWin32Error();
            return new Win32Exception(errorCode).Message;
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            IntPtr errPtr = dlerror();
            return errPtr == IntPtr.Zero ? "Unknown Unix Error" : Marshal.PtrToStringAnsi(errPtr);
#else
            return "Unsupported Platform";
#endif
        }

#if UNITY_EDITOR_WIN
        private IntPtr LoadEOS(string libraryName)
        {
            return LoadLibrary(libraryName);
        }

        private bool FreeEOS()
        {
            //NOTE: FreeLibrary returns non-zero on success
            return FreeLibrary(LibraryPointer) != 0;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

#elif UNITY_EDITOR_OSX
        private IntPtr LoadEOS(string libraryName)
        { 
            return dlopen(libraryName, 10); 
        }

        private static IntPtr UnixGetProcAddress(IntPtr handle, string symbol)
        {
            //try EOS's func name
            IntPtr ptr = dlsym(handle, symbol);
            if (ptr != IntPtr.Zero) return ptr;

            //if it fails, strip the '_' before it and try again
            if (symbol.StartsWith("_"))
            {
                ptr = dlsym(handle, symbol.Substring(1));
                if (ptr != IntPtr.Zero) return ptr;
            }
            
            //if fails again, add a '_'
            if (!symbol.StartsWith("_"))
                ptr = dlsym(handle, "_" + symbol);

            return ptr;
        }

        private bool FreeEOS()
        {
            //NOTE: dlclose returns 0 on success
            return dlclose(LibraryPointer) == 0;
        }

        [DllImport("libdl", SetLastError = true)]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl", SetLastError = true)]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libdl", SetLastError = true)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl")]
        private static extern IntPtr dlerror();

#elif UNITY_EDITOR_LINUX
        private IntPtr LoadEOS(string libraryName)
        { 
            return dlopen(libraryName, 10); 
        }

        private static IntPtr UnixGetProcAddress(IntPtr handle, string symbol)
        {
            //try EOS's func name
            IntPtr ptr = dlsym(handle, symbol);
            if (ptr != IntPtr.Zero) return ptr;

            //if it fails, strip the '_' before it and try again
            if (symbol.StartsWith("_"))
            {
                ptr = dlsym(handle, symbol.Substring(1));
                if (ptr != IntPtr.Zero) return ptr;
            }
            
            //if fails again, add a '_'
            if (!symbol.StartsWith("_"))
                ptr = dlsym(handle, "_" + symbol);

            return ptr;
        }

        private bool FreeEOS()
        {
            //NOTE: dlclose returns 0 on success
            return dlclose(LibraryPointer) == 0;
        }

        [DllImport("libdl.so.2", SetLastError = true)]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.so.2", SetLastError = true)]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libdl.so.2", SetLastError = true)]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.so.2")]
        private static extern IntPtr dlerror();
        
#else
        private IntPtr LoadEOS(string libraryName, out Func<IntPtr, string, IntPtr> GetPtrFunc)
        {
            throw new PlatformNotSupportedException("Current edtior platform not supported for EOSSDK dynamic library loading.");
        }

        private bool FreeEOS() => throw new PlatformNotSupportedException();
#endif
    }
}
#endif