using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KitsuDesktop {
    static class FeedingGeometryTest {
        static void Assert(bool pass,string message) { if(!pass) throw new Exception(message); }
        static void CheckPose(RectangleF feeder,float unit,bool right,int frame) {
            Mood mood=frame<4 ? Mood.FeedLower : frame<8 ? Mood.FeedChew : Mood.FeedRaise;
            double time=(frame%4)*.12+.001;
            PointF root=HomeArt.FeedingPosition(feeder,right);
            RectangleF bowl=HomeArt.BowlBounds(feeder);
            using(Bitmap dog=PixelDog.Draw(mood,time,right,false,0,ToyKind.None)) {
                int pawPixels=0;
                // The paw tips occupy the lower front quarter of the actual
                // 208x176 sprite. Muzzle pixels farther forward are excluded.
                for(int y=145;y<158;y++) for(int canonicalX=104;canonicalX<160;canonicalX++) {
                    int x=right ? canonicalX : 207-canonicalX;
                    if(dog.GetPixel(x,y).A<=160) continue;
                    pawPixels++;
                    float screenX=root.X+(x-104)*unit;
                    Assert(right ? screenX<bowl.Left-unit : screenX>bowl.Right+unit,
                        "Paw overlaps bowl: "+mood+" frame "+(frame%4)+" right="+right+" scale="+unit*4);
                }
                Assert(pawPixels>0,"Missing paw sample: "+mood+" "+frame);
                if(frame==3 || frame>=4 && frame<8) {
                    int noseX=-1,noseY=-1;
                    for(int y=130;y<156;y++) for(int canonicalX=160;canonicalX<208;canonicalX++) {
                        int x=right ? canonicalX : 207-canonicalX;
                        if(dog.GetPixel(x,y).A>160 && canonicalX>=noseX) { noseX=canonicalX; noseY=y; }
                    }
                    Assert(noseX>=160,"Lowered muzzle missing");
                    int sourceX=right ? noseX : 207-noseX;
                    PointF muzzle=new PointF(root.X+(sourceX-104)*unit,root.Y+(noseY-156)*unit);
                    float sy=feeder.Height/230f;
                    RectangleF foodArea=new RectangleF(feeder.X+40*unit,feeder.Y+173*sy,60*unit,29*sy);
                    Assert(foodArea.Contains(muzzle),"Muzzle misses food: "+mood+" "+frame+" right="+right+" scale="+unit*4);
                }
            }
        }
        static void Preview(string path) {
            using(Bitmap sheet=new Bitmap(960,540)) using(Graphics g=Graphics.FromImage(sheet)) using(Font font=new Font("Segoe UI",11)) {
                g.Clear(Color.FromArgb(241,234,222)); g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                for(int row=0;row<2;row++) for(int col=0;col<3;col++) {
                    bool right=row==0;
                    RectangleF feeder=new RectangleF(col*320+(right ? 175 : 5),row*270+2,140,230);
                    HomeArt.DrawFeeder(g,feeder,-1,.85);
                    PointF root=HomeArt.FeedingPosition(feeder,right);
                    if(col==0) root.X=feeder.X+70+(right ? -65 : 65);
                    using(Bitmap dog=PixelDog.Draw(Mood.FeedChew,col==2 ? .361 : .001,right,false,0,ToyKind.None))
                        g.DrawImage(dog,new RectangleF(root.X-104,root.Y-156,208,176));
                    g.DrawString(col==0 ? "До · лапа на миске" : "После · лапки рядом с миской",font,Brushes.Black,col*320+6,row*270+237);
                }
                sheet.Save(path,ImageFormat.Png);
            }
        }
        [STAThread] static void Main(string[] args) {
            foreach(int scale in new[] {3,4,5}) foreach(bool right in new[] {true,false}) {
                float unit=scale/4f;
                RectangleF feeder=new RectangleF(-240,125,140*scale/4,230*scale/4);
                for(int frame=0;frame<12;frame++) CheckPose(feeder,unit,right,frame);
            }
            if(args.Length>0) Preview(args[0]);
            Console.WriteLine("PASS: all 12 feeding poses keep front paws outside the bowl and the lowered muzzle in the food; scales 3/4/5, both directions and negative desktop coordinates.");
        }
    }
}
