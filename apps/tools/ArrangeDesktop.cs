using System;
using System.Runtime.InteropServices;

// Turns on "Auto arrange icons" + "Align icons to grid" for the desktop and refreshes it,
// through the desktop's own shell view (the same setting as right-click > View).
static class ArrangeDesktop
{
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enter);
        void InsertMenusSB(IntPtr a, IntPtr b);
        void SetMenuSB(IntPtr a, IntPtr b, IntPtr c);
        void RemoveMenusSB(IntPtr a);
        void SetStatusTextSB(IntPtr a);
        void EnableModelessSB(bool a);
        void TranslateAcceleratorSB(IntPtr a, short b);
        void BrowseObject(IntPtr a, uint b);
        void GetViewStateStream(uint a, out IntPtr b);
        void GetControlWindow(uint a, out IntPtr b);
        void SendControlMsg(uint a, uint b, IntPtr c, IntPtr d, out IntPtr e);
        void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellView
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enter);
        [PreserveSig] int TranslateAccelerator(IntPtr msg);
        void EnableModeless(bool enable);
        void UIActivate(uint state);
        void Refresh();
    }

    [ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode(out uint mode);
        void SetCurrentViewMode(uint mode);
        void GetFolder(ref Guid riid, out IntPtr ppv);
        void Item(int i, out IntPtr pidl);
        void ItemCount(uint flags, out int count);
        void Items(uint flags, ref Guid riid, out IntPtr ppv);
        void GetSelectionMarkedItem(out int item);
        void GetFocusedItem(out int item);
        void GetItemPosition(IntPtr pidl, out long pt);
        void GetSpacing(out long pt);
        void GetDefaultSpacing(out long pt);
        void GetAutoArrange();
        void SelectItem(int item, uint flags);
        void SelectAndPositionItems(uint count, IntPtr pidls, IntPtr pts, uint flags);
        // IFolderView2
        void SetGroupBy(IntPtr key, bool asc);
        void GetGroupBy(IntPtr key, out bool asc);
        void SetViewProperty(IntPtr pidl, IntPtr key, IntPtr val);
        void GetViewProperty(IntPtr pidl, IntPtr key, IntPtr val);
        void SetTileViewProperties(IntPtr pidl, string list);
        void SetExtendedTileViewProperties(IntPtr pidl, string list);
        void SetText(int type, string text);
        void SetCurrentFolderFlags(uint mask, uint flags);
        void GetCurrentFolderFlags(out uint flags);
    }

    [STAThread]
    static int Main()
    {
        Type t = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        dynamic windows = Activator.CreateInstance(t);
        object loc = 0, root = null;
        int hwnd;
        // SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1
        object disp = windows.FindWindowSW(ref loc, ref root, 8, out hwnd, 1);
        var sp = (IServiceProvider)disp;
        Guid sidTop = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        Guid iidBrowser = typeof(IShellBrowser).GUID;
        object browserObj;
        sp.QueryService(ref sidTop, ref iidBrowser, out browserObj);
        var browser = (IShellBrowser)browserObj;
        object viewObj;
        browser.QueryActiveShellView(out viewObj);

        const uint FWF_AUTOARRANGE = 0x1, FWF_SNAPTOGRID = 0x4;
        var folderView = (IFolderView2)viewObj;
        folderView.SetCurrentFolderFlags(FWF_AUTOARRANGE | FWF_SNAPTOGRID, FWF_AUTOARRANGE | FWF_SNAPTOGRID);
        uint flags;
        folderView.GetCurrentFolderFlags(out flags);
        ((IShellView)viewObj).Refresh();
        Console.WriteLine("desktop flags = 0x" + flags.ToString("X"));
        return (flags & FWF_AUTOARRANGE) != 0 ? 0 : 1;
    }
}
