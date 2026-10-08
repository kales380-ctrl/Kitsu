using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Collections.Generic;

namespace KitsuDesktop {
    // Generated atlas is embedded verbatim; cells are sampled at runtime, never recolored.
    static class StillDog {
        static readonly Bitmap[] frames = Load();
        static Bitmap[] Load() {
            using(Stream stream=typeof(StillDog).Assembly.GetManifestResourceStream("Kitsu.Atlas"))
            using(Bitmap atlas=new Bitmap(stream)) {
                if(atlas.GetPixel(0,0).A!=0) throw new InvalidDataException("Atlas requires real transparency.");
                Bitmap[] result=new Bitmap[16];
                int[] rows=new int[5]; rows[4]=atlas.Height;
                for(int row=1;row<4;row++) {
                    int ideal=row*atlas.Height/4,best=int.MaxValue;
                    for(int y=ideal-atlas.Height/24;y<ideal+atlas.Height/24;y++) {
                        int opaque=0; for(int x=0;x<atlas.Width;x++) if(atlas.GetPixel(x,y).A>28) opaque++;
                        int score=opaque*1000+Math.Abs(y-ideal);
                        if(score<best) { best=score; rows[row]=y; }
                    }
                }
                int[,] cols=new int[4,5];
                for(int row=0;row<4;row++) {
                    cols[row,4]=atlas.Width;
                    for(int col=1;col<4;col++) {
                        int ideal=col*atlas.Width/4,best=int.MaxValue;
                        for(int x=ideal-atlas.Width/24;x<ideal+atlas.Width/24;x++) {
                            int opaque=0; for(int y=rows[row]+1;y<rows[row+1]-1;y++) if(atlas.GetPixel(x,y).A>28) opaque++;
                            int score=opaque*1000+Math.Abs(x-ideal);
                            if(score<best) { best=score; cols[row,col]=x; }
                        }
                    }
                }
                for(int i=0;i<16;i++) {
                    int x0=cols[i/4,i%4], y0=rows[i/4];
                    int x1=cols[i/4,i%4+1], y1=rows[i/4+1];
                    // A slight inset excludes wisps that touch a neighboring cell.
                    int minX=x1,minY=y1,maxX=x0,maxY=y0,biggest=0;
                    int cellW=x1-x0,cellH=y1-y0;
                    bool[] opaque=new bool[cellW*cellH],visited=new bool[cellW*cellH];
                    for(int y=1;y<cellH-1;y++) for(int x=1;x<cellW-1;x++) opaque[y*cellW+x]=atlas.GetPixel(x+x0,y+y0).A>28;
                    for(int start=0;start<opaque.Length;start++) if(opaque[start] && !visited[start]) {
                        Stack<int> todo=new Stack<int>(); todo.Push(start); visited[start]=true;
                        int size=0,l=cellW,t=cellH,r=0,b=0;
                        while(todo.Count>0) {
                            int at=todo.Pop(),x=at%cellW,y=at/cellW; size++;
                            l=Math.Min(l,x); t=Math.Min(t,y); r=Math.Max(r,x); b=Math.Max(b,y);
                            foreach(int next in new int[] {x>0 ? at-1 : -1,x<cellW-1 ? at+1 : -1,y>0 ? at-cellW : -1,y<cellH-1 ? at+cellW : -1})
                                if(next>=0 && opaque[next] && !visited[next]) { visited[next]=true; todo.Push(next); }
                        }
                        if(size>biggest) { biggest=size; minX=l+x0; minY=t+y0; maxX=r+x0; maxY=b+y0; }
                    }
                    if(minX>=maxX) throw new InvalidDataException("Empty pose "+i);
                    minX=Math.Max(x0+1,minX-2); minY=Math.Max(y0+1,minY-2);
                    maxX=Math.Min(x1-1,maxX+2); maxY=Math.Min(y1-1,maxY+2);
                    result[i]=atlas.Clone(Rectangle.FromLTRB(minX,minY,maxX+1,maxY+1),PixelFormat.Format32bppArgb);
                }
                return result;
            }
        }
        public static Bitmap Draw(Mood mood,double time,bool right,bool happy,int gaze) {
            int frame=0; double sx=1; bool mirror=!right;
            int cycle=(int)(time*7)%4;
            switch(mood) {
                case Mood.Walk: case Mood.Follow: case Mood.IconPlay: frame=cycle<2 ? 1 : 2; break;
                case Mood.Run: case Mood.Play: frame=cycle==0 ? 1 : cycle==2 ? 2 : 3; break;
                case Mood.Sniff: case Mood.Bow: frame=8; break;
                case Mood.Sleep: frame=14; break;
                case Mood.Jump: frame=13; break;
                case Mood.Chase: case Mood.Spin:
                    int turn=(int)(time*4)%4;
                    frame=turn==0 ? 0 : turn==1 ? 15 : turn==2 ? 0 : 4;
                    mirror=turn==2; sx=.84+.16*Math.Abs(Math.Cos(time*2*Math.PI)); break;
                case Mood.Bark: frame=((int)(time*5)%2)==0 ? 12 : 0; break;
                case Mood.PawLeft: frame=6; mirror=false; break;
                case Mood.PawRight: frame=7; mirror=false; break;
                case Mood.Bunny: frame=10; mirror=false; break;
                case Mood.Sit: frame=5; mirror=false; break;
                case Mood.Chew: case Mood.Lie: frame=9; break;
                case Mood.Dead: frame=11; mirror=false; break;
                default: if(happy) { frame=4; mirror=false; } break;
            }
            Bitmap output=new Bitmap(208,176,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(output)) {
                g.Clear(Color.Transparent); g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                Bitmap sprite=frames[frame];
                // A consistent body scale keeps lying and bowing poses lower than standing.
                float maxH=(frame==9 ? 92 : frame==14 ? 78 : frame==11 ? 110 : frame==8 ? 122 : frame==3 || frame==13 ? 123 : frame==10 ? 158 : 150);
                float fit=Math.Min(198f/sprite.Width,maxH/sprite.Height);
                float w=sprite.Width*fit*(float)sx,h=sprite.Height*fit;
                float bob=(mood==Mood.Walk || mood==Mood.Run || mood==Mood.Play || mood==Mood.Follow || mood==Mood.IconPlay) ? (float)Math.Sin(time*14)*2 : 0;
                float breath=(mood==Mood.Sleep || mood==Mood.Sit || mood==Mood.Lie || mood==Mood.Bunny) ? (float)Math.Sin(time*2.3)*1.3f : 0;
                h+=breath;
                if(mirror) { g.TranslateTransform(208,0); g.ScaleTransform(-1,1); }
                g.DrawImage(sprite,new RectangleF((208-w)/2,156-h+bob,w,h));
                if(mood==Mood.Chew) {
                    using(Brush bone=new SolidBrush(Color.FromArgb(242,220,174))) { g.FillEllipse(bone,166,142,8,10); g.FillRectangle(bone,171,145,18,5); g.FillEllipse(bone,184,142,8,10); }
                }
            }
            return output;
        }
    }

