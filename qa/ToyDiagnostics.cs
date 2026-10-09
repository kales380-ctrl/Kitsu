using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace KitsuDesktop {
 static class ToyDiagnostics {
 [STAThread] static void Main() {
  ToyKind[] kinds={ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}; double[] times={0,.08,.16,.24,.32,.48};
  using(Bitmap sheet=new Bitmap(1248,672)) using(Graphics g=Graphics.FromImage(sheet)) using(Font label=new Font("Segoe UI",11)) {
   g.Clear(Color.FromArgb(232,221,209));
   for(int row=0;row<3;row++) for(int col=0;col<6;col++) {
    Rectangle at=new Rectangle(col*208,row*224+32,208,176);
    using(Bitmap b=PixelDog.Draw(Mood.ToyCarry,times[col],true,false,0,kinds[row])) g.DrawImageUnscaled(b,at.Location);
    g.DrawString(kinds[row]+" carry t="+times[col].ToString("0.00"),label,Brushes.Black,col*208+5,row*224+5);
   }
   sheet.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toy-carry-actual.png"),ImageFormat.Png);
  }
  times=new [] {0,.174999,.349999,.35,.525,.699999};
  using(Bitmap sheet=new Bitmap(1248,672)) using(Graphics g=Graphics.FromImage(sheet)) using(Font label=new Font("Segoe UI",11)) {
   g.Clear(Color.FromArgb(232,221,209));
   for(int row=0;row<3;row++) for(int col=0;col<6;col++) {
    Rectangle at=new Rectangle(col*208,row*224+32,208,176);
    using(Bitmap b=PixelDog.Draw(Mood.ToyToss,times[col],true,false,0,kinds[row])) g.DrawImageUnscaled(b,at.Location);
    // Cyan cross marks the actual world release point in dog-local coordinates.
    if(col==3) using(Pen cross=new Pen(Color.Cyan,1)) { g.DrawLine(cross,at.X+150-5,at.Y+38,at.X+150+5,at.Y+38);g.DrawLine(cross,at.X+150,at.Y+38-5,at.X+150,at.Y+38+5); }
    g.DrawString(kinds[row]+" toss t="+times[col].ToString("0.000"),label,Brushes.Black,col*208+5,row*224+5);
   }
   sheet.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toy-toss-actual.png"),ImageFormat.Png);
  }
  Mood[] transitions={Mood.ToySettle,Mood.ToyChew,Mood.ToyRise};
  using(Bitmap sheet=new Bitmap(1664,2016)) using(Graphics g=Graphics.FromImage(sheet)) using(Font label=new Font("Segoe UI",11)) {
   g.Clear(Color.FromArgb(232,221,209));
   for(int kindIndex=0;kindIndex<3;kindIndex++) for(int transition=0;transition<3;transition++) for(int col=0;col<8;col++) {
    int row=kindIndex*3+transition; ToyKind kind=kinds[kindIndex];
    double span=transition==1 ? .96 : .95,time=span*col/8+.002;
    Rectangle at=new Rectangle(col*208,row*224+32,208,176);
    using(Bitmap b=PixelDog.Draw(transitions[transition],time,true,false,0,kind)) g.DrawImageUnscaled(b,at.Location);
    g.DrawString(kind+" "+transitions[transition]+" f="+col,label,Brushes.Black,col*208+5,row*224+5);
   }
   sheet.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toy-chew-transitions.png"),ImageFormat.Png);
  }
  // Actual neighboring state boundaries reveal visual jumps that counting
  // animation keys cannot catch. Include both walking directions and all toys.
  Mood[] boundaryMoods={Mood.ToyCarry,Mood.ToySettle,Mood.ToySettle,Mood.ToyChew,Mood.ToyChew,Mood.ToyRise,Mood.ToyRise,Mood.ToyCarry};
  double[] boundaryTimes={.639999,0,.949999,0,.959999,0,.949999,0};
  using(Bitmap sheet=new Bitmap(1664,1344)) using(Graphics g=Graphics.FromImage(sheet)) using(Font label=new Font("Segoe UI",11)) {
   g.Clear(Color.FromArgb(232,221,209));
   for(int kindIndex=0;kindIndex<3;kindIndex++) for(int direction=0;direction<2;direction++) for(int col=0;col<8;col++) {
    int row=kindIndex*2+direction;
    using(Bitmap b=PixelDog.Draw(boundaryMoods[col],boundaryTimes[col],direction==0,false,0,kinds[kindIndex])) g.DrawImageUnscaled(b,col*208,row*224+32);
    g.DrawString(kinds[kindIndex]+" "+(direction==0 ? "R " : "L ")+boundaryMoods[col]+" "+(boundaryTimes[col]==0 ? "start" : "end"),label,Brushes.Black,col*208+5,row*224+5);
   }
   sheet.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"toy-grip-boundaries.png"),ImageFormat.Png);
  }
  for(int frame=0;frame<8;frame++) using(Bitmap b=PixelDog.Draw(Mood.Lie,.95*frame/8+.002,true,false,0,ToyKind.None)) {
   int top=176,bottom=0,left=208,right=0;
   for(int yy=0;yy<b.Height;yy++) for(int xx=0;xx<b.Width;xx++) if(b.GetPixel(xx,yy).A>90) { top=Math.Min(top,yy);bottom=Math.Max(bottom,yy);left=Math.Min(left,xx);right=Math.Max(right,xx); }
   int h=bottom-top+1,tip=-1,tipTop=0,tipBottom=0;
   for(int yy=top+(int)(h*.15);yy<top+h*.60;yy++) for(int xx=0;xx<b.Width;xx++) if(b.GetPixel(xx,yy).A>120) {
    if(xx>tip) { tip=xx;tipTop=yy;tipBottom=yy; } else if(xx==tip) tipBottom=yy;
   }
   Console.WriteLine("GroundLie{0}: bbox=({1},{2})..({3},{4}); noseTip=({5},{6}); suggestedToyCenter=({7},{8})",frame,left,top,right,bottom,tip,(tipTop+tipBottom)/2,tip-13,(tipTop+tipBottom)/2+6);
  }
  foreach(ToyKind kind in kinds) foreach(double time in new [] {.2,.351}) using(Bitmap b=PixelDog.Draw(Mood.ToyToss,time,true,false,0,kind)) {
   int top=176,bottom=0,left=208,right=0,n=0;double sx=0,sy=0;
   for(int yy=0;yy<b.Height;yy++) for(int xx=0;xx<b.Width;xx++) { Color c=b.GetPixel(xx,yy); if(c.A>90) { top=Math.Min(top,yy);bottom=Math.Max(bottom,yy);left=Math.Min(left,xx);right=Math.Max(right,xx); }
    bool colored=kind==ToyKind.Ball ? c.G>110 && c.B>100 && c.R<c.G*.9 : kind==ToyKind.Bone ? c.R>180 && c.G>130 && c.B>70 : c.R>110 && c.R>c.G*1.25 && c.G>55 && c.B>40;
    if(xx>115 && yy<65 && c.A>90 && colored) { sx+=xx;sy+=yy;n++; }
   }
   Console.WriteLine("{0}Toss t={1}: bbox=({2},{3})..({4},{5}); toyColorCentroid=({6:0.0},{7:0.0}) samples{8}",kind,time,left,top,right,bottom,n>0 ? sx/n : 0,n>0 ? sy/n : 0,n);
  }
  foreach(ToyKind kind in new [] {ToyKind.Ball,ToyKind.Boar}) foreach(Mood mood in new [] {Mood.ToySettle,Mood.ToyChew}) using(Bitmap b=PixelDog.Draw(mood,mood==Mood.ToySettle ? .94 : 0,true,false,0,kind)) {
   int top=176,bottom=0,left=208,right=0;
   for(int yy=0;yy<b.Height;yy++) for(int xx=0;xx<b.Width;xx++) if(b.GetPixel(xx,yy).A>90) { top=Math.Min(top,yy);bottom=Math.Max(bottom,yy);left=Math.Min(left,xx);right=Math.Max(right,xx); }
   Console.WriteLine("{0} {1}: bbox=({2},{3})..({4},{5}) width{6} height{7}",kind,mood,left,top,right,bottom,right-left+1,bottom-top+1);
  }
 }
 }
}
