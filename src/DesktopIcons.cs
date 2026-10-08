using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace KitsuDesktop {
    // Restricted to Explorer's desktop list view. Only GETITEMCOUNT, GETITEMPOSITION
    // and SETITEMPOSITION are sent: never open, execute, rename or delete an item.
    sealed class DesktopIcons : IDisposable {
        delegate bool EnumProc(IntPtr window,IntPtr param);
        [StructLayout(LayoutKind.Sequential)] struct XY { public int X,Y; }
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref XY point);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window,ref XY point);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint allocation,uint protect);
        [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint type);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] data,int size,out IntPtr read);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        IntPtr view,process,memory;
        int index=-1,count;
        Point original,last;
        bool moved;
        public bool Restore=true;
        public Point ScreenPosition { get; private set; }
        public static bool DesktopIsForeground() {
            StringBuilder name=new StringBuilder(128); GetClassName(GetForegroundWindow(),name,name.Capacity);
            return name.ToString()=="Progman" || name.ToString()=="WorkerW" || name.ToString()=="SHELLDLL_DefView";
        }
        static IntPtr FindDesktop() {
            IntPtr found=IntPtr.Zero;
            EnumWindows(delegate(IntPtr w,IntPtr p) {
                IntPtr def=FindWindowEx(w,IntPtr.Zero,"SHELLDLL_DefView",null);
                if(def==IntPtr.Zero) return true;
                found=FindWindowEx(def,IntPtr.Zero,"SysListView32","FolderView");
                return found==IntPtr.Zero;
            },IntPtr.Zero);
            return found;
        }
        bool Send(uint msg,IntPtr wp,IntPtr lp,out IntPtr result) {
            result=IntPtr.Zero;
            return view!=IntPtr.Zero && SendMessageTimeout(view,msg,wp,lp,2,80,out result)!=IntPtr.Zero;
        }
        int Count() { IntPtr result; return Send(0x1004,IntPtr.Zero,IntPtr.Zero,out result) ? result.ToInt32() : -1; }
        bool Read(int item,out Point position) {
            position=Point.Empty; IntPtr result,read;
            if(!Send(0x1010,new IntPtr(item),memory,out result) || result==IntPtr.Zero) return false;
            byte[] bytes=new byte[8];
            if(!ReadProcessMemory(process,memory,bytes,bytes.Length,out read) || read.ToInt64()!=8) return false;
            position=new Point(BitConverter.ToInt32(bytes,0),BitConverter.ToInt32(bytes,4)); return true;
        }
        public static DesktopIcons Begin(Random random,Rectangle workArea,out string reason) {
            reason=""; DesktopIcons session=new DesktopIcons();
            session.view=FindDesktop();
            if(session.view==IntPtr.Zero || !IsWindowVisible(session.view)) { reason="Не вижу значков рабочего стола"; session.Dispose(); return null; }
            int style=GetWindowLong(session.view,-16);
            if((style&0x100)!=0) { reason="В Windows включено автоупорядочивание"; session.Dispose(); return null; }
            if((style&3)!=0 && (style&3)!=2) { reason="Этот вид значков не поддерживается"; session.Dispose(); return null; }
            uint pid; GetWindowThreadProcessId(session.view,out pid);
            // The remote buffer is exclusively an output POINT for GETITEMPOSITION.
            session.process=OpenProcess(0x18,false,pid);
            if(session.process==IntPtr.Zero) { reason="Windows не разрешила доступ к значкам"; session.Dispose(); return null; }
            session.memory=VirtualAllocEx(session.process,IntPtr.Zero,new UIntPtr(8),0x3000,4);
            if(session.memory==IntPtr.Zero) { reason="Не удалось прочитать расположение значков"; session.Dispose(); return null; }
            session.count=session.Count();
            if(session.count<=0) { reason="На рабочем столе нет значков"; session.Dispose(); return null; }
            int first=random.Next(session.count);
            for(int offset=0;offset<session.count;offset++) {
                int candidate=(first+offset)%session.count; Point position;
                if(!session.Read(candidate,out position)) continue;
                XY screen=new XY { X=position.X,Y=position.Y }; ClientToScreen(session.view,ref screen);
                if(!workArea.Contains(screen.X+24,screen.Y+24)) continue;
                session.index=candidate; session.original=session.last=position;
                session.ScreenPosition=new Point(screen.X+24,screen.Y+24); return session;
            }
            reason="Нет доступных значков на этом мониторе"; session.Dispose(); return null;
        }
        public bool Move(Point screen) {
            Point current;
            // Abort if Explorer refreshed or the person moved the same icon themselves.
            if(!IsWindow(view) || (GetWindowLong(view,-16)&0x100)!=0 || Count()!=count || !Read(index,out current) || current!=last) return false;
            XY p=new XY { X=screen.X-24,Y=screen.Y-24 }; ScreenToClient(view,ref p);
            if(p.X<short.MinValue || p.X>short.MaxValue || p.Y<short.MinValue || p.Y>short.MaxValue) return false;
            IntPtr result; int packed=(p.X&0xffff)|((p.Y&0xffff)<<16);
            if(!Send(0x100F,new IntPtr(index),new IntPtr(packed),out result) || result==IntPtr.Zero) return false;
            // Alignment to grid may adjust the requested coordinates, so read back.
            if(!Read(index,out last)) return false;
            moved=true; ScreenPosition=screen; return true;
        }
        public void Dispose() {
            if(moved && Restore && IsWindow(view) && (GetWindowLong(view,-16)&0x100)==0 && Count()==count) {
                Point current;
                if(Read(index,out current) && current==last) {
                    IntPtr ignored; int packed=(original.X&0xffff)|((original.Y&0xffff)<<16);
                    Send(0x100F,new IntPtr(index),new IntPtr(packed),out ignored);
                }
            }
            if(memory!=IntPtr.Zero) { VirtualFreeEx(process,memory,UIntPtr.Zero,0x8000); memory=IntPtr.Zero; }
            if(process!=IntPtr.Zero) { CloseHandle(process); process=IntPtr.Zero; }
            view=IntPtr.Zero; moved=false;
        }
    }
}
