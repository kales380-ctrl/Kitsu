using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace KitsuDesktop {
 static class ToyWindowLifecycle {
  [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flag);
  [STAThread] static void Main() {
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   Rectangle offscreen=new Rectangle(-12000,-12000,1000,1000);
   using(ToyForm warmup=new ToyForm()) { warmup.Release(-11500,-11500,0,0,offscreen); warmup.Close(); } Application.DoEvents();
   int gdiBefore=GetGuiResources(Process.GetCurrentProcess().Handle,0),userBefore=GetGuiResources(Process.GetCurrentProcess().Handle,1);
   for(int test=0;test<120;test++) using(ToyForm toy=new ToyForm()) {
    toy.Kind=(ToyKind)(1+test%3); toy.Release(-11500,-11500,50,-30,offscreen);
    if(!toy.Visible || !toy.IsHandleCreated || toy.Held) throw new Exception("Toy window failed first release");
    for(int cycle=0;cycle<5;cycle++) {
     toy.Hold(-11500,-11500); if(toy.Visible || !toy.Held) throw new Exception("Held toy stayed visible");
     toy.Step(.1,offscreen); if(toy.X!=-11500 || toy.Y!=-11500) throw new Exception("Held window drifted");
     toy.Release(-11500,-11500,50,-30,offscreen); if(!toy.Visible || toy.Held) throw new Exception("Dropped toy did not reappear");
    }
    toy.Close(); if(!toy.IsDisposed) throw new Exception("Toy window remained after close");
   }
   GC.Collect(); GC.WaitForPendingFinalizers(); Application.DoEvents();
   int gdiAfter=GetGuiResources(Process.GetCurrentProcess().Handle,0),userAfter=GetGuiResources(Process.GetCurrentProcess().Handle,1);
   if(gdiAfter-gdiBefore>10 || userAfter-userBefore>10) throw new Exception("Toy window resource leak: GDI="+(gdiAfter-gdiBefore)+", USER="+(userAfter-userBefore));
   Console.WriteLine("PASS: 120 per-pixel toy windows, 600 hold/release cycles, close/dispose; GDI delta={0}, USER delta={1}.",gdiAfter-gdiBefore,userAfter-userBefore);
  }
 }
}