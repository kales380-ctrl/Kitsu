using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace KitsuDesktop {
    static class HomeBehaviorTest {
        static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Get(object o,string n) { return o.GetType().GetField(n,Fields).GetValue(o); }
        static void Set(object o,string n,object v) { o.GetType().GetField(n,Fields).SetValue(o,v); }
        static void Call(object o,string n,params object[] args) {
            try { o.GetType().GetMethod(n,Fields).Invoke(o,args); } catch(TargetInvocationException e) { throw e.InnerException; }
        }
        static void Assert(bool pass,string why) { if(!pass) throw new Exception(why); }
        static void Advance(PetForm pet,double time) { Set(pet,"timeline",time); Call(pet,"TickHome",1.0/60,time); }
        static void FeedingScreenEdges() {
            foreach(int size in new[] {3,4,5}) foreach(bool rightEdge in new[] {false,true}) using(PetForm pet=new PetForm(true)) {
                ((Timer)Get(pet,"timer")).Stop();
                Set(pet,"scale",size); Call(pet,"ResizePet");
                HomeObjectForm feeder=(HomeObjectForm)Get(pet,"feeder"); feeder.ResizeObject(size);
                Rectangle work=(Rectangle)Get(pet,"area");
                feeder.Location=new Point(rightEdge ? work.Right-feeder.Width : work.Left,work.Bottom-feeder.Height-8);
                // Force the initially preferred approach onto the cramped side.
                Set(pet,"x",(float)(rightEdge ? work.Right : work.Left));
                Call(pet,"StartMeal");
                bool direction=(bool)Get(pet,"foodRight");
                Assert(direction==rightEdge,"Meal chose cramped side at screen edge, size "+size);
                PointF stand=feeder.FeedingPosition(direction);
                Set(pet,"x",stand.X); Set(pet,"y",stand.Y); Advance(pet,3);
                Assert((Mood)Get(pet,"mood")==Mood.FeedLower,"Edge approach did not reach meal");
                Call(pet,"Clamp");
                Assert(Math.Abs((float)Get(pet,"x")-stand.X)<.01 && Math.Abs((float)Get(pet,"y")-stand.Y)<.01,"Screen clamp pushed paws back onto bowl");
            }
        }
        [STAThread] static void Main() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            FeedingScreenEdges();
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"schedule-"+Guid.NewGuid().ToString("N"));
            string history=Path.Combine(directory,"meals.txt");
            try {
                DateTime day=new DateTime(2026,2,1);
                MealSchedule schedule=new MealSchedule(history);
                Assert(schedule.Poll(day.AddHours(9).AddMinutes(59))==0,"Early morning meal");
                Assert(schedule.Poll(day.AddHours(10))==1,"Missing 10:00 meal");
                Assert(schedule.Poll(day.AddHours(10).AddSeconds(40))==0,"Repeated portion in same minute");
                Assert(new MealSchedule(history).Poll(day.AddHours(10).AddSeconds(50))==0,"Restart repeated meal");
                Assert(schedule.Poll(day.AddHours(17))==1,"Missing 17:00 meal");
                Assert(schedule.Poll(day.AddHours(22))==1,"Missing 22:00 meal");
                Assert(schedule.Poll(day.AddHours(16))==0 && schedule.Poll(day.AddHours(22))==0,"Clock rollback repeated meal");
                Assert(schedule.Poll(day.AddDays(1).AddHours(10))==1,"Next day meal missing");
                MealSchedule resumed=new MealSchedule(history);
                Assert(resumed.Poll(day.AddDays(2).AddHours(15))==0,"Late startup backfilled missed meals");
                Assert(resumed.Poll(day.AddDays(2).AddHours(17).AddMinutes(3))==1,"Suspend/resume crossed meal lost");
                Assert(resumed.Poll(day.AddDays(2).AddHours(22))==1,"Evening after resume missing");
            } finally { File.Delete(history); File.Delete(history+".tmp"); if(Directory.Exists(directory)) Directory.Delete(directory); }

            using(PetForm pet=new PetForm(true)) {
                ((Timer)Get(pet,"timer")).Stop();
                HomeObjectForm bed=(HomeObjectForm)Get(pet,"bed"),feeder=(HomeObjectForm)Get(pet,"feeder");
                Call(pet,"GoToBed",false,double.PositiveInfinity);
                Assert((Mood)Get(pet,"mood")==Mood.GoBed,"Place skipped walking");
                Set(pet,"x",bed.RestPoint.X); Set(pet,"y",bed.RestPoint.Y); Advance(pet,1);
                Assert((Mood)Get(pet,"mood")==Mood.BedLie && (bool)Get(pet,"inBed"),"Place did not lie inside bed");
                Advance(pet,100);
                Assert((Mood)Get(pet,"mood")==Mood.BedLie,"Place unexpectedly slept or wandered");
                bed.Location=new Point(bed.Left+20,bed.Top-15); Advance(pet,101);
                Assert(Math.Abs((float)Get(pet,"x")-bed.RestPoint.X)<.1,"Dragging bed left dog behind");
                Call(pet,"GoToBed",true,double.PositiveInfinity);
                Assert((Mood)Get(pet,"mood")==Mood.HomeRise,"Sleep command skipped rising from place");
                Advance(pet,103); Set(pet,"x",bed.RestPoint.X); Set(pet,"y",bed.RestPoint.Y); Advance(pet,104);
                Assert((Mood)Get(pet,"mood")==Mood.BedSleep,"Sleep command didn't choose bed");
                Call(pet,"StartMeal");
                Assert((Mood)Get(pet,"mood")==Mood.HomeRise,"Meal didn't wake sleeping dog");
                double firstDispense=feeder.DispenseStart; Call(pet,"StartMeal");
                Assert(feeder.DispenseStart==firstDispense,"Repeated click dispensed twice during waking");
                Advance(pet,106);
                Assert((Mood)Get(pet,"mood")==Mood.GoFood,"Waking didn't start approach to food");
                bool right=(bool)Get(pet,"foodRight");
                PointF diningPoint=feeder.FeedingPosition(right);
                Set(pet,"x",diningPoint.X); Set(pet,"y",diningPoint.Y);
                feeder.Step(107); Advance(pet,107);
                Assert((Mood)Get(pet,"mood")==Mood.FeedLower,"Food approach skipped head lowering");
                Advance(pet,108.01); Assert((Mood)Get(pet,"mood")==Mood.FeedChew,"No chewing stage");
                Advance(pet,114); Assert(feeder.Food>0 && feeder.Food<1,"Food doesn't decrease while eating");
                double stop=(double)Get(pet,"until"); Advance(pet,stop);
                Assert((Mood)Get(pet,"mood")==Mood.FeedRaise && feeder.Food==0,"No empty bowl/head raise");
                Advance(pet,stop+1.01); Assert((Mood)Get(pet,"mood")==Mood.LeaveFood,"No walking away from feeder");
                float before=(float)Get(pet,"x"); Advance(pet,stop+1.2);
                Assert(Math.Abs(before-(float)Get(pet,"x"))>.1,"Dog didn't move away from feeder");
                Call(pet,"Command",Mood.Sit,double.PositiveInfinity,"sit");
                Assert((Mood)Get(pet,"mood")==Mood.Sit && !(bool)Get(pet,"inBed"),"Command didn't interrupt meal cleanly");
                pet.Dispose(); Assert(bed.IsDisposed && feeder.IsDisposed,"Home windows leaked on dispose");
            }
            foreach(ToyKind kind in new[] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) {
                var phases=new HashSet<string>();
                for(int i=0;i<8;i++) phases.Add(PixelDog.FrameKey(Mood.ToyCarry,i*.08+.001,kind,2));
                Assert(phases.Count==8 && PixelDog.FrameKey(Mood.ToyCarry,0,kind,2).StartsWith("carry-"),"Carry uses closed-mouth gait/overlay");
            }
            Console.WriteLine("PASS: daily local-time meals at 10/17/22, durable duplicate prevention, next day, clock rollback, resume; bed approach/awake rest/sleep/rise; feeding wake/approach/lower/chew/raise/leave, single dispensing, consumption, interruption and window cleanup; 8 mouth-grip walk poses per toy.");
        }
    }
}