    static class BarkSound {
        static readonly MemoryStream stream=Make();
        static readonly SoundPlayer player=new SoundPlayer(stream);
        // Short synthesized woofs; no microphone, downloaded audio or network.
        static MemoryStream Make() {
            int rate=22050, samples=rate*9/10; Random random=new Random(22);
            MemoryStream wav=new MemoryStream(); BinaryWriter writer=new BinaryWriter(wav);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+samples*2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples*2);
            double filtered=0;
            for(int i=0;i<samples;i++) {
                double t=(double)i/rate,local=t<.40 ? t : t-.48;
                double envelope=local>=0 && local<.3 ? Math.Sin(Math.PI*local/.3)*Math.Exp(-local*5) : 0;
                double freq=230-200*Math.Max(0,local),noise=random.NextDouble()*2-1;
                filtered=.72*filtered+.28*noise;
                double voice=Math.Sin(2*Math.PI*freq*local)+.5*Math.Sin(2*Math.PI*freq*2*local)+.24*Math.Sin(2*Math.PI*freq*3*local);
                writer.Write((short)(Math.Max(-1,Math.Min(1,(voice*.25+filtered*.7)*envelope)) * 22000));
            }
            wav.Position=0; return wav;
        }
        public static void Play() { try { player.Play(); } catch { } }
        public static void Save(string path) { File.WriteAllBytes(path,stream.ToArray()); }
    }
}

