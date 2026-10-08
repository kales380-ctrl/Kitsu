using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KitsuDesktop {
    enum Mood { Walk, Run, Idle, Sniff, Sleep, Jump, Chase, Play, Follow, Chew, Bow, Bark, Spin, PawLeft, PawRight, Bunny, Sit, Lie, Dead, IconPlay, Pet, ToyPickup, ToyCarry, ToyShake, ToyRoll, ToyToss, ToyChew, Wake, RiseSit, RiseLie, RiseDead, ToySettle, ToyRise }

    class ToyForm : AlphaForm {
        public float X,Y,VX,VY;
        public bool Dragging,Held;
        public ToyKind Kind=ToyKind.Ball;
        public bool Bone { get { return Kind==ToyKind.Bone; } set { Kind=value ? ToyKind.Bone : ToyKind.Ball; } }
        Point grab;
        Point previous;
        Bitmap artwork;
        ToyKind artworkKind=ToyKind.None;
        readonly bool headless;
        public Action Released;
        public ToyForm() : this(false) { }
        public ToyForm(bool headlessTest) {
            headless=headlessTest;
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
            ClientSize=new Size(42,36); AutoScaleMode=AutoScaleMode.None;
            DoubleBuffered=true; Cursor=Cursors.Hand;
            MouseDown += delegate(object s,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { Dragging=true; grab=e.Location; previous=Cursor.Position; Capture=true; } };
            MouseMove += delegate { if(Dragging) { Point p=Cursor.Position; VX=Math.Max(-700,Math.Min(700,(p.X-previous.X)*30)); VY=Math.Max(-600,Math.Min(600,(p.Y-previous.Y)*30)); previous=p; X=p.X-grab.X+21; Y=p.Y-grab.Y+18; Locate(); } };
            MouseUp += delegate { if(Dragging) { Dragging=false; Capture=false; if(Released!=null) Released(); } };
            Shown+=delegate { RenderToy(); };
        }
        public void Locate() { Location=new Point((int)X-21,(int)Y-18); if(Visible) RenderToy(); }
        public void Hold(float mouthX,float mouthY) { Held=true; VX=0; VY=0; X=mouthX; Y=mouthY; Hide(); }
        public void Release(float atX,float atY,float velocityX,float velocityY,Rectangle area) {
            Held=false; X=Math.Max(area.Left+21,Math.Min(area.Right-21,atX)); Y=Math.Max(area.Top+18,Math.Min(area.Bottom-18,atY));
            VX=velocityX; VY=velocityY; Locate(); if(!Visible && !headless) Show(); RenderToy();
        }
        public void Step(double dt,Rectangle area) {
            if(Dragging || Held) return;
            if(Y>=area.Bottom-18 && VY==0) VX*=(float)Math.Pow(Kind==ToyKind.Ball ? .08 : Kind==ToyKind.Boar ? .025 : .01,dt);
            else VY+=(float)(950*dt);
            X+=VX*(float)dt; Y+=VY*(float)dt;
            if(X<area.Left+21) { X=area.Left+21; VX=Math.Abs(VX)*.7f; }
            if(X>area.Right-21) { X=area.Right-21; VX=-Math.Abs(VX)*.7f; }
            if(Y<area.Top+18) { Y=area.Top+18; VY=Math.Abs(VY)*.7f; }
            if(Y>area.Bottom-18) { Y=area.Bottom-18; VY=-Math.Abs(VY)*(Kind==ToyKind.Bone ? .25f : Kind==ToyKind.Boar ? .44f : .62f); VX*=.92f; if(Math.Abs(VY)<35) { VY=0; VX*=.9f; } }
            Locate();
        }
        void RenderToy() {
            if(!IsHandleCreated || !Visible || Held) return;
            if(artwork==null || artworkKind!=Kind) { if(artwork!=null) artwork.Dispose(); artwork=ToyArt.Draw(Kind,42,36); artworkKind=Kind; }
            Present(artwork);
        }
        protected override void Dispose(bool disposing) { if(disposing && artwork!=null) { artwork.Dispose(); artwork=null; } base.Dispose(disposing); }
    }

    class PetForm : AlphaForm {
        readonly Random rng=new Random();
        readonly Timer timer=new Timer();
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly System.Threading.EventWaitHandle stopSignal=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,"Local\\KitsuDesktopPet_Stop_v3");
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly NotifyIcon tray=new NotifyIcon();
        readonly ToolStripMenuItem pauseItem=new ToolStripMenuItem("Пауза");
        readonly ToolStripMenuItem followItem=new ToolStripMenuItem("Следовать за мышкой");
        readonly ToolStripMenuItem iconItem=new ToolStripMenuItem("Иногда играть с иконками");
        readonly ToolStripMenuItem restoreItem=new ToolStripMenuItem("Возвращать иконки на место");
        readonly ToolStripMenuItem soundItem=new ToolStripMenuItem("Звук лая");
        Rectangle area;
        float x,y,tx,ty;
        double last,timeline,until,bubbleUntil,happyUntil,stateStart,previousAnimation,nextIcon=75,lastIconMove;
        string bubble="Привет! Я Кицу ♥";
        Mood mood=Mood.Idle;
        Mood previousMood=Mood.Idle;
        bool right=true,dragging,paused,closing,moving,petResumePlay;
        Point dragOffset,dragStart;
        int scale=4;
        ToyForm toy;
        Icon petIcon;
        int chaseCount;
        double toyPlayUntil;
        Mood lastToyAction=Mood.Idle;
        bool tossReleased;
        float toyRollSpeed;
        double toyChewSeconds;
        DesktopIcons icons;
        bool iconCarrying,manualIcon;
        Point iconOffset;
        readonly bool smoke;
        int ticks;
        public PetForm() : this(false) { }
        public PetForm(bool smokeTest) {
            smoke=smokeTest;
            Text="Кицу — шипперке"; FormBorderStyle=FormBorderStyle.None;
            ShowInTaskbar=false; TopMost=true;
            DoubleBuffered=true; AutoScaleMode=AutoScaleMode.None; Cursor=Cursors.Hand;
            area=Screen.FromPoint(Cursor.Position).WorkingArea;
            x=area.Left+area.Width*.7f; y=area.Bottom-12; tx=x; ty=y;
            ResizePet(); SetMood(Mood.Idle,4); bubbleUntil=6;
            BuildMenu(); ContextMenuStrip=menu;
            using(Bitmap bmp=PixelDog.Draw(Mood.Idle,1,true,false,0)) {
                IntPtr handle=bmp.GetHicon();
                using(Icon temp=Icon.FromHandle(handle)) petIcon=(Icon)temp.Clone();
                DestroyIcon(handle);
            }
            Icon=petIcon; tray.Icon=petIcon; tray.Text="Кицу — ваша маленькая шипперке"; tray.ContextMenuStrip=menu; tray.Visible=!smoke;
            tray.DoubleClick += delegate { paused=false; pauseItem.Checked=false; TopMost=true; Speak("Я здесь! ♥",3); Jump(); };
            MouseDown+=OnDown; MouseMove+=OnMove; MouseUp+=OnUp;
            MouseDoubleClick+=delegate(object s,MouseEventArgs e) { if(e.Button==MouseButtons.Left) ThrowBall(); };
            clock.Restart(); last=0;
            timer.Interval=16; timer.Tick+=Tick; timer.Start();
            Shown+=delegate { Render(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        void DisplayChanged(object sender,EventArgs e) { if(!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { area=Screen.FromPoint(new Point((int)x,(int)y)).WorkingArea; Clamp(); }); }
        void BuildMenu() {
            var title=new ToolStripMenuItem("Кицу · шипперке ♀"); title.Enabled=false; menu.Items.Add(title);
            menu.Items.Add("Погладить ♥",null,delegate { Pet(); });
            menu.Items.Add("Бросить мячик",null,delegate { ThrowBall(); });
            menu.Items.Add("Дать игрушку-косточку",null,delegate { ThrowBall(true); });
            menu.Items.Add("Дать любимого резинового кабанчика",null,delegate { ThrowToy(ToyKind.Boar); });
            menu.Items.Add("Убрать игрушку",null,delegate { StopIcons(); RemoveToy(); Choose(); });
            var commands=new ToolStripMenuItem("Команды");
            commands.DropDownItems.Add("Поклон",null,delegate { Command(Mood.Bow,5,"Поклон!"); });
            commands.DropDownItems.Add("Прыжок",null,delegate { Command(Mood.Jump,1.1,"Оп!"); });
            commands.DropDownItems.Add("Голос",null,delegate { Command(Mood.Bark,1.5,"Гав-гав!"); if(soundItem.Checked) BarkSound.Play(); });
            commands.DropDownItems.Add("Кружись",null,delegate { Command(Mood.Spin,1.6,"Кружусь!"); });
            commands.DropDownItems.Add("Дай лапу",null,delegate { bool left=rng.Next(2)==0; Command(left ? Mood.PawLeft : Mood.PawRight,5,left ? "Левая лапка ♥" : "Правая лапка ♥"); });
            commands.DropDownItems.Add("Зайка",null,delegate { Command(Mood.Bunny,rng.Next(5,16)+1.5,"Зайка!"); });
            commands.DropDownItems.Add("Сидеть",null,delegate { Command(Mood.Sit,double.PositiveInfinity,"Сижу!"); });
            commands.DropDownItems.Add("Лежать",null,delegate { Command(Mood.Lie,double.PositiveInfinity,"Лежу!"); });
            commands.DropDownItems.Add("Умри",null,delegate { Command(Mood.Dead,10,"Лапки вверх!"); });
            commands.DropDownItems.Add(new ToolStripSeparator());
            commands.DropDownItems.Add("Гулять / отменить команду",null,delegate { ResumeWalking(); });
            menu.Items.Add(commands);
            menu.Items.Add("Ловить хвост",null,delegate { Wake(); followItem.Checked=false; StopIcons(); SetMood(Mood.Chase,3.2); Speak("Сейчас поймаю!",2); });
            menu.Items.Add("Спать / проснуться",null,delegate { DropHeldToy(); StopIcons(); if(mood==Mood.Sleep) { Wake(); Speak("Уже встала!",2); } else { followItem.Checked=false; SetMood(Mood.Sleep,double.PositiveInfinity); Speak("Спокойной ночи…",3); } });
            followItem.CheckOnClick=true; followItem.Click+=delegate { Wake(); StopIcons(); if(followItem.Checked) { RemoveToy(); SetMood(Mood.Follow,3600); Speak("Поиграем?",2); } else Choose(); }; menu.Items.Add(followItem);
            menu.Items.Add(new ToolStripSeparator());
            iconItem.Checked=!smoke; iconItem.CheckOnClick=true;
            iconItem.Click+=delegate { if(!iconItem.Checked) StopIcons(); nextIcon=timeline+90; }; menu.Items.Add(iconItem);
            restoreItem.Checked=true; restoreItem.CheckOnClick=true; menu.Items.Add(restoreItem);
            menu.Items.Add("Поиграть с иконкой сейчас",null,delegate { StartIcons(true); });
            soundItem.Checked=true; soundItem.CheckOnClick=true; menu.Items.Add(soundItem);
            menu.Items.Add(new ToolStripSeparator());
            pauseItem.CheckOnClick=true; pauseItem.Click+=delegate { paused=pauseItem.Checked; if(paused) StopIcons(); }; menu.Items.Add(pauseItem);
            var top=new ToolStripMenuItem("Поверх окон"); top.Checked=true; top.CheckOnClick=true;
            top.Click+=delegate { TopMost=top.Checked; if(toy!=null) toy.TopMost=TopMost; }; menu.Items.Add(top);
            var sizes=new ToolStripMenuItem("Размер");
            foreach(int n in new int[] {3,4,5}) { int size=n; sizes.DropDownItems.Add(n==3 ? "Маленькая" : n==4 ? "Обычная" : "Крупная",null,delegate { scale=size; ResizePet(); Clamp(); }); }
            menu.Items.Add(sizes);
            menu.Items.Add("На другой монитор",null,delegate { StopIcons(); RemoveToy(); Screen[] all=Screen.AllScreens; int idx=Array.FindIndex(all,s=>s.WorkingArea==area); area=all[(idx+1)%all.Length].WorkingArea; x=area.Left+area.Width/2; y=area.Bottom-12; Choose(); });
            menu.Items.Add("Как играть",null,delegate { Speak("Клик — ласка • два клика — мяч",5); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Закрыть Кицу",null,delegate { Close(); });
        }
        void ResizePet() { ClientSize=new Size(Math.Max(300,52*scale+96),44*scale+116); Place(); }
        void Place() { Location=new Point((int)x-ClientSize.Width/2,(int)y-(39*scale+48)); }
        void Clamp() { x=Math.Max(area.Left+26*scale,Math.Min(area.Right-26*scale,x)); y=Math.Max(area.Top+(mood==Mood.IconPlay ? 38*scale : 44*scale+48),Math.Min(area.Bottom-4,y)); Place(); }
        void Speak(string text,double seconds) { bubble=text; bubbleUntil=timeline+seconds; }
        void SetMood(Mood m,double seconds) { if(!IsToySequence(m)) DropHeldToy(); previousMood=mood; previousAnimation=Math.Max(0,timeline-stateStart); mood=m; stateStart=timeline; until=stateStart+seconds; moving=false; }
        void Command(Mood m,double seconds,string text) { Wake(); followItem.Checked=false; StopIcons(); DropHeldToy(); SetMood(m,seconds); Speak(text,Math.Min(3,seconds)); }
        void Wake() { paused=false; pauseItem.Checked=false; if(mood==Mood.Sleep) SetMood(Mood.Wake,1.1); }
        void ResumeWalking() {
            Wake(); StopIcons();
            if(mood==Mood.Sit) SetMood(Mood.RiseSit,.85);
            else if(mood==Mood.Lie) SetMood(Mood.RiseLie,.95);
            else if(mood==Mood.Dead) SetMood(Mood.RiseDead,1.05);
            else if(mood!=Mood.Wake) Choose();
        }
        void Pet() {
            Wake(); followItem.Checked=false; StopIcons();
            // The same toy is put beside her paw; interruptions never replace a
            // held toy with another object or leave its desktop window hidden.
            DropHeldToy();
            petResumePlay=toy!=null; happyUntil=timeline+3; SetMood(Mood.Pet,3);
            Speak(new string[] {"♥","М-м-м… ♥","Ещё за ушком!","Как приятно!"}[rng.Next(4)],2.5);
        }
        void Jump() { Wake(); StopIcons(); SetMood(Mood.Jump,1.1); }
        void OnDown(object sender,MouseEventArgs e) {
            if(e.Button!=MouseButtons.Left) return;
            StopIcons(); DropHeldToy(); if(IsToySequence(mood)) SetMood(Mood.Idle,2);
            dragging=true; Capture=true; dragStart=Cursor.Position; dragOffset=new Point(Cursor.Position.X-(int)x,Cursor.Position.Y-(int)y);
        }
        void OnMove(object sender,MouseEventArgs e) {
            if(dragging) { Point p=Cursor.Position; if(Math.Abs(p.X-dragStart.X)+Math.Abs(p.Y-dragStart.Y)>5) { x=p.X-dragOffset.X; y=p.Y-dragOffset.Y; area=Screen.FromPoint(p).WorkingArea; Clamp(); } }
        }
        void OnUp(object sender,MouseEventArgs e) {
            if(e.Button!=MouseButtons.Left || !dragging) return;
            dragging=false; Capture=false;
            if(Math.Abs(Cursor.Position.X-dragStart.X)+Math.Abs(Cursor.Position.Y-dragStart.Y)<=5) Pet();
            else { tx=x; ty=y; SetMood(Mood.Idle,2); Speak("Ух, новое место!",2); }
        }
        void RemoveToy() { petResumePlay=false; toyPlayUntil=0; tossReleased=false; if(toy!=null) { toy.Close(); toy.Dispose(); toy=null; } }
        void ThrowBall() { ThrowBall(false); }
        void ThrowBall(bool bone) { ThrowToy(bone ? ToyKind.Bone : ToyKind.Ball); }
        void ThrowToy(ToyKind kind) {
            Wake(); StopIcons(); followItem.Checked=false; RemoveToy(); toy=new ToyForm(smoke); toy.TopMost=TopMost;
            toy.Kind=kind;
            toy.Released=delegate { Wake(); StopIcons(); followItem.Checked=false; toyPlayUntil=timeline+180; StartToyFetch(); Speak(ToyGreeting(toy.Kind),2); };
            toy.Release(MouthX(),MouthY(),(right ? 1 : -1)*rng.Next(240,440),-380,area);
            chaseCount=0; lastToyAction=Mood.Idle; toyPlayUntil=timeline+180; StartToyFetch(); Speak(ToyGreeting(kind),2);
        }
        string ToyGreeting(ToyKind kind) { return kind==ToyKind.Bone ? "Моя косточка!" : kind==ToyKind.Boar ? "Любимый кабанчик! ♥" : "Лови мячик!"; }
        float MouthX() { return x+(right ? 65 : -65)*scale/4f; }
        float MouthY() { return y-75*scale/4f; }
        float TossX() { float offset=toy!=null && toy.Kind==ToyKind.Boar ? 52 : toy!=null && toy.Kind==ToyKind.Bone ? 56 : 57; return x+(right ? offset : -offset)*scale/4f; }
        float TossY() { float offset=toy!=null && toy.Kind==ToyKind.Boar ? 129 : toy!=null && toy.Kind==ToyKind.Bone ? 119 : 110; return y-offset*scale/4f; }
        void DropHeldToy() {
            if(toy==null || !toy.Held) return;
            toy.Release(x+(right ? 70 : -70)*scale/4f,Math.Min(area.Bottom-18,y-18),0,0,area);
            tossReleased=false;
        }
        void StartToyFetch() {
            if(toy==null) { Choose(); return; }
            if(toy.Held) DropHeldToy();
            if(toyPlayUntil<=timeline) toyPlayUntil=timeline+60;
            SetMood(Mood.Play,Math.Max(1,toyPlayUntil-timeline));
        }
        static bool IsToySequence(Mood m) { return m==Mood.ToyPickup || m==Mood.ToyCarry || m==Mood.ToyShake || m==Mood.ToyRoll || m==Mood.ToyToss || m==Mood.ToyChew || m==Mood.ToySettle || m==Mood.ToyRise; }
        void BeginToyAction(Mood action) {
            if(toy==null) { Choose(); return; }
            if(action!=Mood.ToyPickup) lastToyAction=action; tossReleased=false;
            if(action==Mood.ToyCarry) {
                // Carry the toy to another part of the working area, still using
                // the walking gait rather than sliding a fixed mouth pose.
                float edge=26*scale;
                tx=Math.Max(area.Left+edge,Math.Min(area.Right-edge,x+(rng.Next(2)==0 ? -1 : 1)*rng.Next(150,330)));
                ty=Math.Max(area.Top+44*scale+48,Math.Min(area.Bottom-4,y+rng.Next(-80,81)));
                double distance=Math.Sqrt((tx-x)*(tx-x)+(ty-y)*(ty-y));
                SetMood(action,Math.Max(.75,Math.Min(2.5,distance/170+.12)));
            } else if(action==Mood.ToyRoll) {
                float edge=26*scale;
                bool goRight=x<area.Left+area.Width*.35f || x<area.Right-300 && rng.Next(2)==0;
                right=goRight; toyRollSpeed=rng.Next(120,190);
                tx=Math.Max(area.Left+edge,Math.Min(area.Right-edge,x+(right ? 1 : -1)*toyRollSpeed*1.2f)); ty=y;
                SetMood(action,1.25);
            } else if(action==Mood.ToyPickup) SetMood(action,.65);
            else if(action==Mood.ToyShake) SetMood(action,1.35);
            else if(action==Mood.ToyToss) SetMood(action,.7);
            else { toyChewSeconds=rng.Next(6,11); SetMood(Mood.ToySettle,.95); }
            if(!toy.Held) toy.Hold(MouthX(),MouthY());
        }
        void NextToyAction() {
            if(toy==null) { Choose(); return; }
            // Boar is her favourite: she spends more quiet time chewing it, but
            // all three toys can be carried, shaken, paw-rolled, tossed and gnawed.
            Mood[] choices={Mood.ToyCarry,Mood.ToyShake,Mood.ToyRoll,Mood.ToyToss,Mood.ToyChew};
            int[] weights=toy.Kind==ToyKind.Boar ? new int[] {23,20,15,12,30} : toy.Kind==ToyKind.Bone ? new int[] {20,20,15,15,30} : new int[] {25,25,20,20,10};
            int total=0; for(int i=0;i<choices.Length;i++) if(choices[i]!=lastToyAction) total+=weights[i];
            int pick=rng.Next(total);
            for(int i=0;i<choices.Length;i++) if(choices[i]!=lastToyAction) { if(pick<weights[i]) { BeginToyAction(choices[i]); return; } pick-=weights[i]; }
        }
        void TickToyPlay(double dt,double now) {
            if(toy==null) { Choose(); return; }
            if(now>toyPlayUntil) { DropHeldToy(); toyPlayUntil=0; Choose(); return; }
            if(mood==Mood.Play) {
                float approach=toy.X>=x ? 1 : -1;
                if(Math.Abs(toy.X-x)>80) right=approach>0;
                MoveTowards(toy.X-approach*52*scale/4f,Math.Min(area.Bottom-4,toy.Y+18),340,dt);
                if(!toy.Dragging && Math.Abs(toy.X-x)<72*scale/4f && Math.Abs(toy.Y-y)<65 && Math.Abs(toy.VY)<140) {
                    right=toy.X>x; chaseCount++; happyUntil=now+1;
                    toy.Hold(MouthX(),MouthY()); BeginToyAction(Mood.ToyPickup);
                }
                return;
            }
            if(mood==Mood.ToyCarry) MoveTowards(tx,ty,170,dt);
            else if(mood==Mood.ToyRoll) MoveTowards(tx,ty,toyRollSpeed,dt);
            else if(mood==Mood.ToyToss && !tossReleased && now-stateStart+1e-7>=.35) {
                int direction=x<area.Left+200 ? 1 : x>area.Right-200 ? -1 : rng.Next(2)==0 ? -1 : 1;
                toy.Release(TossX(),TossY(),direction*rng.Next(240,441),-rng.Next(260,441),area);
                tossReleased=true; if(chaseCount%3==0) Speak("Лови!",1.5);
            }
            if(toy.Held) {
                toy.X=mood==Mood.ToyRoll ? x+(right ? 70 : -70)*scale/4f : MouthX();
                toy.Y=mood==Mood.ToyRoll ? y-18*scale/4f : MouthY();
            }
            if(now>=until) {
                if(mood==Mood.ToyToss) StartToyFetch();
                else if(mood==Mood.ToyRoll) BeginToyAction(Mood.ToyPickup);
                else if(mood==Mood.ToySettle) { SetMood(Mood.ToyChew,toyChewSeconds); Speak(toy.Kind==ToyKind.Boar ? "Люблю кабанчика ♥" : "Хрум-хрум",2); }
                else if(mood==Mood.ToyChew) SetMood(Mood.ToyRise,.95);
                else NextToyAction();
            }
        }
        void Choose() {
            if(followItem.Checked) { SetMood(Mood.Follow,3600); return; }
            if(toy!=null && timeline<toyPlayUntil) { StartToyFetch(); return; }
            int n=rng.Next(100);
            if(n<53) { tx=area.Left+26*scale+rng.Next(Math.Max(1,area.Width-52*scale));
                ty=rng.Next(4)==0 ? area.Top+Math.Max(44*scale+50,area.Height/2)+rng.Next(Math.Max(1,area.Height/2-45)) : area.Bottom-rng.Next(5,50);
                SetMood(n<24 ? Mood.Run : Mood.Walk,rng.Next(5,13));
            } else if(n<70) SetMood(Mood.Sniff,rng.Next(3,6));
            else if(n<78) SetMood(Mood.Chase,3.2);
            else if(n<85) SetMood(Mood.Jump,1.1);
            else if(n<90) SetMood(Mood.Sleep,rng.Next(18,40));
            else SetMood(Mood.Idle,rng.Next(3,7));
        }
        void StopIcons() {
            if(icons!=null) { icons.Dispose(); icons=null; }
            iconCarrying=false;
            if(mood==Mood.IconPlay) SetMood(Mood.Idle,2);
        }
        void StartIcons(bool manual) {
            if(smoke) return;
            StopIcons(); nextIcon=timeline+rng.Next(100,201);
            if(!manual && !DesktopIcons.DesktopIsForeground()) return;
            string reason; icons=DesktopIcons.Begin(rng,area,out reason);
            if(icons==null) { if(manual) Speak(reason,5); return; }
            Wake(); RemoveToy(); followItem.Checked=false; manualIcon=manual;
            icons.Restore=restoreItem.Checked;
            tx=icons.ScreenPosition.X+(icons.ScreenPosition.X<area.Left+area.Width/2 ? 80 : -80);
            tx=Math.Max(area.Left+26*scale,Math.Min(area.Right-26*scale,tx));
            ty=Math.Max(area.Top+38*scale,Math.Min(area.Bottom-4,icons.ScreenPosition.Y+100));
            SetMood(Mood.IconPlay,16); Speak("Что это тут?",2); lastIconMove=0;
        }
        void MoveTowards(float targetX,float targetY,double speed,double dt) {
            double dx=targetX-x,dy=targetY-y,d=Math.Sqrt(dx*dx+dy*dy);
            if(Math.Abs(dx)>2) right=dx>0;
            if(d>2) { double step=Math.Min(d,speed*dt); x+=(float)(dx/d*step); y+=(float)(dy/d*step); moving=step>0; }
        }
        void Tick(object sender,EventArgs e) {
            double wall=clock.Elapsed.TotalSeconds,dt=Math.Max(0,Math.Min(.05,wall-last)); last=wall;
            if(stopSignal.WaitOne(0)) { Close(); return; }
            ticks++;
            if(smoke && wall>3) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"smoke-test.txt"),"PASS: "+ticks+" timer frames; per-pixel window rendered without errors."); Close(); return; }
            // One logical clock drives both poses and movement. Pausing or a busy UI
            // cannot skip a gait, the crouch before a jump, or the roll onto her back.
            if(paused || dragging || menu.Visible) { Render(); return; }
            timeline+=dt; double now=timeline; moving=false;
            // Recheck work area to respect taskbar and resolution changes.
            area=Screen.FromPoint(new Point((int)x,(int)y)).WorkingArea;
            if(toy!=null) toy.Step(dt,area);
            if(iconItem.Checked && now>nextIcon && (mood==Mood.Idle || mood==Mood.Walk || mood==Mood.Sniff)) StartIcons(false);
            if(mood==Mood.IconPlay && icons!=null) {
                if(!manualIcon && !DesktopIcons.DesktopIsForeground()) StopIcons();
                else {
                    MoveTowards(tx,ty,100,dt);
                    if(!iconCarrying && Math.Abs(tx-x)<5 && Math.Abs(ty-y)<5) {
                        iconOffset=new Point(icons.ScreenPosition.X-(int)x,icons.ScreenPosition.Y-(int)y);
                        iconCarrying=true; tx=Math.Max(area.Left+125,Math.Min(area.Right-140,x+rng.Next(-150,151))); ty=Math.Max(area.Top+220,Math.Min(area.Bottom-25,y+rng.Next(-80,81)));
                        until=now+7; Speak("Я только поиграю!",2);
                    }
                    if(iconCarrying && now-lastIconMove>.15) {
                        lastIconMove=now;
                        Point carried=new Point((int)x+iconOffset.X,(int)y+iconOffset.Y);
                        carried.X=Math.Max(area.Left+24,Math.Min(area.Right-40,carried.X));
                        carried.Y=Math.Max(area.Top+24,Math.Min(area.Bottom-50,carried.Y));
                        if(!icons.Move(carried)) { StopIcons(); Speak("Оставлю на месте",2); }
                    }
                }
            } else if((mood==Mood.Play || IsToySequence(mood)) && toy!=null) {
                TickToyPlay(dt,now);
            } else if(mood==Mood.Follow) {
                Point p=Cursor.Position;
                float mx=Math.Max(area.Left+26*scale,Math.Min(area.Right-26*scale,p.X));
                float my=Math.Max(area.Top+44*scale+48,Math.Min(area.Bottom-4,p.Y+45));
                if(Math.Abs(mx-x)>65 || Math.Abs(my-y)>60) MoveTowards(mx,my,220,dt);
            } else if(mood==Mood.Walk || mood==Mood.Run) {
                MoveTowards(tx,ty,mood==Mood.Run ? 320 : 140,dt);
                if(Math.Abs(tx-x)<3 && Math.Abs(ty-y)<3) SetMood(Mood.Sniff,2);
            }
            if(now>until) {
                if(mood==Mood.Pet && petResumePlay && toy!=null) { petResumePlay=false; StartToyFetch(); }
                else if(mood==Mood.Sleep) SetMood(Mood.Wake,1.1);
                else if(mood==Mood.Dead) SetMood(Mood.RiseDead,1.05);
                else if(!IsToySequence(mood)) { StopIcons(); Choose(); }
            }
            Clamp(); Render();
        }
        void Render() {
            if(!IsHandleCreated) return;
            using(Bitmap canvas=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppPArgb)) using(Graphics g=Graphics.FromImage(canvas)) {
            double now=timeline;
            double anim=Math.Max(0,now-stateStart);
            Point p=Cursor.Position;
            bool near=Math.Abs(p.X-x)<110 && Math.Abs(p.Y-y+50)<100;
            bool happy=now<happyUntil || near && mood!=Mood.Sleep;
            int left=(ClientSize.Width-52*scale)/2,top=48;
            g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            Rectangle dogRect=new Rectangle(left,top,52*scale,44*scale);
            Mood renderMood=mood==Mood.Follow && !moving ? Mood.Idle : mood;
            ToyKind renderToy=toy!=null && (toy.Held || mood==Mood.ToyToss) ? toy.Kind : ToyKind.None;
            // Fast motion uses its own consecutive poses, without double limbs from
            // blending unrelated gait frames. A short dissolve softens quiet changes.
            bool quietToyTransition=mood==Mood.ToyChew && previousMood==Mood.ToySettle || mood==Mood.ToyRise && previousMood==Mood.ToyChew;
            double dissolve=quietToyTransition ? .18 : .12;
            bool crossfade=anim<dissolve && previousMood!=mood && (quietToyTransition || !IsSequence(mood) && !IsSequence(previousMood));
            if(crossfade) {
                using(Bitmap before=PixelDog.Draw(previousMood,previousAnimation,right,false,0,renderToy)) using(ImageAttributes opacity=new ImageAttributes()) {
                    ColorMatrix matrix=new ColorMatrix(); matrix.Matrix33=(float)(1-anim/dissolve); opacity.SetColorMatrix(matrix);
                    g.DrawImage(before,dogRect,0,0,before.Width,before.Height,GraphicsUnit.Pixel,opacity);
                }
                using(Bitmap after=PixelDog.Draw(renderMood,anim,right,happy,p.X>x ? 1 : -1,renderToy,until-stateStart)) using(ImageAttributes opacity=new ImageAttributes()) {
                    ColorMatrix matrix=new ColorMatrix(); matrix.Matrix33=(float)(anim/dissolve); opacity.SetColorMatrix(matrix);
                    g.DrawImage(after,dogRect,0,0,after.Width,after.Height,GraphicsUnit.Pixel,opacity);
                }
            } else using(Bitmap b=PixelDog.Draw(renderMood,anim,right,happy,p.X>x ? 1 : -1,renderToy,until-stateStart)) g.DrawImage(b,dogRect,0,0,b.Width,b.Height,GraphicsUnit.Pixel);
            if(now<happyUntil) {
                using(Brush heart=new SolidBrush(Color.FromArgb(246,142,162))) {
                    int hx=left+32*scale,hy=top+1+(int)(Math.Sin(anim*3)*3);
                    g.FillRectangle(heart,hx,hy,3*scale,scale); g.FillRectangle(heart,hx-scale,hy-scale,2*scale,scale); g.FillRectangle(heart,hx+2*scale,hy-scale,2*scale,scale); g.FillRectangle(heart,hx+scale,hy+scale,scale,scale);
                }
            }
            if(now<bubbleUntil) {
                using(Font font=new Font("Segoe UI",9)) {
                    Size sz=TextRenderer.MeasureText(bubble,font); int w=Math.Min(ClientSize.Width-4,sz.Width+14);
                    Rectangle box=new Rectangle((ClientSize.Width-w)/2,3,w,31);
                    using(Brush bg=new SolidBrush(Color.FromArgb(255,246,227))) g.FillRectangle(bg,box);
                    using(Pen pen=new Pen(Color.FromArgb(95,76,76),2)) g.DrawRectangle(pen,box);
                    using(Brush ink=new SolidBrush(Color.FromArgb(54,43,48))) using(StringFormat fmt=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap }) g.DrawString(bubble,font,ink,box,fmt);
                }
            }
            Present(canvas);
            }
        }
        static bool IsSequence(Mood m) { return m!=Mood.Idle && m!=Mood.Sniff; }
        protected override void OnFormClosed(FormClosedEventArgs e) {
            if(!closing) { closing=true; timer.Stop(); timer.Dispose(); StopIcons(); RemoveToy(); tray.Visible=false; tray.Dispose(); menu.Dispose(); petIcon.Dispose(); stopSignal.Dispose(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged; }
            base.OnFormClosed(e);
        }
    }

    static class Program {
        [STAThread] static void Main(string[] args) {
            try { Run(args); }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"kitsu-error.txt"),ex.ToString(),Encoding.UTF8); Environment.ExitCode=1; }
        }
        static void Run(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>0 && args[0]=="--preview") { Preview(args.Length>1 ? args[1] : "kitsu-preview.png"); return; }
            if(args.Length>0 && args[0]=="--sequence-preview") { PixelDog.SequencePreview(args.Length>1 ? args[1] : "kitsu-sequences.png"); return; }
            if(args.Length>0 && args[0]=="--commands-preview") { PixelDog.CommandsPreview(args.Length>1 ? args[1] : "kitsu-commands-v4.png"); return; }
            if(args.Length>0 && args[0]=="--self-test") { SelfTest(); return; }
            if(args.Length>0 && args[0]=="--bark-preview") { BarkSound.Save(args.Length>1 ? args[1] : "bark.wav"); return; }
            if(args.Length>0 && args[0]=="--smoke-test") { Application.Run(new PetForm(true)); return; }
            bool first; using(var mutex=new System.Threading.Mutex(true,"Local\\KitsuDesktopPet_v1",out first)) {
                if(!first) { MessageBox.Show("Кицу уже гуляет по рабочему столу. Её меню есть рядом с часами.","Кицу"); return; }
                Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"kitsu-error.txt"),e.Exception.ToString(),Encoding.UTF8); Application.Exit(); };
                Application.Run(new PetForm(args.Length>0 && args[0]=="--smoke-test"));
            }
        }
        static void SelfTest() {
            PixelDog.Validate();
            int frames=0;
            foreach(Mood mood in Enum.GetValues(typeof(Mood))) for(int frame=0;frame<12;frame++) foreach(bool right in new bool[] {true,false}) {
                using(Bitmap b=PixelDog.Draw(mood,frame/9.0,right,frame%2==0,1)) {
                    if(b.GetPixel(0,0).A!=0) throw new Exception("Transparency failed");
                    int opaque=0; for(int y=0;y<b.Height;y++) for(int x=0;x<b.Width;x++) if(b.GetPixel(x,y).A>0) opaque++;
                    if(opaque<2500 || opaque>32000) throw new Exception("Empty or opaque frame: "+mood+" "+opaque);
                    frames++;
                }
            }
            Rectangle area=new Rectangle(-1920,0,1920,1040);
            foreach(ToyKind kind in new ToyKind[] {ToyKind.Ball,ToyKind.Bone,ToyKind.Boar}) using(ToyForm toy=new ToyForm(true)) {
                toy.Kind=kind; toy.X=-1500; toy.Y=400; toy.VX=900; toy.VY=-600;
                for(int i=0;i<1800;i++) { toy.Step(1.0/30,area);
                    if(toy.X<area.Left+21 || toy.X>area.Right-21 || toy.Y<area.Top+18 || toy.Y>area.Bottom-18) throw new Exception("Toy escaped desktop");
                }
                if(Math.Abs(toy.VY)>1 || Math.Abs(toy.VX)>1) throw new Exception("Toy never settled");
            }
            Console.WriteLine("PASS: {0} sprite frames; ball, bone and boar bounce, remain inside a negative-coordinate monitor, and settle.",frames);
        }
        static void Preview(string path) {
            Mood[] moods={Mood.Idle,Mood.Walk,Mood.Run,Mood.Sit,Mood.Bow,Mood.Jump,Mood.Bark,Mood.Spin,Mood.PawLeft,Mood.PawRight,Mood.Bunny,Mood.Lie,Mood.Dead,Mood.Sleep,Mood.Chew,Mood.Follow};
            string[] labels={"Кицу · карие глазки","Прогулка","Бег","Сидеть","Поклон","Прыжок","Голос","Кружись","Левая лапка","Правая лапка","Зайка · 5–15 сек.","Лежать","Умри","Сон","Косточка","Следует за мышкой"};
            using(Bitmap sheet=new Bitmap(900,980)) using(Graphics g=Graphics.FromImage(sheet)) using(Font font=new Font("Segoe UI",13)) {
                g.Clear(Color.FromArgb(242,232,216)); g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                for(int i=0;i<moods.Length;i++) {
                    int ox=(i%4)*225,oy=(i/4)*245;
                    using(Brush bg=new SolidBrush(i%2==0 ? Color.FromArgb(229,214,191) : Color.FromArgb(236,223,201))) g.FillRectangle(bg,ox+8,oy+8,209,229);
                    using(Bitmap b=PixelDog.Draw(moods[i],1.3,i!=6,i==7,0)) {
                        if(b.Width!=208 || b.Height!=176) throw new Exception("Unexpected sprite size");
                        if(b.GetPixel(0,0).A!=0) throw new Exception("Sprite transparency is missing");
                        g.DrawImage(b,new Rectangle(ox+9,oy+34,208,176),0,0,b.Width,b.Height,GraphicsUnit.Pixel);
                    }
                    TextRenderer.DrawText(g,labels[i],font,new Rectangle(ox+8,oy+203,209,28),Color.FromArgb(59,51,49),TextFormatFlags.HorizontalCenter);
                }
                sheet.Save(path,ImageFormat.Png);
            }
        }
    }
}

