using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using SkiaSharp;

namespace KitsuDesktop.Mac;

enum Mood { Walk, Run, Idle, Sniff, Sleep, Jump, Chase, Play, Follow, Chew, Bow, Bark, Spin, PawLeft, PawRight, Bunny, Sit, Lie, Dead, IconPlay, Pet, ToyPickup, ToyCarry, ToyShake, ToyRoll, ToyToss, ToyChew, Wake, RiseSit, RiseLie, RiseDead, ToySettle, ToyRise, GoBed, BedLie, BedSleep, LeaveBed, GoFood, FeedLower, FeedChew, FeedRaise, LeaveFood, HomeRise }
enum ToyKind { None, Ball, Bone, Boar }
enum HomeKind { Bed, Feeder }

// The original transparent animation artwork is decoded by Skia on both Intel
// and Apple Silicon. Every turn, step, roll and toy grip is a drawn pose.
static class MacArt {
    sealed class Atlas {
        public readonly SKBitmap[] Frames;
        public readonly string Name;
        public Atlas(string name) {
            Name = name;
            using SKBitmap atlas = Load(name);
            int stride = atlas.RowBytes;
            byte[] pixels = new byte[stride * atlas.Height];
            Marshal.Copy(atlas.GetPixels(), pixels, 0, pixels.Length);
            if (pixels[3] > 28) throw new InvalidDataException("Animation backdrop is not transparent: " + name);
            for (int p = 3; p < pixels.Length; p += 4) if (pixels[p] <= 2) pixels[p] = 0;
            Marshal.Copy(pixels, 0, atlas.GetPixels(), pixels.Length);
            int[] rows = new int[5]; rows[4] = atlas.Height;
            for (int r = 1; r < 4; r++) {
                int ideal = r * atlas.Height / 4, best = int.MaxValue;
                for (int y = ideal - atlas.Height / 24; y < ideal + atlas.Height / 24; y++) {
                    int count = 0;
                    for (int x = 0; x < atlas.Width; x++) if (pixels[y * stride + x * 4 + 3] > 28) count++;
                    int score = count * 1000 + Math.Abs(y - ideal);
                    if (score < best) { best = score; rows[r] = y; }
                }
            }
            int[,] cols = new int[4, 5];
            for (int r = 0; r < 4; r++) {
                cols[r, 4] = atlas.Width;
                for (int c = 1; c < 4; c++) {
                    int ideal = c * atlas.Width / 4, best = int.MaxValue;
                    for (int x = ideal - atlas.Width / 24; x < ideal + atlas.Width / 24; x++) {
                        int count = 0;
                        for (int y = rows[r] + 1; y < rows[r + 1] - 1; y++) if (pixels[y * stride + x * 4 + 3] > 28) count++;
                        int score = count * 1000 + Math.Abs(x - ideal);
                        if (score < best) { best = score; cols[r, c] = x; }
                    }
                }
            }
            Frames = new SKBitmap[16];
            for (int i = 0; i < 16; i++) {
                int x0 = cols[i / 4, i % 4], y0 = rows[i / 4], w = cols[i / 4, i % 4 + 1] - x0, h = rows[i / 4 + 1] - y0;
                bool[] mask = new bool[w * h], seen = new bool[w * h];
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) mask[y * w + x] = pixels[(y0 + y) * stride + (x0 + x) * 4 + 3] > 28;
                List<Rectangle> bounds = new(); List<int> sizes = new(), brightParts = new(); int largest = 0, mainPart = -1;
                int[] stack = new int[w * h];
                for (int start = 0; start < mask.Length; start++) if (mask[start] && !seen[start]) {
                    int top = 0, count = 0, bright = 0, l = w, t = h, r = 0, b = 0; stack[top++] = start; seen[start] = true;
                    while (top > 0) {
                        int at = stack[--top], x = at % w, y = at / w; count++;
                        int pixel = (y0 + y) * stride + (x0 + x) * 4;
                        if ((pixels[pixel + 2] > 140 && pixels[pixel + 1] > 85 && pixels[pixel] > 45) || (pixels[pixel + 1] > 110 && pixels[pixel] > 110 && pixels[pixel + 2] < 110)) bright++;
                        l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y);
                        if (x > 0) Push(at - 1, mask, seen, stack, ref top);
                        if (x < w - 1) Push(at + 1, mask, seen, stack, ref top);
                        if (y > 0) Push(at - w, mask, seen, stack, ref top);
                        if (y < h - 1) Push(at + w, mask, seen, stack, ref top);
                    }
                    bounds.Add(Rectangle.FromLTRB(l, t, r + 1, b + 1)); sizes.Add(count); brightParts.Add(bright);
                    if (count > largest) { largest = count; mainPart = bounds.Count - 1; }
                }
                Rectangle chosen = mainPart >= 0 ? bounds[mainPart] : Rectangle.Empty;
                if (name == "Kitsu.Comfort" && i < 8) for (int part = 0; part < bounds.Count; part++) {
                    Rectangle piece = bounds[part];
                    if (part != mainPart && sizes[part] > 30 && brightParts[part] > sizes[part] / 4 && piece.Left > chosen.Right - 25 && piece.Left < chosen.Right + 45 && piece.Bottom > chosen.Bottom - 40)
                        chosen = Rectangle.Union(chosen, piece);
                }
                if (name.EndsWith("Play") && i < 14 || name == "Kitsu.ToyChew" || name.StartsWith("Kitsu.Carry") && i < 8) for (int part = 0; part < bounds.Count; part++) {
                    Rectangle piece = bounds[part];
                    if (part != mainPart && sizes[part] > 40 && brightParts[part] > sizes[part] / 4 && piece.Left > chosen.Left + chosen.Width / 2 && piece.Left < chosen.Right + 65 && piece.Bottom > chosen.Top + chosen.Height / 2 && piece.Top < chosen.Bottom + 35)
                        chosen = Rectangle.Union(chosen, piece);
                }
                if (chosen.IsEmpty || largest < 500) throw new InvalidDataException("Empty or clipped animation pose: " + name + " " + i);
                chosen.Inflate(2, 2); chosen = Rectangle.Intersect(chosen, new Rectangle(1, 1, w - 2, h - 2)); chosen.Offset(x0, y0);
                using SKBitmap subset = new();
                if (!atlas.ExtractSubset(subset, new SKRectI(chosen.X, chosen.Y, chosen.Right, chosen.Bottom))) throw new InvalidDataException("Could not crop animation: " + name);
                Frames[i] = subset.Copy();
            }
        }
        static void Push(int at, bool[] mask, bool[] seen, int[] stack, ref int top) { if (mask[at] && !seen[at]) { seen[at] = true; stack[top++] = at; } }
        public float Fit(int start, int count, float maxW, float maxH) {
            int w = 0, h = 0;
            for (int i = start; i < start + count; i++) { w = Math.Max(w, Frames[i].Width); h = Math.Max(h, Frames[i].Height); }
            return Math.Min(maxW / w, maxH / h);
        }
    }

    static SKBitmap Load(string name) {
        using Stream stream = typeof(MacArt).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing artwork: " + name);
        using SKCodec codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid artwork: " + name);
        SKBitmap result = new(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        SKCodecResult decoded = codec.GetPixels(result.Info, result.GetPixels());
        if (decoded != SKCodecResult.Success) { result.Dispose(); throw new InvalidDataException("Could not decode artwork: " + name); }
        return result;
    }

    sealed class AlphaMask {
        public readonly byte[] Values;
        public readonly int Width, Height;
        public AlphaMask(SKBitmap source) {
            Width = source.Width; Height = source.Height; Values = new byte[Width * Height];
            byte[] pixels = new byte[source.RowBytes * Height]; Marshal.Copy(source.GetPixels(), pixels, 0, pixels.Length);
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) Values[y * Width + x] = pixels[y * source.RowBytes + x * 4 + 3];
        }
    }
    static readonly ConditionalWeakTable<AvaloniaBitmap, AlphaMask> masks = new();
    static readonly Dictionary<string, AvaloniaBitmap> rendered = new();
    static readonly SKBitmap bed = Load("Kitsu.Bed"), feeder = Load("Kitsu.Feeder");

    // Returned images are shared, immutable cached frames. The application must
    // not dispose them while a window still refers to one.
    static AvaloniaBitmap Complete(SKBitmap source) {
        using SKImage image = SKImage.FromBitmap(source);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using Stream stream = data.AsStream();
        AvaloniaBitmap result = new(stream);
        masks.Add(result, new AlphaMask(source));
        return result;
    }
    static SKBitmap Surface(int width, int height) => new(new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Bgra8888, SKAlphaType.Premul));
    static SKPaint Paint(SKColor color) => new() { Color = color, IsAntialias = true };
    static SKPaint ImagePaint() => new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };
    public static bool HitTest(AvaloniaBitmap bitmap, int x, int y) => bitmap != null && masks.TryGetValue(bitmap, out AlphaMask mask) && x >= 0 && x < mask.Width && y >= 0 && y < mask.Height && mask.Values[y * mask.Width + x] > 20;

        static readonly Atlas gait=new Atlas("Kitsu.Gait"),spin=new Atlas("Kitsu.Spin"),tail=new Atlas("Kitsu.Tail"),transitions=new Atlas("Kitsu.Transitions"),comfort=new Atlas("Kitsu.Comfort"),chew=new Atlas("Kitsu.Chew");
        static readonly Atlas ground=new Atlas("Kitsu.Ground"),posture=new Atlas("Kitsu.Posture"),paws=new Atlas("Kitsu.Paws"),ambient=new Atlas("Kitsu.Ambient"),restVoice=new Atlas("Kitsu.RestVoice");
        static readonly Atlas ballPlay=new Atlas("Kitsu.BallPlay"),boarPlay=new Atlas("Kitsu.BoarPlay"),bonePlay=new Atlas("Kitsu.BonePlay"),toyChew=new Atlas("Kitsu.ToyChew"),ballShake=new Atlas("Kitsu.BallShake"),boarShake=new Atlas("Kitsu.BoarShake");
        static readonly Atlas carryBall=new Atlas("Kitsu.CarryBall"),carryBone=new Atlas("Kitsu.CarryBone"),carryBoar=new Atlas("Kitsu.CarryBoar"),meals=new Atlas("Kitsu.Meals");
        static readonly Atlas settleBall=new Atlas("Kitsu.SettleBall"),settleBone=new Atlas("Kitsu.SettleBone"),settleBoar=new Atlas("Kitsu.SettleBoar");
        static readonly float walkFit=gait.Fit(0,8,198,148),runFit=gait.Fit(8,8,198,148),spinFit=spin.Fit(0,16,198,148),tailFit=tail.Fit(0,16,198,148);
        static readonly float jumpFit=transitions.Fit(0,8,198,145),deadFit=transitions.Fit(8,8,198,148),pickupFit=comfort.Fit(0,8,198,148),petFit=comfort.Fit(8,8,198,148),chewFit=chew.Fit(0,16,198,96);
        static readonly float sitFit=ground.Fit(0,8,198,148),lieFit=ground.Fit(8,8,198,148),bowFit=posture.Fit(0,8,198,148),bunnyFit=posture.Fit(8,8,198,152);
        static readonly float leftPawFit=paws.Fit(0,8,198,148),rightPawFit=paws.Fit(8,8,198,148),idleFit=ambient.Fit(0,8,198,148),sniffFit=ambient.Fit(8,8,198,148);
        static readonly float sleepFit=restVoice.Fit(0,8,198,148),barkFit=restVoice.Fit(8,8,198,148);
        static readonly float ballFit=ballPlay.Fit(0,16,198,148),boarFit=boarPlay.Fit(0,16,198,148),boneFit=bonePlay.Fit(0,16,198,148);
        static readonly float ballShakeFit=ballShake.Fit(0,16,198,148),boarShakeFit=boarShake.Fit(0,16,198,148),ballChewFit=toyChew.Fit(0,8,198,103),boarChewFit=toyChew.Fit(8,8,198,103);
        static readonly float carryBallFit=carryBall.Fit(0,16,198,148),carryBoneFit=carryBone.Fit(0,16,198,148),carryBoarFit=carryBoar.Fit(0,16,198,148),mealFit=meals.Fit(0,12,198,148),bedLieFit=meals.Fit(12,4,184,100);
        static readonly float settleBallFit=settleBall.Fit(0,16,198,148),settleBoneFit=settleBone.Fit(0,16,198,148),settleBoarFit=settleBoar.Fit(0,16,198,148);
        static int Loop(double time,double duration,int count) { return (int)(Math.Max(0,time)%duration/duration*count)%count; }
        static int Stage(double time,double[] boundaries) { int index=0; while(index<boundaries.Length && time>=boundaries[index]) index++; return index; }
        static int Advance(double time,double duration,int count) { return Math.Min(count-1,(int)(Math.Max(0,time)/duration*count)); }
        static int EnterHold(double time,double enter,int holdStart,double holdPeriod,double duration) {
            if(time<enter) return Advance(time,enter,8);
            if(!double.IsInfinity(duration) && time>=duration-enter) return 7-Advance(time-(duration-enter),enter,8);
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
        static Atlas SettleAtlas(ToyKind kind) { return kind==ToyKind.Boar ? settleBoar : kind==ToyKind.Bone ? settleBone : settleBall; }
        static float SettleFit(ToyKind kind) { return kind==ToyKind.Boar ? settleBoarFit : kind==ToyKind.Bone ? settleBoneFit : settleBallFit; }
        static string AtlasName(Atlas a) {
            if(a==gait) return "gait"; if(a==spin) return "spin"; if(a==tail) return "tail"; if(a==transitions) return "transition";
            if(a==comfort) return "comfort"; if(a==chew) return "bone-chew"; if(a==ground) return "ground"; if(a==posture) return "posture";
            if(a==paws) return "paw"; if(a==ambient) return "ambient"; if(a==restVoice) return "rest-voice";
            if(a==ballPlay) return "ball-play"; if(a==boarPlay) return "boar-play"; if(a==bonePlay) return "bone-play";
            if(a==ballShake) return "ball-shake"; if(a==boarShake) return "boar-shake";
            if(a==carryBall) return "carry-ball"; if(a==carryBone) return "carry-bone"; if(a==carryBoar) return "carry-boar"; if(a==meals) return "meals";
            if(a==settleBall) return "settle-ball"; if(a==settleBone) return "settle-bone"; if(a==settleBoar) return "settle-boar"; return "toy-chew";
        }
        static Atlas Select(Mood mood,double time,ToyKind kind,double duration,out int frame,out float fit,out float hop) {
            Atlas atlas=null; frame=0;fit=1;hop=0;
            switch(mood) {
                case Mood.Idle: atlas=ambient;frame=Loop(time,2.4,8);fit=idleFit;break;
                case Mood.Sniff: atlas=ambient;frame=8+Loop(time,1.2,8);fit=sniffFit;break;
                case Mood.Sit: atlas=ground;frame=EnterHold(time,.85,6,1.8,double.PositiveInfinity);fit=sitFit;break;
                case Mood.Lie: atlas=ground;frame=8+EnterHold(time,.95,6,1.8,double.PositiveInfinity);fit=lieFit;break;
                case Mood.BedLie:
                    if(time<.95) { atlas=ground;frame=8+Advance(time,.95,8);fit=lieFit; }
                    else { atlas=meals;frame=12+Loop(time-.95,2.4,4);fit=bedLieFit; } break;
                case Mood.BedSleep: atlas=restVoice;frame=EnterHold(time,1.1,6,2.4,double.PositiveInfinity);fit=sleepFit;break;
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
                case Mood.Sleep: atlas=restVoice;frame=EnterHold(time,1.1,6,2.4,double.PositiveInfinity);fit=sleepFit;break;
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
                case Mood.ToySettle:
                    if(kind==ToyKind.None) { atlas=ground;frame=8+Advance(time,.95,8);fit=lieFit; }
                    else { atlas=SettleAtlas(kind);frame=Advance(time,.95,8);fit=SettleFit(kind); } break;
                case Mood.ToyRise:
                    if(kind==ToyKind.None) { atlas=ground;frame=15-Advance(time,.95,8);fit=lieFit; }
                    else { atlas=SettleAtlas(kind);frame=8+Advance(time,.95,8);fit=SettleFit(kind); } break;
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
        static double DefaultDuration(Mood mood) { return mood==Mood.Bunny ? 8.5 : mood==Mood.Bow || mood==Mood.PawLeft || mood==Mood.PawRight ? 5 : double.PositiveInfinity; }
        public static string FrameKey(Mood mood,double time) { return FrameKey(mood,time,ToyKind.Bone,DefaultDuration(mood)); }
        public static string FrameKey(Mood mood,double time,ToyKind kind,double duration) {
            int frame;float fit,hop;Atlas a=Select(mood,time,kind,duration,out frame,out fit,out hop);
            return a==null ? "still:"+mood : AtlasName(a)+":"+frame;
        }



    public static AvaloniaBitmap DrawDog(Mood mood, double time, bool right, bool happy, float requestedHop, ToyKind kind = ToyKind.None, double duration = double.PositiveInfinity) {
        Atlas atlas = Select(mood, time, kind, duration, out int frame, out float fit, out float hop);
        if (atlas == null) throw new ArgumentOutOfRangeException(nameof(mood), mood, "Missing animation state");
        bool front = mood == Mood.PawLeft || mood == Mood.PawRight || mood == Mood.Bunny || mood == Mood.Sit || mood == Mood.RiseSit || mood == Mood.BedLie && time >= .95;
        bool mirror = !right && !front;
        // Quantise sub-pixel jump height only; poses retain the complete original
        // frame timing while the immutable image cache remains bounded.
        hop = (float)Math.Round(hop * 4) / 4;
        string key = $"dog:{atlas.Name}:{frame}:{mirror}:{fit}:{hop}";
        if (rendered.TryGetValue(key, out AvaloniaBitmap saved)) return saved;
        using SKBitmap output = Surface(208, 176);
        RenderPose(output, atlas, frame, fit, hop, mirror);
        return rendered[key] = Complete(output);
    }

    static void RenderPose(SKBitmap output, Atlas atlas, int frame, float fit, float hop, bool mirror) {
        using SKCanvas canvas = new(output); using SKPaint paint = ImagePaint();
        canvas.Clear(SKColors.Transparent);
        SKBitmap pose = atlas.Frames[frame]; float w = pose.Width * fit, h = pose.Height * fit;
        if (mirror) { canvas.Translate(208, 0); canvas.Scale(-1, 1); }
        canvas.DrawBitmap(pose, new SKRect((208 - w) / 2, 156 - h - hop, (208 + w) / 2, 156 - hop), paint);
    }

    // Does not initialise AppKit or require a logged-in desktop. CI validates
    // native Skia decoding and every state on each actual macOS architecture.
    public static string ValidateArtwork() {
        Atlas[] atlases = { gait, spin, tail, transitions, comfort, chew, ground, posture, paws, ambient, restVoice, ballPlay, boarPlay, bonePlay, toyChew, ballShake, boarShake, carryBall, carryBone, carryBoar, meals, settleBall, settleBone, settleBoar };
        int count = 0, renders = 0;
        foreach (Atlas atlas in atlases) foreach (SKBitmap pose in atlas.Frames) {
            if (pose.Width < 20 || pose.Height < 20) throw new InvalidDataException("Invalid pose in " + atlas.Name);
            count++;
        }
        if (count != 384) throw new InvalidDataException("Missing animation frames");
        if (typeof(MacArt).Assembly.GetManifestResourceNames().Count(n => n.StartsWith("Kitsu.")) != 27) throw new InvalidDataException("Missing embedded artwork");
        using SKBitmap output = Surface(208, 176);
        foreach (Mood mood in Enum.GetValues<Mood>()) foreach (ToyKind kind in Enum.GetValues<ToyKind>()) foreach (bool right in new[] { true, false }) {
            for (int step = 0; step < 12; step++) {
                double time = step * .16;
                Atlas atlas = Select(mood, time, kind, 6, out int frame, out float fit, out float hop);
                if (atlas == null || frame < 0 || frame >= 16 || !float.IsFinite(fit) || fit <= 0) throw new InvalidDataException("Invalid animation selector: " + mood);
                bool front = mood == Mood.PawLeft || mood == Mood.PawRight || mood == Mood.Bunny || mood == Mood.Sit || mood == Mood.RiseSit || mood == Mood.BedLie && time >= .95;
                RenderPose(output, atlas, frame, fit, hop, !right && !front);
                int visible = 0;
                for (int y = 1; y < 176; y += 5) for (int x = 1; x < 208; x += 5) if (output.GetPixel(x, y).Alpha > 20) visible++;
                if (visible < 20 || output.GetPixel(0, 0).Alpha != 0 || output.GetPixel(207, 175).Alpha != 0) throw new InvalidDataException("Empty or nontransparent render: " + mood);
                renders++;
            }
        }
        foreach (ToyKind kind in new[] { ToyKind.Ball, ToyKind.Bone, ToyKind.Boar }) for (int i = 0; i < 8; i++) {
            string expected = kind == ToyKind.Ball ? "settle-ball" : kind == ToyKind.Bone ? "settle-bone" : "settle-boar";
            double time = (i + .05) * .95 / 8;
            if (FrameKey(Mood.ToySettle, time, kind, 2) != expected + ":" + i || FrameKey(Mood.ToyRise, time, kind, 2) != expected + ":" + (i + 8)) throw new InvalidDataException("Toy leaves integrated jaw animation");
        }
        return $"PASS: 27 artwork resources, {count} animated poses, {renders} transparent renders, 48 integrated toy settle/rise frames";
    }

    public static void SavePreview(string path) {
        Mood[] moods = { Mood.Walk, Mood.Run, Mood.Spin, Mood.Chase, Mood.Jump, Mood.Dead, Mood.Pet, Mood.ToyCarry, Mood.ToySettle, Mood.ToyRise, Mood.FeedChew, Mood.BedLie };
        using SKBitmap sheet = Surface(208 * 8, 176 * moods.Length);
        using SKCanvas canvas = new(sheet); using SKPaint paint = ImagePaint();
        canvas.Clear(new SKColor(241, 234, 222));
        using SKBitmap dog = Surface(208, 176);
        for (int row = 0; row < moods.Length; row++) for (int column = 0; column < 8; column++) {
            Mood mood = moods[row]; double time = column * (row == 0 ? .08 : row == 1 ? .06 : row == 8 || row == 9 ? .95 / 8 : .16);
            ToyKind kind = row % 3 == 0 ? ToyKind.Ball : row % 3 == 1 ? ToyKind.Bone : ToyKind.Boar;
            Atlas atlas = Select(mood, time, kind, 6, out int frame, out float fit, out float hop);
            RenderPose(dog, atlas, frame, fit, hop, column >= 4);
            canvas.DrawBitmap(dog, column * 208, row * 176, paint);
        }
        using SKImage image = SKImage.FromBitmap(sheet); using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using Stream destination = File.Create(path); data.SaveTo(destination);
    }

    public static AvaloniaBitmap DrawBed(int width, int height, bool frontOnly = false) {
        string key = $"bed:{width}:{height}:{frontOnly}";
        if (rendered.TryGetValue(key, out AvaloniaBitmap saved)) return saved;
        using SKBitmap output = Surface(width, height);
        using (SKCanvas canvas = new(output)) using (SKPaint paint = ImagePaint()) {
            canvas.Clear(SKColors.Transparent);
            if (frontOnly) canvas.ClipRect(new SKRect(0, height * .73f, width, height));
            canvas.DrawBitmap(bed, new SKRect(0, 0, width, height), paint);
        }
        return rendered[key] = Complete(output);
    }

    public static AvaloniaBitmap DrawFeeder(int width, int height, double dispense, double food) {
        int pellets = (int)(Math.Clamp(food, 0, 1) * 48);
        int dispenseFrame = dispense >= 0 && dispense < 2.2 ? (int)(dispense * 60) : -1;
        string key = $"feeder:{width}:{height}:{dispenseFrame}:{pellets}";
        if (rendered.TryGetValue(key, out AvaloniaBitmap saved)) return saved;
        using SKBitmap output = Surface(width, height);
        using (SKCanvas canvas = new(output)) using (SKPaint imagePaint = ImagePaint()) using (SKPaint dark = Paint(new SKColor(105, 61, 32))) using (SKPaint light = Paint(new SKColor(173, 118, 64))) {
            canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(feeder, new SKRect(0, 0, width, height), imagePaint);
            canvas.Scale(width / 140f, height / 230f);
            for (int i = 0; i < pellets; i++) {
                float px = 70 + (float)Math.Sin(i * 2.39996) * Math.Min(28, 4 + (float)Math.Sqrt(i) * 4);
                float py = 185 + (float)Math.Cos(i * 2.39996) * Math.Min(7, 2 + (float)Math.Sqrt(i));
                canvas.DrawOval(new SKRect(px - 2, py - 1.5f, px + 2, py + 1.5f), dark);
                canvas.DrawOval(new SKRect(px - 1.5f, py - 1.2f, px + .8f, py + .1f), light);
            }
            if (dispenseFrame >= 0) for (int i = 0; i < 18; i++) {
                double age = dispenseFrame / 60.0 - i * .055;
                if (age < 0 || age > 1.15) continue;
                float t = (float)(age % .48 / .48);
                float px = 70 + (float)Math.Sin(i * 4.1) * 7 * t, py = 142 + 43 * t * t;
                canvas.DrawOval(new SKRect(px, py, px + 3.2f, py + 2.6f), dark);
                canvas.DrawOval(new SKRect(px + .3f, py + .3f, px + 2.1f, py + 1.3f), light);
            }
        }
        return rendered[key] = Complete(output);
    }

    public static AvaloniaBitmap DrawToy(ToyKind kind, int width = 42, int height = 36) {
        string key = $"toy:{kind}:{width}:{height}";
        if (rendered.TryGetValue(key, out AvaloniaBitmap saved)) return saved;
        using SKBitmap output = Surface(width, height);
        using (SKCanvas canvas = new(output)) {
            canvas.Clear(SKColors.Transparent); canvas.Scale(width / 42f, height / 36f);
            if (kind == ToyKind.Ball) DrawBall(canvas);
            else if (kind == ToyKind.Bone) DrawBone(canvas);
            else if (kind == ToyKind.Boar) DrawBoar(canvas);
        }
        return rendered[key] = Complete(output);
    }

    static void DrawBall(SKCanvas canvas) {
        using SKPaint shadow = Paint(new SKColor(0, 0, 0, 28)); canvas.DrawOval(new SKRect(7, 30, 36, 34), shadow);
        using SKPath sphere = new(); sphere.AddOval(new SKRect(5, 3, 37, 33));
        using SKPaint rubber = Paint(SKColors.White);
        rubber.Shader = SKShader.CreateRadialGradient(new SKPoint(15, 10), 25, new[] { new SKColor(129, 219, 215), new SKColor(34, 104, 129) }, SKShaderTileMode.Clamp);
        canvas.DrawPath(sphere, rubber); canvas.Save(); canvas.ClipPath(sphere);
        using SKPaint stripe = Paint(new SKColor(244, 209, 115)); stripe.Style = SKPaintStyle.Stroke; stripe.StrokeWidth = 5;
        using SKPath lines = new(); lines.MoveTo(11, 2); lines.CubicTo(18, 11, 13, 24, 22, 34); lines.MoveTo(4, 13); lines.CubicTo(15, 10, 29, 17, 38, 12); canvas.DrawPath(lines, stripe); canvas.Restore();
        using SKPaint outline = Paint(new SKColor(34, 91, 105)); outline.Style = SKPaintStyle.Stroke; outline.StrokeWidth = 1.1f; canvas.DrawPath(sphere, outline);
        using SKPaint shine = Paint(new SKColor(255, 255, 244, 185)); canvas.DrawOval(new SKRect(11, 7, 18, 10), shine);
    }
    static void DrawBone(SKCanvas canvas) {
        using SKPaint shadow = Paint(new SKColor(0, 0, 0, 28)); canvas.DrawOval(new SKRect(3, 29, 38, 33), shadow);
        using SKPath path = new(); path.MoveTo(11, 13); path.CubicTo(4, 2, -1, 10, 5, 17); path.CubicTo(-1, 24, 6, 31, 12, 23); path.LineTo(29, 23); path.CubicTo(36, 31, 43, 24, 37, 17); path.CubicTo(43, 10, 37, 2, 30, 13); path.Close();
        using SKPaint fill = Paint(SKColors.White); fill.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 5), new SKPoint(0, 29), new[] { new SKColor(255, 240, 204), new SKColor(195, 151, 94) }, SKShaderTileMode.Clamp); canvas.DrawPath(path, fill);
        using SKPaint outline = Paint(new SKColor(155, 115, 71)); outline.Style = SKPaintStyle.Stroke; outline.StrokeWidth = 1.1f; canvas.DrawPath(path, outline);
        using SKPaint shine = Paint(new SKColor(255, 250, 219, 210)); shine.Style = SKPaintStyle.Stroke; shine.StrokeWidth = 2; canvas.DrawLine(13, 15, 29, 15, shine);
    }
    static void DrawBoar(SKCanvas canvas) {
        using SKPaint shadow = Paint(new SKColor(0, 0, 0, 25)); canvas.DrawOval(new SKRect(3, 30, 40, 34), shadow);
        using SKPaint legs = Paint(new SKColor(113, 68, 65)); canvas.DrawOval(new SKRect(10, 23, 16, 32), legs); canvas.DrawOval(new SKRect(25, 23, 31, 32), legs);
        using SKPaint tailPaint = Paint(new SKColor(151, 94, 85)); tailPaint.Style = SKPaintStyle.Stroke; tailPaint.StrokeWidth = 2.5f;
        using SKPath tail = new(); tail.AddArc(new SKRect(1, 14, 9, 23), 10, 310); canvas.DrawPath(tail, tailPaint); canvas.DrawLine(7, 21, 9, 20, tailPaint);
        using SKPaint rubber = Paint(SKColors.White); rubber.Shader = SKShader.CreateRadialGradient(new SKPoint(21, 13), 21, new[] { new SKColor(216, 145, 130), new SKColor(137, 80, 75) }, SKShaderTileMode.Clamp); canvas.DrawOval(new SKRect(7, 10, 36, 29), rubber);
        using SKPaint outline = Paint(new SKColor(101, 60, 58)); outline.Style = SKPaintStyle.Stroke; outline.StrokeWidth = 1; canvas.DrawOval(new SKRect(7, 10, 36, 29), outline);
        using SKPaint ears = Paint(new SKColor(147, 81, 76)); Polygon(canvas, ears, 26, 15, 25, 5, 31, 10); Polygon(canvas, ears, 32, 13, 35, 6, 36, 17);
        using SKPaint inner = Paint(new SKColor(229, 166, 152)); Polygon(canvas, inner, 27, 12, 27, 8, 30, 11);
        using SKPaint face = Paint(new SKColor(176, 107, 96)); canvas.DrawOval(new SKRect(25, 12, 38, 26), face);
        using SKPaint snout = Paint(new SKColor(227, 158, 141)); canvas.DrawOval(new SKRect(31, 18, 41, 25), snout);
        using SKPaint nostril = Paint(new SKColor(113, 58, 58)); canvas.DrawOval(new SKRect(35, 20, 36.8f, 22.3f), nostril); canvas.DrawOval(new SKRect(38, 20, 39.5f, 22.3f), nostril);
        using SKPaint eye = Paint(new SKColor(42, 29, 29)); canvas.DrawOval(new SKRect(31, 15, 33.8f, 17.8f), eye);
        using SKPaint shine = Paint(new SKColor(255, 240, 205)); canvas.DrawOval(new SKRect(31.5f, 15, 32.5f, 16), shine);
        using SKPaint tusk = Paint(new SKColor(255, 235, 193)); Polygon(canvas, tusk, 32, 24, 30, 21, 30, 26);
        using SKPaint bristle = Paint(new SKColor(111, 64, 62)); bristle.Style = SKPaintStyle.Stroke; bristle.StrokeWidth = 1.1f;
        canvas.DrawLine(15, 11, 17, 7, bristle); canvas.DrawLine(18, 10, 20, 6, bristle); canvas.DrawLine(21, 10, 23, 7, bristle);
        using SKPaint highlight = Paint(new SKColor(250, 201, 176, 135)); highlight.Style = SKPaintStyle.Stroke; highlight.StrokeWidth = 1.5f;
        using SKPath arc = new(); arc.AddArc(new SKRect(12, 12, 29, 22), 205, 70); canvas.DrawPath(arc, highlight);
    }
    static void Polygon(SKCanvas canvas, SKPaint paint, params float[] points) {
        using SKPath path = new(); path.MoveTo(points[0], points[1]);
        for (int i = 2; i < points.Length; i += 2) path.LineTo(points[i], points[i + 1]);
        path.Close(); canvas.DrawPath(path, paint);
    }
}

