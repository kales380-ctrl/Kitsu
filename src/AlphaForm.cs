using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ComponentModel;

namespace KitsuDesktop {
    class AlphaForm : Form {
        [StructLayout(LayoutKind.Sequential)] struct XY { public int X,Y; public XY(int x,int y) { X=x;Y=y; } }
        [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend { public byte Op,Flags,Alpha,Format; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr window,IntPtr dst,ref XY pos,ref XY size,IntPtr src,ref XY origin,uint key,ref Blend blend,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        // Furniture can rise above the dog when Windows activates or moves it.
        // Keep the actual layered windows in the same order as our artwork,
        // without activating the dog or moving unrelated application windows.
        public void KeepBelow(Form foreground) {
            if(!Visible || !IsHandleCreated || IsDisposed || foreground==null || !foreground.Visible || !foreground.IsHandleCreated || foreground.IsDisposed) return;
            for(IntPtr above=GetWindow(Handle,3);above!=IntPtr.Zero;above=GetWindow(above,3))
                if(above==foreground.Handle) return;
            if(!SetWindowPos(Handle,foreground.Handle,0,0,0,0,0x0013)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08080080; return p; } }
        protected void Present(Bitmap bitmap) {
            if(!IsHandleCreated || IsDisposed) return;
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),handle=IntPtr.Zero,previous=IntPtr.Zero;
            try {
                handle=bitmap.GetHbitmap(Color.FromArgb(0)); previous=SelectObject(memory,handle);
                XY pos=new XY(Left,Top),size=new XY(bitmap.Width,bitmap.Height),origin=new XY(0,0);
                Blend blend=new Blend { Alpha=255,Format=1 };
                if(!UpdateLayeredWindow(Handle,screen,ref pos,ref size,memory,ref origin,0,ref blend,2)) throw new Win32Exception(Marshal.GetLastWin32Error());
            } finally {
                if(previous!=IntPtr.Zero) SelectObject(memory,previous);
                if(handle!=IntPtr.Zero) DeleteObject(handle);
                DeleteDC(memory); ReleaseDC(IntPtr.Zero,screen);
            }
        }
    }
}
