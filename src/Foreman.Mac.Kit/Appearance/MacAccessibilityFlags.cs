using System;
using System.Runtime.InteropServices;

namespace Foreman.Mac.Kit.Appearance {
    //Reduce Motion/Transparency have no Avalonia surface; read via objc against NSWorkspace, false off macOS.
    public static class MacAccessibilityFlags {
        private const string ObjCLibrary = "/usr/lib/libobjc.dylib";

        public static bool ReduceMotion => OperatingSystem.IsMacOS() && Query("accessibilityDisplayShouldReduceMotion");

        public static bool ReduceTransparency => OperatingSystem.IsMacOS() && Query("accessibilityDisplayShouldReduceTransparency");

        private static bool Query(string selectorName) {
            IntPtr workspaceClass = objc_getClass("NSWorkspace");
            IntPtr sharedWorkspace = objc_msgSend_IntPtr(workspaceClass, sel_registerName("sharedWorkspace"));
            return objc_msgSend_Bool(sharedWorkspace, sel_registerName(selectorName));
        }

        [DllImport(ObjCLibrary, EntryPoint = "objc_getClass", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLibrary, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool objc_msgSend_Bool(IntPtr receiver, IntPtr selector);
    }
}
