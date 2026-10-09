using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace KitsuDesktop {
    static class MultipleToysTest {
        static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Get(object o,string name) { return o.GetType().GetField(name,Fields).GetValue(o); }
        static void Set(object o,string name,object value) { o.GetType().GetField(name,Fields).SetValue(o,value); }
        static void Call(object o,string name,params object[] args) {
            try { o.GetType().GetMethod(name,Fields).Invoke(o,args); } catch(TargetInvocationException e) { throw e.InnerException; }
        }
        static void Assert(bool pass,string why) { if(!pass) throw new Exception(why); }
        static ToolStripMenuItem Item(PetForm pet,string text) {
            foreach(ToolStripItem entry in ((ContextMenuStrip)Get(pet,"menu")).Items) if(entry.Text==text) return (ToolStripMenuItem)entry;
            throw new Exception("Menu item missing: "+text);
        }
        static List<ToyForm> Toys(PetForm pet) { return (List<ToyForm>)Get(pet,"toys"); }
        static void NativeWindows() {
            // The pet stays in smoke mode so this native-window check never
            // writes desktop positions or meal history, and never adds a tray
            // icon. Only its toy windows use the normal Show/Present path.
            using(PetForm pet=new PetForm(true)) {
                ((Timer)Get(pet,"timer")).Stop();
                ((ToolStripMenuItem)Get(pet,"iconItem")).Checked=false;
                pet.Show(); Application.DoEvents();
                foreach(ToyKind kind in new[] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) {
                    Call(pet,"ThrowToy",kind); ToyForm item=(ToyForm)Get(pet,"toy");
                    Set(item,"headless",false);
                    item.Release(item.X,item.Y,0,0,Screen.FromPoint(new Point((int)item.X,(int)item.Y)).WorkingArea);
                    Application.DoEvents();
                    foreach(ToyForm existing in Toys(pet)) Assert(existing.Visible && existing.IsHandleCreated && !existing.IsDisposed,"Selecting another toy hid/disposed a native window");
                }
                ToyForm[] all=Toys(pet).ToArray(); ToyForm boar=(ToyForm)Get(pet,"toy");
                Call(pet,"BeginToyAction",Mood.ToyCarry); Application.DoEvents();
                Assert(boar.Held && !boar.Visible,"Held toy remains as a separate foreground window");
                foreach(ToyForm existing in all) if(!ReferenceEquals(existing,boar)) Assert(existing.Visible && !existing.Held,"Holding one toy hid another toy");
                Call(pet,"ThrowToy",ToyKind.Ball); Application.DoEvents();
                Assert(!boar.Held && boar.Visible,"Switching failed to restore the held toy window");
                Assert(Toys(pet).Count==3,"Native selection created duplicate windows");
                foreach(ToyForm existing in all) Assert(existing.Visible && !existing.IsDisposed,"Native switching removed an earlier toy");
                Call(pet,"BeginToyAction",Mood.ToyChew); ToyForm ball=(ToyForm)Get(pet,"toy");
                Call(pet,"StopToyPlay"); Application.DoEvents();
                Assert(!ball.Held && ball.Visible,"Ending play left the toy window hidden");
                Item(pet,"Убрать все игрушки").PerformClick(); Application.DoEvents();
                foreach(ToyForm existing in all) Assert(existing.IsDisposed,"Remove-all left a native toy window alive");
                Assert(Toys(pet).Count==0 && Get(pet,"toy")==null,"Native remove-all left selected or retained toys");
                pet.Close();
            }
        }
        [STAThread] static void Main() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(PetForm pet=new PetForm(true)) {
                ((Timer)Get(pet,"timer")).Stop();
                Dictionary<ToyKind,ToyForm> originals=new Dictionary<ToyKind,ToyForm>();
                foreach(ToyKind kind in new[] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) {
                    Call(pet,"ThrowToy",kind); originals[kind]=(ToyForm)Get(pet,"toy");
                    Assert(Toys(pet).Count==originals.Count,"Selecting another kind removed a toy");
                    foreach(ToyForm item in originals.Values) Assert(!item.IsDisposed,"Selecting another kind disposed earlier window");
                }
                ToyForm ball=originals[ToyKind.Ball],bone=originals[ToyKind.Bone],boar=originals[ToyKind.Boar];
                for(int repeat=0;repeat<30;repeat++) foreach(ToyKind kind in originals.Keys) {
                    Call(pet,"ThrowToy",kind);
                    Assert(Toys(pet).Count==3 && ReferenceEquals(Get(pet,"toy"),originals[kind]),"Repeated selection created another instance");
                }
                Call(pet,"BeginToyAction",Mood.ToyCarry); Assert(boar.Held,"Active boar was not held");
                ball.Released();
                Assert(ReferenceEquals(Get(pet,"toy"),ball),"An older toy's release fetched the current toy instead");
                Assert(!boar.Held && !boar.IsDisposed,"Switching by drag release lost the held boar");
                Assert((Mood)Get(pet,"mood")==Mood.Play && (double)Get(pet,"toyPlayUntil")>0,"Released older toy did not start fetch");
                Call(pet,"BeginToyAction",Mood.ToyChew);
                Call(pet,"ThrowToy",ToyKind.Bone);
                Assert(!ball.Held && !ball.IsDisposed && ReferenceEquals(Get(pet,"toy"),bone),"Selecting a toy during chewing lost the previous toy");
                Call(pet,"BeginToyAction",Mood.ToyShake);
                Item(pet,"Следовать за мышкой").PerformClick();
                Assert((Mood)Get(pet,"mood")==Mood.Follow && !bone.Held && Toys(pet).Count==3,"Following removed or hid a toy");
                Item(pet,"Следовать за мышкой").PerformClick();
                Call(pet,"ThrowToy",ToyKind.Boar); Call(pet,"BeginToyAction",Mood.ToyCarry);
                Item(pet,"На другой монитор").PerformClick();
                Assert(!boar.Held && Toys(pet).Count==3,"Changing monitor removed or hid a toy");
                foreach(ToyForm item in Toys(pet)) {
                    Rectangle work=Screen.FromPoint(new Point((int)item.X,(int)item.Y)).WorkingArea;
                    item.X=work.Left+80; item.Y=work.Top+80; item.VX=90; item.VY=20;
                }
                // Every retained free toy must keep moving, even when it is not
                // the one currently selected by the dog.
                float ballX=ball.X,boneX=bone.X,boarX=boar.X;
                Call(pet,"StepToys",.02);
                Assert(ball.X>ballX && bone.X>boneX && boar.X>boarX,"Inactive toy physics stopped");
                foreach(ToyForm item in Toys(pet)) {
                    Rectangle work=Screen.FromPoint(new Point((int)item.X,(int)item.Y)).WorkingArea;
                    Assert(item.X>=work.Left+21 && item.X<=work.Right-21 && item.Y>=work.Top+18 && item.Y<=work.Bottom-18,"A toy escaped its own monitor");
                }
                Item(pet,"Поверх окон").PerformClick(); foreach(ToyForm item in Toys(pet)) Assert(!item.TopMost,"Topmost did not update inactive toy");
                Item(pet,"Поверх окон").PerformClick(); foreach(ToyForm item in Toys(pet)) Assert(item.TopMost,"Topmost did not restore inactive toy");
                Call(pet,"ThrowToy",ToyKind.Boar); Call(pet,"BeginToyAction",Mood.ToyChew);
                Set(pet,"timeline",(double)Get(pet,"toyPlayUntil")+1); Call(pet,"TickToyPlay",.02,(double)Get(pet,"timeline"));
                Assert(Toys(pet).Count==3 && !boar.Held,"End of play removed or hid a toy");
                Item(pet,"Убрать все игрушки").PerformClick();
                Assert(Toys(pet).Count==0 && Get(pet,"toy")==null,"Remove-all left a toy selected");
                foreach(ToyForm item in originals.Values) Assert(item.IsDisposed && item.Released==null,"Remove-all leaked a window or release callback");
                // Cleanup also works while one toy is held and hidden.
                foreach(ToyKind kind in originals.Keys) Call(pet,"ThrowToy",kind);
                ToyForm[] closing=Toys(pet).ToArray(); Call(pet,"BeginToyAction",Mood.ToyCarry);
                pet.Dispose(); foreach(ToyForm item in closing) Assert(item.IsDisposed,"Pet shutdown leaked a retained toy");
            }
            NativeWindows();
            Console.WriteLine("PASS: all 3 toy kinds coexist; 90 selections reuse windows; old-toy drag release selects exact instance; held toys are dropped during switching/follow/monitor changes; inactive physics and topmost update; toys remain after play; remove-all and shutdown dispose every retained window; native windows stay visible across selection, held windows hide then reappear after switching/end-play, and remove-all closes every window.");
        }
    }
}
