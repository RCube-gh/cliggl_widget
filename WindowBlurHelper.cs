using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FocusHudWpf;

internal static class WindowBlurHelper
{
    [DllImport("user32.dll")]
    internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    internal enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    internal enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    public static void EnableBlur(Window window)
    {
        var windowHelper = new WindowInteropHelper(window);
        var accent = new AccentPolicy();
        var accentStructSize = Marshal.SizeOf(accent);

        // Try Acrylic first (Win10 1803+)
        // Color: AABBGGRR. Let's use a very dark tint: 0x99000000 (Opacity 0x99, Black 0x000000)
        // If we want it to just blur whatever is behind without much tint, use low alpha like 0x01000000
        // BUT Acrylic needs some alpha/tint to work properly usually. 
        // Let's try 0xCC2B3044 (App color #2b3044 with CC alpha)
        // R=44, G=30, B=2b -> 0xCC2B3044 ?? No, int is usually AABBGGRR in this API? 
        // Or ABGR? Usually ABGR (0xAABBGGRR).
        // #2b3044 -> R=2b(43), G=30(48), B=44(68). 
        // Let's stick to a safe dark gray tint: 0x99202020. 
        // Wait, ACCENT_ENABLE_BLURBEHIND (3) is safer and provides standard blur. 
        // ACCENT_ENABLE_ACRYLICBLURBEHIND (4) gives that noisy texture.
        
        // Try Standard Blur (Glass)
        // ACCENT_ENABLE_BLURBEHIND = 3
        // This usually works better for generic transparency+blur without complex tuning.
        accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
        accent.AccentFlags = 0; // Standard
        // accent.GradientColor is ignored in this mode usually, or set to 0.
        accent.GradientColor = 0;

        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }
}
