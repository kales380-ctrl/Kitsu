using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KitsuDesktop {
    static class HomeWindowLayersTest {
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Get(object o,string field) { return o.GetType().GetField(field,Private).GetValue(o); }
        static bool Above(Form top,Form bottom) {
            for(IntPtr h=GetWindow(bottom.Handle,3);h!=IntPtr.Zero;h=GetWindow(h,3)) if(h==top.Handle) return true;
            return false;
        }
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        [STAThread] static void Main() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(PetForm pet=new PetForm(true)) {
                ((Timer)Get(pet,"timer")).Stop(); pet.Show(); Application.DoEvents();
                HomeObjectForm bed=(HomeObjectForm)Get(pet,"bed"),feeder=(HomeObjectForm)Get(pet,"feeder");
                foreach(bool topmost in new[] {true,false,true}) {
                    pet.TopMost=topmost; bed.TopMost=topmost; feeder.TopMost=topmost;
                    foreach(HomeObjectForm item in new[] {bed,feeder}) {
                        item.BringToFront();
                        Check(Above(item,pet),"Test did not reproduce furniture obscuring the dog");
                        item.Render(1);
                        Check(Above(pet,item),"Furniture render did not restore dog foreground");
                        item.Left+=5; item.ResizeObject(4); item.Step(1.1);
                        Check(Above(pet,item),"Moving/resizing furniture obscured the dog");
                        item.BringToFront();
                        typeof(PetForm).GetMethod("Render",Private).Invoke(pet,null);
                        Check(Above(pet,bed) && Above(pet,feeder),"Dog render did not restore both background windows");
                    }
                }
                pet.Close();
                Check(bed.IsDisposed && feeder.IsDisposed,"Home windows survived pet close");
            }
            Console.WriteLine("PASS: actual layered window order after show, foreground activation, move, resize and TopMost changes; furniture disposal.");
        }
    }
}
