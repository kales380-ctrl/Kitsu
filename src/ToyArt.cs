using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KitsuDesktop {
    enum ToyKind { None, Ball, Bone, Boar }

    // The toys are drawn as small, rounded rubber objects. The same artwork is
    // used in their transparent desktop windows and, when needed, in a mouth.
    static class ToyArt {
        public static Bitmap Draw(ToyKind kind,int width,int height) {
            Bitmap result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(result)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.ScaleTransform(width/42f,height/36f);
                Draw(g,kind,new RectangleF(0,0,42,36));
            }
            return result;
        }
        public static void Draw(Graphics g,ToyKind kind,RectangleF bounds,float angle=0) {
            if(kind==ToyKind.None) return;
            GraphicsState state=g.Save();
            g.TranslateTransform(bounds.X,bounds.Y);
            g.ScaleTransform(bounds.Width/42f,bounds.Height/36f);
            if(angle!=0) { g.TranslateTransform(21,18); g.RotateTransform(angle); g.TranslateTransform(-21,-18); }
            if(kind==ToyKind.Bone) DrawBone(g);
            else if(kind==ToyKind.Boar) DrawBoar(g);
            else DrawBall(g);
            g.Restore(state);
        }
        static void DrawBall(Graphics g) {
            using(Brush shadow=new SolidBrush(Color.FromArgb(28,0,0,0))) g.FillEllipse(shadow,7,30,29,4);
            using(GraphicsPath sphere=new GraphicsPath()) {
                sphere.AddEllipse(5,3,32,30);
                using(PathGradientBrush rubber=new PathGradientBrush(sphere)) {
                    rubber.CenterPoint=new PointF(15,10); rubber.CenterColor=Color.FromArgb(129,219,215);
                    rubber.SurroundColors=new Color[] {Color.FromArgb(34,104,129)}; g.FillPath(rubber,sphere);
                }
                GraphicsState clip=g.Save(); g.SetClip(sphere);
                using(Pen stripe=new Pen(Color.FromArgb(244,209,115),5)) {
                    g.DrawBezier(stripe,11,2,18,11,13,24,22,34);
                    g.DrawBezier(stripe,4,13,15,10,29,17,38,12);
                }
                g.Restore(clip);
                using(Pen outline=new Pen(Color.FromArgb(34,91,105),1.1f)) g.DrawPath(outline,sphere);
            }
            using(Brush light=new SolidBrush(Color.FromArgb(185,255,255,244))) g.FillEllipse(light,11,7,7,3);
        }
        static void DrawBone(Graphics g) {
            using(Brush shadow=new SolidBrush(Color.FromArgb(28,0,0,0))) g.FillEllipse(shadow,3,29,35,4);
            using(GraphicsPath path=new GraphicsPath()) {
                path.AddBezier(11,13,4,2,-1,10,5,17);
                path.AddBezier(5,17,-1,24,6,31,12,23);
                path.AddLine(12,23,29,23);
                path.AddBezier(29,23,36,31,43,24,37,17);
                path.AddBezier(37,17,43,10,37,2,30,13);
                path.CloseFigure();
                using(LinearGradientBrush fill=new LinearGradientBrush(new Rectangle(0,5,42,24),Color.FromArgb(255,240,204),Color.FromArgb(195,151,94),90)) g.FillPath(fill,path);
                using(Pen outline=new Pen(Color.FromArgb(155,115,71),1.1f)) g.DrawPath(outline,path);
            }
            using(Pen light=new Pen(Color.FromArgb(210,255,250,219),2)) g.DrawLine(light,13,15,29,15);
        }
        static void DrawBoar(Graphics g) {
            using(Brush shadow=new SolidBrush(Color.FromArgb(25,0,0,0))) g.FillEllipse(shadow,3,30,37,4);
            using(Brush legs=new SolidBrush(Color.FromArgb(113,68,65))) {
                g.FillEllipse(legs,10,23,6,9); g.FillEllipse(legs,25,23,6,9);
            }
            using(Pen tail=new Pen(Color.FromArgb(151,94,85),2.5f)) {
                g.DrawArc(tail,1,14,8,9,10,310); g.DrawLine(tail,7,21,9,20);
            }
            using(GraphicsPath body=new GraphicsPath()) {
                body.AddEllipse(7,10,29,19);
                using(PathGradientBrush rubber=new PathGradientBrush(body)) {
                    rubber.CenterPoint=new PointF(21,13); rubber.CenterColor=Color.FromArgb(216,145,130);
                    rubber.SurroundColors=new Color[] {Color.FromArgb(137,80,75)}; g.FillPath(rubber,body);
                }
                using(Pen outline=new Pen(Color.FromArgb(101,60,58),1)) g.DrawPath(outline,body);
            }
            using(Brush ears=new SolidBrush(Color.FromArgb(147,81,76))) {
                g.FillPolygon(ears,new PointF[] {new PointF(26,15),new PointF(25,5),new PointF(31,10)});
                g.FillPolygon(ears,new PointF[] {new PointF(32,13),new PointF(35,6),new PointF(36,17)});
            }
            using(Brush inner=new SolidBrush(Color.FromArgb(229,166,152))) g.FillPolygon(inner,new PointF[] {new PointF(27,12),new PointF(27,8),new PointF(30,11)});
            using(Brush face=new SolidBrush(Color.FromArgb(176,107,96))) g.FillEllipse(face,25,12,13,14);
            using(Brush snout=new SolidBrush(Color.FromArgb(227,158,141))) g.FillEllipse(snout,31,18,10,7);
            using(Brush nostril=new SolidBrush(Color.FromArgb(113,58,58))) { g.FillEllipse(nostril,35,20,1.8f,2.3f); g.FillEllipse(nostril,38,20,1.5f,2.3f); }
            using(Brush eye=new SolidBrush(Color.FromArgb(42,29,29))) g.FillEllipse(eye,31,15,2.8f,2.8f);
            using(Brush shine=new SolidBrush(Color.FromArgb(255,240,205))) g.FillEllipse(shine,31.5f,15,1,1);
            using(Brush tusk=new SolidBrush(Color.FromArgb(255,235,193))) g.FillPolygon(tusk,new PointF[] {new PointF(32,24),new PointF(30,21),new PointF(30,26)});
            using(Pen bristle=new Pen(Color.FromArgb(111,64,62),1.1f)) {
                g.DrawLine(bristle,15,11,17,7); g.DrawLine(bristle,18,10,20,6); g.DrawLine(bristle,21,10,23,7);
            }
            using(Pen highlight=new Pen(Color.FromArgb(135,250,201,176),1.5f)) g.DrawArc(highlight,12,12,17,10,205,70);
        }
    }
}
