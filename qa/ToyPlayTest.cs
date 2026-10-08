using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
namespace KitsuDesktop {
 static class ToyPlayTest {
  static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
  static object Get(object o,string name) { return o.GetType().GetField(name,F).GetValue(o); }
  static void Set(object o,string name,object value) { o.GetType().GetField(name,F).SetValue(o,value); }
  static void Call(object o,string name,params object[] args) { try { o.GetType().GetMethod(name,F).Invoke(o,args); } catch(TargetInvocationException e) { throw e.InnerException; } }
  static void Assert(bool pass,string message) { if(!pass) throw new Exception(message); }
  [STAThread] static void Main() {
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   Rectangle negative=new Rectangle(-1920,-200,1920,1040);
   foreach(ToyKind kind in new [] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) using(ToyForm toy=new ToyForm(true)) {
    toy.Kind=kind; toy.X=-1500; toy.Y=200; toy.VX=900; toy.VY=-600;
    for(int i=0;i<1800;i++) { toy.Step(1.0/30,negative); Assert(toy.X>=negative.Left+21 && toy.X<=negative.Right-21 && toy.Y>=negative.Top+18 && toy.Y<=negative.Bottom-18,"Toy escaped negative monitor: "+kind); }
    Assert(Math.Abs(toy.VX)<1 && Math.Abs(toy.VY)<1,"Toy never settled: "+kind);
    toy.Hold(-500,500); toy.Step(1,negative); Assert(toy.Held && toy.X==-500 && toy.Y==500,"Held toy physics moved");
    toy.Release(-500,500,200,-300,negative); Assert(!toy.Held && toy.Kind==kind,"Release lost toy type");
    using(Bitmap art=ToyArt.Draw(kind,42,36)) { Assert(art.GetPixel(0,0).A==0,"Toy artwork transparency"); art.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toy-"+kind+".png")); }
   }
   foreach(ToyKind kind in new [] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) using(PetForm pet=new PetForm(true)) {
    ((Timer)Get(pet,"timer")).Stop(); Set(pet,"rng",new Random(132+(int)kind));
    Call(pet,"ThrowToy",kind); ToyForm original=(ToyForm)Get(pet,"toy"); Rectangle area=(Rectangle)Get(pet,"area");
    System.Collections.Generic.HashSet<Mood> seen=new System.Collections.Generic.HashSet<Mood>();
    for(int step=0;step<9000;step++) {
     double time=(step+1)/60.0; Set(pet,"timeline",time); original.Step(1.0/60,area);
     Call(pet,"TickToyPlay",1.0/60,time); Call(pet,"Clamp");
     Mood state=(Mood)Get(pet,"mood"); seen.Add(state);
     Assert(Object.ReferenceEquals(original,Get(pet,"toy")),"Play replaced toy instance: "+kind);
     Assert(original.Kind==kind,"Play changed toy kind");
     if(state==Mood.ToyPickup || state==Mood.ToyCarry || state==Mood.ToyRoll || state==Mood.ToyShake || state==Mood.ToyChew || state==Mood.ToySettle || state==Mood.ToyRise) Assert(original.Held,"Toy dropped during "+state);
    }
    foreach(Mood expected in new [] {Mood.Play,Mood.ToyPickup,Mood.ToyCarry,Mood.ToyRoll,Mood.ToyShake,Mood.ToyToss,Mood.ToyChew,Mood.ToySettle,Mood.ToyRise}) Assert(seen.Contains(expected),"Missing play action: "+kind+" "+expected);
    Call(pet,"BeginToyAction",Mood.ToyChew); Call(pet,"Pet"); Assert(!original.Held && Object.ReferenceEquals(original,Get(pet,"toy")),"Pet lost held toy");
    Call(pet,"BeginToyAction",Mood.ToyShake); Call(pet,"Command",Mood.Lie,double.PositiveInfinity,"Lie"); Assert(!original.Held,"Command lost held toy");
    Set(pet,"timeline",150.0); Call(pet,"BeginToyAction",Mood.ToyChew); Assert((Mood)Get(pet,"mood")==Mood.ToySettle && original.Held,"Chewing skipped lie-down");
    Set(pet,"timeline",150.94); Call(pet,"TickToyPlay",.01,150.94); Assert((Mood)Get(pet,"mood")==Mood.ToySettle,"Lie-down ended too soon");
    Set(pet,"timeline",150.95); Call(pet,"TickToyPlay",.01,150.95); Assert((Mood)Get(pet,"mood")==Mood.ToyChew && original.Held,"Lie-down did not become chewing");
    double chewEnd=(double)Get(pet,"until"); Assert(chewEnd>=156.95 && chewEnd<=160.95,"Chew duration changed");
    Set(pet,"timeline",chewEnd); Call(pet,"TickToyPlay",.01,chewEnd); Assert((Mood)Get(pet,"mood")==Mood.ToyRise && original.Held,"Chew skipped stand-up");
    double riseEnd=(double)Get(pet,"until"); Assert(Math.Abs(riseEnd-chewEnd-.95)<1e-7,"Stand-up duration changed");
    Set(pet,"timeline",riseEnd); Call(pet,"TickToyPlay",.01,riseEnd); Assert((Mood)Get(pet,"mood")!=Mood.ToyChew && (Mood)Get(pet,"mood")!=Mood.ToySettle,"Toy play repeated chewing without variety");
    Set(pet,"timeline",170.0); Call(pet,"BeginToyAction",Mood.ToyToss);
    Set(pet,"timeline",170.34); Call(pet,"TickToyPlay",.01,170.34); Assert(original.Held,"Toss released before mouth follow through");
    Set(pet,"timeline",170.35); Call(pet,"TickToyPlay",.01,170.35); Assert(!original.Held && (bool)Get(pet,"tossReleased"),"Toss did not release on frame boundary");
    float vx=original.VX,vy=original.VY;
    Set(pet,"timeline",170.5); Call(pet,"TickToyPlay",.01,170.5); Assert(original.VX==vx && original.VY==vy,"Toss released toy twice");
    Set(pet,"timeline",181.0); Call(pet,"TickToyPlay",.01,181.0); Assert(!original.Held && Object.ReferenceEquals(original,Get(pet,"toy")),"Finishing play lost toy");
    Call(pet,"RemoveToy"); Assert(Get(pet,"toy")==null && original.IsDisposed,"Remove did not clean toy window");
    pet.Close();
   }
   Console.WriteLine("PASS: ball/bone/boar physics, alpha artwork, all 9 play states for each kind, stable toy identity, pet and command drops, timed lie-down and stand-up, timed single toss release, retained toys at play end, removal cleanup.");
  }
 }
}