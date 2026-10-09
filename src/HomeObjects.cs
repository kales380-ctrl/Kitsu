using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace KitsuDesktop {
    enum HomeKind { Bed, Feeder }

    // Local wall time, independent of the animation clock. Each scheduled portion
    // is remembered across restarts and backward adjustments of the PC clock.
    sealed class MealSchedule {
        readonly string path;
        readonly HashSet<string> served=new HashSet<string>();
        DateTime? observed;
        public MealSchedule(string historyPath) {
            path=historyPath;
            try { if(File.Exists(path)) foreach(string line in File.ReadAllLines(path)) served.Add(line.Trim()); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
        public int Poll(DateTime now) {
            int due=0;
            foreach(int hour in new[] {10,17,22}) {
                DateTime slot=now.Date.AddHours(hour);
                bool minute=now>=slot && now<slot.AddMinutes(1);
                bool crossed=observed.HasValue && observed.Value<slot && now>=slot;
                string key=slot.ToString("yyyy-MM-dd-HH",CultureInfo.InvariantCulture);
                if((minute || crossed) && served.Add(key)) due++;
            }
            observed=now;
            if(due>0) Save();
            return due;
        }
        void Save() {
            if(String.IsNullOrEmpty(path)) return;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary=path+".tmp";
                File.WriteAllLines(temporary,served);
                File.Move(temporary,path,true);
            } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }

    static class HomeArt {
        static readonly Bitmap bed=Load("Kitsu.Bed"),feeder=Load("Kitsu.Feeder");
        static Bitmap Load(string name) {
            using(Stream stream=typeof(HomeArt).Assembly.GetManifestResourceStream(name)) {
                if(stream==null) throw new InvalidDataException("Missing home artwork: "+name);
                using(Bitmap source=new Bitmap(stream)) return new Bitmap(source);
            }
        }
        public static void DrawBed(Graphics g,RectangleF bounds,bool frontOnly=false) {
            GraphicsState state=g.Save();
            if(frontOnly) g.SetClip(new RectangleF(bounds.X,bounds.Y+bounds.Height*.73f,bounds.Width,bounds.Height*.27f),CombineMode.Intersect);
            g.DrawImage(bed,bounds); g.Restore(state);
        }
        public static void DrawFeeder(Graphics g,RectangleF bounds,double dispense,double food) {
            g.DrawImage(feeder,bounds);
            GraphicsState state=g.Save(); g.TranslateTransform(bounds.X,bounds.Y); g.ScaleTransform(bounds.Width/140f,bounds.Height/230f);
            // Pellets move from the outlet to the metal bowl, then remain there
            // and disappear gradually as Kitsu eats her portion.
            using(Brush dark=new SolidBrush(Color.FromArgb(105,61,32))) using(Brush light=new SolidBrush(Color.FromArgb(173,118,64))) {
                int count=(int)(Math.Max(0,Math.Min(1,food))*48);
                for(int i=0;i<count;i++) {
                    float px=70+(float)Math.Sin(i*2.39996)*Math.Min(28,4+(float)Math.Sqrt(i)*4);
                    float py=185+(float)Math.Cos(i*2.39996)*Math.Min(7,2+(float)Math.Sqrt(i));
                    g.FillEllipse(dark,px-2,py-1.5f,4,3); g.FillEllipse(light,px-1.5f,py-1.2f,2.3f,1.3f);
                }
                if(dispense>=0 && dispense<2.2) for(int i=0;i<18;i++) {
                    double age=dispense-i*.055;
                    if(age<0 || age>1.15) continue;
                    float t=(float)(age%.48/.48);
                    float px=70+(float)Math.Sin(i*4.1)*7*t;
                    float py=142+43*t*t;
                    g.FillEllipse(dark,px,py,3.2f,2.6f); g.FillEllipse(light,px+.3f,py+.3f,1.8f,1);
                }
            }
            g.Restore(state);
        }
        public static PointF FeedingPosition(RectangleF feederBounds,bool facingRight) {
            // The front paws reach 55 px beyond the 104 px sprite centre. The
            // bowl begins 38 px before its centre: leave room between those
            // two silhouettes while the lowered muzzle reaches the near food.
            float sx=feederBounds.Width/140f,sy=feederBounds.Height/230f;
            return new PointF(feederBounds.X+feederBounds.Width*.5f+(facingRight ? -96 : 96)*sx,
                feederBounds.Y+feederBounds.Height*.80f+15*sy);
        }
        public static RectangleF BowlBounds(RectangleF feederBounds) {
            return new RectangleF(feederBounds.X+32*feederBounds.Width/140f,feederBounds.Y+150*feederBounds.Height/230f,
                76*feederBounds.Width/140f,72*feederBounds.Height/230f);
        }
        public static void Preview(string path) {
            using(Bitmap sheet=new Bitmap(1200,1000)) using(Graphics g=Graphics.FromImage(sheet)) using(Font label=new Font("Segoe UI",11)) using(Brush ink=new SolidBrush(Color.FromArgb(48,43,42))) {
                g.Clear(Color.FromArgb(241,234,222)); g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                string[] titles={"Лежанка · как на фото","Место · лежит и смотрит","Иди спать · сон в лежанке","Кормушка · выдача гранул"};
                for(int col=0;col<4;col++) g.DrawString(titles[col],label,ink,col*300+8,8);
                for(int col=0;col<3;col++) {
                    RectangleF bounds=new RectangleF(col*300+8,62,284,189);
                    DrawBed(g,bounds);
                    if(col>0) {
                        Mood rest=col==1 ? Mood.BedLie : Mood.BedSleep;
                        using(Bitmap dog=PixelDog.Draw(rest,2,true,false,0,ToyKind.None)) g.DrawImage(dog,bounds.X+bounds.Width/2-104,bounds.Y+bounds.Height*.78f-156,208,176);
                        DrawBed(g,bounds,true);
                    }
                }
                DrawFeeder(g,new RectangleF(1038,35,140,230),.65,.65);
                PointF feedingPoint=FeedingPosition(new RectangleF(1038,35,140,230),true);
                using(Bitmap dog=PixelDog.Draw(Mood.FeedChew,.6,true,false,0,ToyKind.None)) g.DrawImage(dog,feedingPoint.X-104,feedingPoint.Y-156,208,176);
                ToyKind[] kinds={ToyKind.Ball,ToyKind.Bone,ToyKind.Boar};
                string[] toyTitles={"Мячик в пасти · 8 фаз ходьбы","Косточка в пасти · 8 фаз ходьбы","Кабанчик в пасти · 8 фаз ходьбы"};
                for(int row=0;row<3;row++) {
                    int top=290+row*160; g.DrawString(toyTitles[row],label,ink,8,top);
                    for(int col=0;col<8;col++) using(Bitmap dog=PixelDog.Draw(Mood.ToyCarry,col*.08+.001,true,false,0,kinds[row])) g.DrawImage(dog,new Rectangle(col*150+2,top+23,146,124));
                }
                g.DrawString("Кормление · наклонить морду → пожевать → поднять морду → уйти",label,ink,8,790);
                Mood[] feeding={Mood.FeedLower,Mood.FeedLower,Mood.FeedChew,Mood.FeedChew,Mood.FeedRaise,Mood.FeedRaise,Mood.FeedRaise,Mood.LeaveFood};
                double[] times={.001,.25,.001,.13,.001,.25,.47,.16};
                for(int col=0;col<8;col++) using(Bitmap dog=PixelDog.Draw(feeding[col],times[col],true,false,0,ToyKind.None)) g.DrawImage(dog,new Rectangle(col*150+2,818,146,124));
                sheet.Save(path,ImageFormat.Png);
            }
        }
    }

    sealed class HomeObjectForm : AlphaForm {
        public readonly HomeKind Kind;
        public Form Foreground;
        public Action Clicked,Moved;
        public bool Dragging;
        public double DispenseUntil,DispenseStart,Food;
        Point grab,start;
        bool moved;
        double now;
        public HomeObjectForm(HomeKind kind,int scale) {
            Kind=kind; Text=kind==HomeKind.Bed ? "Лежанка Кицу" : "Кормушка Кицу";
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
            AutoScaleMode=AutoScaleMode.None; Cursor=Cursors.Hand; ResizeObject(scale);
            MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { Dragging=true; moved=false; start=Cursor.Position; grab=e.Location; Capture=true; } };
            MouseMove+=delegate {
                if(!Dragging) return;
                Point cursor=Cursor.Position;
                if(Math.Abs(cursor.X-start.X)+Math.Abs(cursor.Y-start.Y)>5) moved=true;
                if(moved) { Location=new Point(cursor.X-grab.X,cursor.Y-grab.Y); ClampTo(Screen.FromPoint(cursor).WorkingArea); Render(now); }
            };
            MouseUp+=delegate(object sender,MouseEventArgs e) {
                if(e.Button!=MouseButtons.Left || !Dragging) return;
                Dragging=false; Capture=false;
                if(moved) { if(Moved!=null) Moved(); } else if(Clicked!=null) Clicked();
            };
            Shown+=delegate { Render(now); };
        }
        public void ResizeObject(int scale) { ClientSize=Kind==HomeKind.Bed ? new Size(300*scale/4,200*scale/4) : new Size(140*scale/4,230*scale/4); Render(now); }
        public PointF RestPoint { get { return new PointF(Left+Width*.5f,Top+Height*.78f); } }
        public PointF BowlPoint { get { return new PointF(Left+Width*.5f,Top+Height*.80f); } }
        public PointF FeedingPosition(bool facingRight) { return HomeArt.FeedingPosition(new RectangleF(Left,Top,Width,Height),facingRight); }
        public void ClampTo(Rectangle area) { Location=new Point(Math.Max(area.Left,Math.Min(area.Right-Width,Left)),Math.Max(area.Top,Math.Min(area.Bottom-Height,Top))); }
        public void Dispense(double time) { DispenseStart=time; DispenseUntil=time+2.2; Food=0; }
        public void Step(double time) { now=time; if(Kind==HomeKind.Feeder && time<DispenseUntil) Food=Math.Min(1,(time-DispenseStart)/1.7); Render(time); }
        public void Render(double time) {
            if(!IsHandleCreated || !Visible) return;
            using(Bitmap canvas=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb)) using(Graphics g=Graphics.FromImage(canvas)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                if(Kind==HomeKind.Bed) HomeArt.DrawBed(g,new RectangleF(0,0,Width,Height));
                else HomeArt.DrawFeeder(g,new RectangleF(0,0,Width,Height),time<DispenseUntil ? time-DispenseStart : -1,Food);
                Present(canvas);
                KeepBelow(Foreground);
            }
        }
    }
}
