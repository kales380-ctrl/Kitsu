using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace KitsuDesktop {
    // Each frame is an actual pose. Rotation, stepping and rolling come from the
    // drawings themselves, with one shared scale per sequence and no image rotation.
    static class PixelDog {
        sealed class Atlas {
            public readonly Bitmap[] Frames;
            public Atlas(string name) {
                using(Stream stream=typeof(PixelDog).Assembly.GetManifestResourceStream(name)) {
                    if(stream==null) throw new InvalidDataException("Missing animation: "+name);
                    using(Bitmap source=new Bitmap(stream)) using(Bitmap atlas=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb)) {
                        using(Graphics copy=Graphics.FromImage(atlas)) { copy.CompositingMode=CompositingMode.SourceCopy; copy.DrawImageUnscaled(source,0,0); }
                        Rectangle full=new Rectangle(0,0,atlas.Width,atlas.Height);
                        BitmapData data=atlas.LockBits(full,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                        byte[] pixels=new byte[data.Stride*data.Height]; int stride=data.Stride;
                        try { Marshal.Copy(data.Scan0,pixels,0,pixels.Length); } finally { atlas.UnlockBits(data); }
                        if(pixels[3]>28) throw new InvalidDataException("Animation backdrop is not transparent: "+name);
                        // Some generated sheets contain alpha 1 on their empty gutters.
                        // Remove that invisible fringe so the desktop stays click-through.
                        for(int p=3;p<pixels.Length;p+=4) if(pixels[p]<=2) pixels[p]=0;
                        data=atlas.LockBits(full,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                        try { Marshal.Copy(pixels,0,data.Scan0,pixels.Length); } finally { atlas.UnlockBits(data); }
                        int[] rows=new int[5]; rows[4]=atlas.Height;
                        for(int r=1;r<4;r++) {
                            int ideal=r*atlas.Height/4,best=int.MaxValue;
                            for(int y=ideal-atlas.Height/24;y<ideal+atlas.Height/24;y++) {
                                int count=0; for(int x=0;x<atlas.Width;x++) if(pixels[y*stride+x*4+3]>28) count++;
                                int score=count*1000+Math.Abs(y-ideal);
                                if(score<best) { best=score; rows[r]=y; }
                            }
                        }
                        int[,] cols=new int[4,5];
                        for(int r=0;r<4;r++) {
                            cols[r,4]=atlas.Width;
                            for(int c=1;c<4;c++) {
                                int ideal=c*atlas.Width/4,best=int.MaxValue;
                                for(int x=ideal-atlas.Width/24;x<ideal+atlas.Width/24;x++) {
                                    int count=0; for(int y=rows[r]+1;y<rows[r+1]-1;y++) if(pixels[y*stride+x*4+3]>28) count++;
                                    int score=count*1000+Math.Abs(x-ideal);
                                    if(score<best) { best=score; cols[r,c]=x; }
                                }
                            }
                        }
                        Frames=new Bitmap[16];
                        for(int i=0;i<16;i++) {
                            int x0=cols[i/4,i%4],y0=rows[i/4],w=cols[i/4,i%4+1]-x0,h=rows[i/4+1]-y0;
                            bool[] mask=new bool[w*h],seen=new bool[w*h];
                            for(int y=1;y<h-1;y++) for(int x=1;x<w-1;x++) mask[y*w+x]=pixels[(y0+y)*stride+(x0+x)*4+3]>28;
                            List<Rectangle> bounds=new List<Rectangle>(); List<int> sizes=new List<int>(); List<int> brightParts=new List<int>(); int largest=0,mainPart=-1;
                            int[] stack=new int[w*h];
                            for(int start=0;start<mask.Length;start++) if(mask[start] && !seen[start]) {
                                int top=0,count=0,bright=0,l=w,t=h,r=0,b=0; stack[top++]=start; seen[start]=true;
                                while(top>0) {
                                    int at=stack[--top],x=at%w,y=at/w; count++;
                                    int pixel=(y0+y)*stride+(x0+x)*4;
                                    if((pixels[pixel+2]>140 && pixels[pixel+1]>85 && pixels[pixel]>45) || (pixels[pixel+1]>110 && pixels[pixel]>110 && pixels[pixel+2]<110)) bright++;
                                    l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x);b=Math.Max(b,y);
                                    if(x>0) Push(at-1,mask,seen,stack,ref top);
                                    if(x<w-1) Push(at+1,mask,seen,stack,ref top);
                                    if(y>0) Push(at-w,mask,seen,stack,ref top);
                                    if(y<h-1) Push(at+w,mask,seen,stack,ref top);
                                }
                                bounds.Add(Rectangle.FromLTRB(l,t,r+1,b+1)); sizes.Add(count); brightParts.Add(bright);
                                if(count>largest) { largest=count;mainPart=bounds.Count-1; }
                            }
                            Rectangle chosen=mainPart>=0 ? bounds[mainPart] : Rectangle.Empty;
                            // Keep the separate ivory bone next to the front paws, while
                            // excluding clipped charcoal wisps from neighboring cells.
                            if(name=="Kitsu.Comfort" && i<8) for(int part=0;part<bounds.Count;part++) {
                                Rectangle piece=bounds[part];
                                if(part!=mainPart && sizes[part]>30 && brightParts[part]>sizes[part]/4 && piece.Left>chosen.Right-25 && piece.Left<chosen.Right+45 && piece.Bottom>chosen.Bottom-40)
                                    chosen=Rectangle.Union(chosen,piece);
                            }
                            if(name.EndsWith("Play") && i<14 || name=="Kitsu.ToyChew" || name.StartsWith("Kitsu.Carry") && i<8) for(int part=0;part<bounds.Count;part++) {
                                Rectangle piece=bounds[part];
                                if(part!=mainPart && sizes[part]>40 && brightParts[part]>sizes[part]/4 && piece.Left>chosen.Left+chosen.Width/2 && piece.Left<chosen.Right+65 && piece.Bottom>chosen.Top+chosen.Height/2 && piece.Top<chosen.Bottom+35)
                                    chosen=Rectangle.Union(chosen,piece);
                            }
                            if(chosen.IsEmpty || largest<500) throw new InvalidDataException("Empty or clipped animation pose: "+name+" "+i);
                            chosen.Inflate(2,2); chosen=Rectangle.Intersect(chosen,new Rectangle(1,1,w-2,h-2)); chosen.Offset(x0,y0);
                            Frames[i]=atlas.Clone(chosen,PixelFormat.Format32bppArgb);
                        }
                    }
                }
            }
            static void Push(int at,bool[] mask,bool[] seen,int[] stack,ref int top) { if(mask[at]&&!seen[at]) { seen[at]=true;stack[top++]=at; } }
            public float Fit(int start,int count,float maxW,float maxH) {
                int w=0,h=0; for(int i=start;i<start+count;i++) { w=Math.Max(w,Frames[i].Width);h=Math.Max(h,Frames[i].Height); }
                return Math.Min(maxW/w,maxH/h);
            }
        }
        static readonly Atlas gait=new Atlas("Kitsu.Gait"),spin=new Atlas("Kitsu.Spin"),tail=new Atlas("Kitsu.Tail"),transitions=new Atlas("Kitsu.Transitions"),comfort=new Atlas("Kitsu.Comfort"),chew=new Atlas("Kitsu.Chew");
        static readonly Atlas ground=new Atlas("Kitsu.Ground"),posture=new Atlas("Kitsu.Posture"),paws=new Atlas("Kitsu.Paws"),ambient=new Atlas("Kitsu.Ambient"),restVoice=new Atlas("Kitsu.RestVoice");
        static readonly Atlas ballPlay=new Atlas("Kitsu.BallPlay"),boarPlay=new Atlas("Kitsu.BoarPlay"),bonePlay=new Atlas("Kitsu.BonePlay"),toyChew=new Atlas("Kitsu.ToyChew"),ballShake=new Atlas("Kitsu.BallShake"),boarShake=new Atlas("Kitsu.BoarShake");
        static readonly Atlas carryBall=new Atlas("Kitsu.CarryBall"),carryBone=new Atlas("Kitsu.CarryBone"),carryBoar=new Atlas("Kitsu.CarryBoar"),meals=new Atlas("Kitsu.Meals");
        static readonly float walkFit=gait.Fit(0,8,198,148),runFit=gait.Fit(8,8,198,148),spinFit=spin.Fit(0,16,198,148),tailFit=tail.Fit(0,16,198,148);
        static readonly float jumpFit=transitions.Fit(0,8,198,145),deadFit=transitions.Fit(8,8,198,148),pickupFit=comfort.Fit(0,8,198,148),petFit=comfort.Fit(8,8,198,148),chewFit=chew.Fit(0,16,198,96);
        static readonly float sitFit=ground.Fit(0,8,198,148),lieFit=ground.Fit(8,8,198,148),bowFit=posture.Fit(0,8,198,148),bunnyFit=posture.Fit(8,8,198,152);
        static readonly float leftPawFit=paws.Fit(0,8,198,148),rightPawFit=paws.Fit(8,8,198,148),idleFit=ambient.Fit(0,8,198,148),sniffFit=ambient.Fit(8,8,198,148);
        static readonly float sleepFit=restVoice.Fit(0,8,198,148),barkFit=restVoice.Fit(8,8,198,148);
        static readonly float ballFit=ballPlay.Fit(0,16,198,148),boarFit=boarPlay.Fit(0,16,198,148),boneFit=bonePlay.Fit(0,16,198,148);
        static readonly float ballShakeFit=ballShake.Fit(0,16,198,148),boarShakeFit=boarShake.Fit(0,16,198,148),ballChewFit=toyChew.Fit(0,8,198,103),boarChewFit=toyChew.Fit(8,8,198,103);
        static readonly float carryBallFit=carryBall.Fit(0,16,198,148),carryBoneFit=carryBone.Fit(0,16,198,148),carryBoarFit=carryBoar.Fit(0,16,198,148),mealFit=meals.Fit(0,12,198,148),bedLieFit=meals.Fit(12,4,184,100);
        static readonly float[] lieMouthX={170,170,172,170,175,172,171,165},lieMouthY={52,82,100,111,100,93,95,95};
        static int Loop(double time,double duration,int count) { return (int)(Math.Max(0,time)%duration/duration*count)%count; }
        static int Stage(double time,double[] boundaries) { int index=0; while(index<boundaries.Length && time>=boundaries[index]) index++; return index; }
        static int Advance(double time,double duration,int count) { return Math.Min(count-1,(int)(Math.Max(0,time)/duration*count)); }
        static int EnterHold(double time,double enter,int holdStart,double holdPeriod,double duration) {
            if(time<enter) return Advance(time,enter,8);
            if(!Double.IsInfinity(duration) && time>=duration-enter) return 7-Advance(time-(duration-enter),enter,8);
            return holdStart+Loop(time-enter,holdPeriod,8-holdStart);
        }
        static int PetIndex(double time) {
            if(time<.8) return Math.Min(4,(int)(time/.16));
            int[] hold={4,4,5,4,6,4}; return hold[Loop(time-.8,1.2,hold.Length)];
        }
        static Atlas ToyAtlas(ToyKind kind) { return kind==ToyKind.Boar ? boarPlay : kind==ToyKind.Bone ? bonePlay : ballPlay; }
        static float ToyFit(ToyKind kind) { return kind==ToyKind.Boar ? boarFit : kind==ToyKind.Bone ? boneFit : ballFit; }
        static Atlas CarryAtlas(ToyKind kind) { return kind==ToyKind.Boar ? carryBoar : kind==ToyKind.Bone ? carryBone : carryBall; }
        static float CarryFit(ToyKind kind) { return kind==ToyKind.Boar ? carryBoarFit : kind==ToyKind.Bone ? carryBoneFit : carryBallFit; }
        static string AtlasName(Atlas a) {
            if(a==gait) return "gait"; if(a==spin) return "spin"; if(a==tail) return "tail"; if(a==transitions) return "transition";
            if(a==comfort) return "comfort"; if(a==chew) return "bone-chew"; if(a==ground) return "ground"; if(a==posture) return "posture";
            if(a==paws) return "paw"; if(a==ambient) return "ambient"; if(a==restVoice) return "rest-voice";
            if(a==ballPlay) return "ball-play"; if(a==boarPlay) return "boar-play"; if(a==bonePlay) return "bone-play";
            if(a==ballShake) return "ball-shake"; if(a==boarShake) return "boar-shake";
            if(a==carryBall) return "carry-ball"; if(a==carryBone) return "carry-bone"; if(a==carryBoar) return "carry-boar"; if(a==meals) return "meals"; return "toy-chew";
        }
        static Atlas Select(Mood mood,double time,ToyKind kind,double duration,out int frame,out float fit,out float hop) {
            Atlas atlas=null; frame=0;fit=1;hop=0;
            switch(mood) {
                case Mood.Idle: atlas=ambient;frame=Loop(time,2.4,8);fit=idleFit;break;
                case Mood.Sniff: atlas=ambient;frame=8+Loop(time,1.2,8);fit=sniffFit;break;
                case Mood.Sit: atlas=ground;frame=EnterHold(time,.85,6,1.8,Double.PositiveInfinity);fit=sitFit;break;
                case Mood.Lie: atlas=ground;frame=8+EnterHold(time,.95,6,1.8,Double.PositiveInfinity);fit=lieFit;break;
                case Mood.BedLie:
                    if(time<.95) { atlas=ground;frame=8+Advance(time,.95,8);fit=lieFit; }
                    else { atlas=meals;frame=12+Loop(time-.95,2.4,4);fit=bedLieFit; } break;
                case Mood.BedSleep: atlas=restVoice;frame=EnterHold(time,1.1,6,2.4,Double.PositiveInfinity);fit=sleepFit;break;
                case Mood.LeaveBed: case Mood.HomeRise: atlas=ground;frame=15-Advance(time,.95,8);fit=lieFit;break;
                case Mood.GoBed: case Mood.GoFood: case Mood.LeaveFood: atlas=gait;frame=Loop(time,.64,8);fit=walkFit;break;
                case Mood.FeedLower: atlas=meals;frame=Advance(time,.48,4);fit=mealFit;break;
                case Mood.FeedChew: atlas=meals;frame=4+Loop(time,.48,4);fit=mealFit;break;
                case Mood.FeedRaise: atlas=meals;frame=8+Advance(time,.48,4);fit=mealFit;break;
                case Mood.RiseSit: atlas=ground;frame=7-Advance(time,.85,8);fit=sitFit;break;
                case Mood.RiseLie: atlas=ground;frame=15-Advance(time,.95,8);fit=lieFit;break;
                case Mood.Bow: atlas=posture;frame=EnterHold(time,.8,6,1.6,duration);fit=bowFit;break;
                case Mood.Bunny: atlas=posture;frame=8+EnterHold(time,.75,5,1.5,duration);fit=bunnyFit;break;
                // Anatomical left/right are separate drawings, rather than a random mirror.
                case Mood.PawLeft: atlas=paws;frame=EnterHold(time,.8,7,1,duration);fit=leftPawFit;break;
                case Mood.PawRight: atlas=paws;frame=8+EnterHold(time,.8,7,1,duration);fit=rightPawFit;break;
                case Mood.Sleep: atlas=restVoice;frame=EnterHold(time,1.1,6,2.4,Double.PositiveInfinity);fit=sleepFit;break;
                case Mood.Wake: atlas=restVoice;frame=7-Advance(time,1.1,8);fit=sleepFit;break;
                case Mood.Bark: atlas=restVoice;frame=8+Loop(time,.7,8);fit=barkFit;break;
                case Mood.Walk: case Mood.Follow: case Mood.IconPlay: atlas=gait;frame=Loop(time,.64,8);fit=walkFit;break;
                case Mood.Run: case Mood.Play: atlas=gait;frame=8+Loop(time,.48,8);fit=runFit;break;
                case Mood.Spin: atlas=spin;frame=(4+Loop(time,1.6,16))%16;fit=spinFit;break;
                case Mood.Chase: atlas=tail;frame=Loop(time,1.6,16);fit=tailFit;break;
                case Mood.Jump:
                    atlas=transitions;frame=Stage(time,new double[] {.08,.18,.28,.38,.57,.72,.86,1.0}); if(frame>7) frame=0;fit=jumpFit;
                    if(time>.28 && time<.86) hop=(float)Math.Sin((time-.28)/.58*Math.PI)*25;break;
                case Mood.Dead: atlas=transitions;frame=8+Stage(time,new double[] {.08,.2,.34,.48,.62,.78,1.0});fit=deadFit;break;
                case Mood.RiseDead: atlas=transitions;frame=15-Advance(time,1.05,8);fit=deadFit;break;
                case Mood.Chew:
                    if(time<1.2) { atlas=comfort;frame=Stage(time,new double[] {.14,.3,.46,.68,.9});fit=pickupFit; }
                    else { atlas=chew;frame=Loop(time-1.2,1.28,16);fit=chewFit; } break;
                case Mood.Pet: atlas=comfort;frame=8+PetIndex(time);fit=petFit;break;
                case Mood.ToyPickup: atlas=CarryAtlas(kind);frame=Advance(time,.65,8);fit=CarryFit(kind);break;
                case Mood.ToyCarry: atlas=CarryAtlas(kind);frame=8+Loop(time,.64,8);fit=CarryFit(kind);break;
                case Mood.ToySettle: atlas=ground;frame=8+Advance(time,.95,8);fit=lieFit;break;
                case Mood.ToyRise: atlas=ground;frame=15-Advance(time,.95,8);fit=lieFit;break;
                case Mood.ToyRoll: atlas=ToyAtlas(kind);frame=8+Loop(time,.5,4);fit=ToyFit(kind);break;
                case Mood.ToyToss: atlas=ToyAtlas(kind);frame=12+Advance(time,.7,4);if(kind==ToyKind.None) frame=Math.Max(14,frame);fit=ToyFit(kind);break;
                case Mood.ToyShake:
                    if(kind==ToyKind.Boar) { atlas=boarShake;frame=Loop(time,1.35,16);fit=boarShakeFit; }
                    else if(kind==ToyKind.Bone) { atlas=bonePlay;frame=4+Loop(time,.45,4);fit=boneFit; }
                    else { atlas=ballShake;frame=Loop(time,1.35,16);fit=ballShakeFit; } break;
                case Mood.ToyChew:
                    if(kind==ToyKind.Bone) { atlas=chew;frame=Loop(time,1.28,16);fit=chewFit; }
                    else { atlas=toyChew;frame=(kind==ToyKind.Boar ? 8 : 0)+Loop(time,.96,8);fit=kind==ToyKind.Boar ? boarChewFit : ballChewFit; } break;
            }
            return atlas;
        }
        static double DefaultDuration(Mood mood) { return mood==Mood.Bunny ? 8.5 : mood==Mood.Bow || mood==Mood.PawLeft || mood==Mood.PawRight ? 5 : Double.PositiveInfinity; }
        public static string FrameKey(Mood mood,double time) { return FrameKey(mood,time,ToyKind.Bone,DefaultDuration(mood)); }
        public static string FrameKey(Mood mood,double time,ToyKind kind,double duration) {
            int frame;float fit,hop;Atlas a=Select(mood,time,kind,duration,out frame,out fit,out hop);
            return a==null ? "still:"+mood : AtlasName(a)+":"+frame;
        }
        public static Bitmap Draw(Mood mood,double time,bool right,bool happy,int gaze) { return Draw(mood,time,right,happy,gaze,ToyKind.Bone,DefaultDuration(mood)); }
        public static Bitmap Draw(Mood mood,double time,bool right,bool happy,int gaze,ToyKind kind) { return Draw(mood,time,right,happy,gaze,kind,DefaultDuration(mood)); }
        public static Bitmap Draw(Mood mood,double time,bool right,bool happy,int gaze,ToyKind kind,double duration) {
            int frame;float fit,hop; Atlas atlas=Select(mood,time,kind,duration,out frame,out fit,out hop);
            if(atlas==null) return StillDog.Draw(mood,time,right,happy,gaze);
            Bitmap output=new Bitmap(208,176,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(output)) {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.SmoothingMode=SmoothingMode.AntiAlias;
                Bitmap pose=atlas.Frames[frame];float w=pose.Width*fit,h=pose.Height*fit;
                // Front commands keep their anatomical paw identity whichever way she last walked.
                bool front=mood==Mood.PawLeft || mood==Mood.PawRight || mood==Mood.Bunny || mood==Mood.Sit || mood==Mood.RiseSit || mood==Mood.BedLie && time>=.95;
                if(!right && !front) { g.TranslateTransform(208,0);g.ScaleTransform(-1,1); }
                g.DrawImage(pose,new RectangleF((208-w)/2,156-h-hop,w,h));
                // Pickup and carry sprites already contain the object between
                // upper/lower jaws. No toy is painted over a closed muzzle.
                if((mood==Mood.ToySettle || mood==Mood.ToyRise) && kind!=ToyKind.None) {
                    int phase=frame-8;float toyWidth=kind==ToyKind.Bone ? 34 : 29;
                    ToyArt.Draw(g,kind,new RectangleF(lieMouthX[phase]-toyWidth/2,lieMouthY[phase]-12,toyWidth,24));
                }
            }
            return output;
        }
        public static void SequencePreview(string path) {
            Mood[] moods={Mood.Walk,Mood.Run,Mood.Spin,Mood.Chase,Mood.Jump,Mood.Dead,Mood.Chew,Mood.Pet};
            string[] titles={"Ходьба · 8 фаз","Бег · 8 фаз","Кружись · 16 ракурсов","Лови хвост · 16 фаз","Прыжок · присесть → полёт → посадка","Умри · лечь → бок → спина","Косточка · взять → грызть","Погладить · глазки закрыты, мордочка вверх"};
            double[] spans={.64,.48,1.6,1.6,1.1,1.1,2.48,1.5};
            PreviewRows(path,moods,titles,spans,null);
        }
        public static void CommandsPreview(string path) {
            Mood[] moods={Mood.Sit,Mood.Lie,Mood.Bow,Mood.Bunny,Mood.PawLeft,Mood.PawRight,Mood.Bark,Mood.Sleep,Mood.Idle,Mood.Sniff,Mood.ToyPickup,Mood.ToyShake,Mood.ToyRoll,Mood.ToyToss,Mood.ToyChew,Mood.ToyPickup,Mood.ToyShake,Mood.ToyChew};
            string[] titles={"Сидеть · опустить задние лапки → сесть","Лежать · передние лапки → грудь → задние","Поклон · грудь вниз, задняя часть стоит","Зайка · сесть → поднять грудь → баланс","Дай левую лапку","Дай правую лапку","Голос · вдох → открыть пасть → гав → закрыть","Сон · лечь → свернуться → закрыть глазки","Внимание · ушки, взгляд, дыхание","Вынюхивать · мордочка у земли","Мячик · взять пастью","Мячик · трясти мордочкой, 16 фаз","Мячик · катить передними лапками","Мячик · замах → бросить → пустая пасть","Мячик · пожевать","Кабанчик · взять пастью","Кабанчик · трясти мордочкой, 16 фаз","Кабанчик · пожевать"};
            double[] spans={.85,.95,.8,.75,.8,.8,.7,1.1,2.4,1.2,.65,1.35,.5,.7,.96,.65,1.35,.96};
            ToyKind[] kinds=new ToyKind[moods.Length];for(int i=0;i<kinds.Length;i++) kinds[i]=i>=15 ? ToyKind.Boar : ToyKind.Ball;
            PreviewRows(path,moods,titles,spans,kinds);
        }
        static void PreviewRows(string path,Mood[] moods,string[] titles,double[] spans,ToyKind[] kinds) {
            using(Bitmap sheet=new Bitmap(1280,moods.Length*160)) using(Graphics g=Graphics.FromImage(sheet)) using(Font label=new Font("Segoe UI",11)) {
                g.Clear(Color.FromArgb(245,237,221));g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                for(int row=0;row<moods.Length;row++) {
                    using(Brush text=new SolidBrush(Color.FromArgb(56,45,40))) g.DrawString(titles[row],label,text,8,row*160+2);
                    for(int col=0;col<8;col++) {
                        double t=spans[row]*col/8+0.002;
                        using(Bitmap b=Draw(moods[row],t,true,false,0,kinds==null ? ToyKind.Bone : kinds[row])) g.DrawImage(b,new Rectangle(col*160+4,row*160+24,156,132));
                    }
                }
                sheet.Save(path,ImageFormat.Png);
            }
        }
        public static void Validate() {
            Mood[] dynamic={Mood.Walk,Mood.Run,Mood.Spin,Mood.Chase,Mood.Jump,Mood.Dead,Mood.Chew,Mood.Pet,Mood.Sit,Mood.Lie,Mood.Bow,Mood.Bunny,Mood.PawLeft,Mood.PawRight,Mood.Bark,Mood.Sleep,Mood.Idle,Mood.Sniff,Mood.Wake,Mood.RiseSit,Mood.RiseLie,Mood.RiseDead};
            double[] spans={.64,.48,1.6,1.6,1.1,1.05,2.48,.8,.85,.95,.8,.75,.8,.8,.7,1.1,2.4,1.2,1.1,.85,.95,1.05};
            int[] needed={8,8,16,16,8,8,21,5,8,8,8,8,8,8,8,8,8,8,8,8,8,8};
            for(int i=0;i<dynamic.Length;i++) AssertPhases(dynamic[i],ToyKind.Bone,spans[i],needed[i]);
            foreach(ToyKind kind in new ToyKind[] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) {
                AssertPhases(Mood.ToyPickup,kind,.65,8);AssertPhases(Mood.ToyCarry,kind,.64,8);AssertPhases(Mood.ToyRoll,kind,.5,4);AssertPhases(Mood.ToyToss,kind,.7,4);
                AssertPhases(Mood.ToySettle,kind,.95,8);AssertPhases(Mood.ToyRise,kind,.95,8);
                AssertPhases(Mood.ToyShake,kind,1.35,kind==ToyKind.Bone ? 4 : 16);AssertPhases(Mood.ToyChew,kind,1.28,kind==ToyKind.Bone ? 16 : 8);
            }
            if(FrameKey(Mood.Dead,2)!=FrameKey(Mood.Dead,9)) throw new Exception("Play-dead final pose is not held");
            if(FrameKey(Mood.Walk,.64)!=FrameKey(Mood.Walk,0)) throw new Exception("Walk loop does not close");
            if(FrameKey(Mood.ToyToss,.35,ToyKind.None,.7)!=FrameKey(Mood.ToyToss,.35,ToyKind.Ball,.7)) throw new Exception("Toss mouth is not empty at release");
            if(FrameKey(Mood.Bow,4.999)!=FrameKey(Mood.Bow,0)) throw new Exception("Bow does not return to standing");
            AssertPhases(Mood.FeedLower,ToyKind.None,1,4);AssertPhases(Mood.FeedChew,ToyKind.None,2.4,4);AssertPhases(Mood.FeedRaise,ToyKind.None,1,4);
            AssertPhases(Mood.BedLie,ToyKind.None,3.35,12);
            Console.WriteLine("PASS: 336 generated animation poses, 16-view turns, 8-phase commands and reverse recovery; mouth-grip pickup/carry for three toys, meal phases and attentive bed rest.");
        }
        static void AssertPhases(Mood mood,ToyKind kind,double span,int needed) {
            HashSet<string> keys=new HashSet<string>();
            for(int sample=0;sample<160;sample++) keys.Add(FrameKey(mood,span*sample/160,kind,DefaultDuration(mood)));
            if(keys.Count<needed) throw new Exception("Animation drops poses: "+mood+" "+kind+" "+keys.Count);
        }
    }
}
