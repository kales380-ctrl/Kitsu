using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace KitsuDesktop.Mac;

internal readonly struct Vec
{
    public readonly float X, Y;
    public Vec(float x, float y) { X = x; Y = y; }
}

internal readonly struct WorkArea
{
    public readonly float Left, Top, Width, Height;
    public float Right => Left + Width;
    public float Bottom => Top + Height;
    public WorkArea(PixelRect r) { Left = r.X; Top = r.Y; Width = r.Width; Height = r.Height; }
    public WorkArea(float left, float top, float width, float height) { Left = left; Top = top; Width = width; Height = height; }
}

internal sealed class ActionCommand : ICommand
{
    private readonly Action action;
    public ActionCommand(Action action) => this.action = action;
    public bool CanExecute(object parameter) => true;
    public void Execute(object parameter) => action();
    public event EventHandler CanExecuteChanged { add { } remove { } }
}

internal sealed class MealSchedule
{
    private readonly string path;
    private readonly HashSet<string> served = new();
    private DateTime? observed;
    public MealSchedule(string path)
    {
        this.path = path;
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) foreach (string line in File.ReadAllLines(path)) served.Add(line.Trim()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public int Poll(DateTime now)
    {
        int due = 0;
        foreach (int hour in new[] { 10, 17, 22 })
        {
            DateTime slot = now.Date.AddHours(hour);
            bool minute = now >= slot && now < slot.AddMinutes(1);
            bool crossed = observed.HasValue && observed.Value < slot && now >= slot;
            if ((minute || crossed) && served.Add(slot.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture))) due++;
        }
        observed = now;
        if (due > 0 && !string.IsNullOrEmpty(path))
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path + ".tmp", served); File.Move(path + ".tmp", path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return due;
    }
}

internal abstract class OverlayWindow : Window
{
    protected OverlayWindow(string title, double width, double height)
    {
        Title = title; Width = width; Height = height;
        SystemDecorations = SystemDecorations.None; CanResize = false;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Cursor = new Cursor(StandardCursorType.Hand);
        Opened += (_, _) => NativeDesktop.ConfigureOverlay(this);
    }
    protected static PixelPoint Pointer(Window w, PointerEventArgs e)
    {
        PixelPoint point = w.PointToScreen(e.GetPosition(w));
        NativeDesktop.Observe(point); return point;
    }
    internal abstract void UpdateHitTest(PixelPoint cursor);
}

internal sealed class DesktopToy : OverlayWindow
{
    public float X, Y, VX, VY;
    public bool Dragging, Held;
    public readonly ToyKind Kind;
    public Action Released;
    private readonly Image image;
    private readonly Bitmap artwork;
    private PixelPoint grab, previous;
    public DesktopToy(ToyKind kind) : base("Игрушка Кицу", 42, 36)
    {
        Kind = kind; artwork = MacArt.DrawToy(kind, 42, 36);
        Content = image = new Image { Source = artwork, Stretch = Stretch.Fill };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Dragging = true; PixelPoint p = Pointer(this, e); previous = p;
            grab = new PixelPoint(p.X - Position.X, p.Y - Position.Y); e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            PixelPoint p = Pointer(this, e); if (!Dragging) return;
            VX = Math.Clamp((p.X - previous.X) * 30, -700, 700); VY = Math.Clamp((p.Y - previous.Y) * 30, -600, 600);
            previous = p; X = p.X - grab.X + 21; Y = p.Y - grab.Y + 18; Locate();
        };
        PointerReleased += (_, e) => { if (!Dragging) return; Dragging = false; e.Pointer.Capture(null); Released?.Invoke(); e.Handled = true; };
    }
    public void Locate() => Position = new PixelPoint((int)X - 21, (int)Y - 18);
    public void Hold(float mouthX, float mouthY) { Held = true; VX = VY = 0; X = mouthX; Y = mouthY; Hide(); }
    public void Release(float x, float y, float vx, float vy, WorkArea area)
    {
        Held = false; X = Math.Clamp(x, area.Left + 21, area.Right - 21); Y = Math.Clamp(y, area.Top + 18, area.Bottom - 18);
        VX = vx; VY = vy; Locate(); if (!IsVisible) Show();
    }
    public void Step(double dt, WorkArea area)
    {
        if (Dragging || Held) return;
        if (Y >= area.Bottom - 18 && VY == 0) VX *= (float)Math.Pow(Kind == ToyKind.Ball ? .08 : Kind == ToyKind.Boar ? .025 : .01, dt);
        else VY += (float)(950 * dt);
        X += VX * (float)dt; Y += VY * (float)dt;
        if (X < area.Left + 21) { X = area.Left + 21; VX = Math.Abs(VX) * .7f; }
        if (X > area.Right - 21) { X = area.Right - 21; VX = -Math.Abs(VX) * .7f; }
        if (Y < area.Top + 18) { Y = area.Top + 18; VY = Math.Abs(VY) * .7f; }
        if (Y > area.Bottom - 18)
        {
            Y = area.Bottom - 18; VY = -Math.Abs(VY) * (Kind == ToyKind.Bone ? .25f : Kind == ToyKind.Boar ? .44f : .62f); VX *= .92f;
            if (Math.Abs(VY) < 35) { VY = 0; VX *= .9f; }
        }
        Locate();
    }
    internal override void UpdateHitTest(PixelPoint cursor)
    {
        NativeDesktop.IgnoreMouse(this, !Dragging && (!IsVisible || Held || !MacArt.HitTest(artwork, cursor.X - Position.X, cursor.Y - Position.Y)));
    }
}

internal sealed class DesktopHome : OverlayWindow
{
    public readonly HomeKind Kind;
    public Action Clicked, Moved;
    public bool Dragging;
    public double DispenseUntil, DispenseStart, Food;
    public float Left => Position.X;
    public float Top => Position.Y;
    public float ObjectWidth => (float)Width;
    public float ObjectHeight => (float)Height;
    public Vec RestPoint => new(Left + ObjectWidth * .5f, Top + ObjectHeight * .78f);
    public Vec BowlPoint => new(Left + ObjectWidth * .5f, Top + ObjectHeight * .80f);
    public Vec FeedingPosition(bool right) => new(BowlPoint.X + (right ? -96 : 96) * ObjectWidth / 140f, BowlPoint.Y + 15 * ObjectHeight / 230f);
    private readonly Image image;
    private Bitmap artwork;
    private PixelPoint grab, start;
    private bool moved;
    private double lastFood = -1, lastDispense = -10;
    public DesktopHome(HomeKind kind, int scale) : base(kind == HomeKind.Bed ? "Лежанка Кицу" : "Кормушка Кицу", 1, 1)
    {
        Kind = kind; Content = image = new Image { Stretch = Stretch.Fill }; ResizeObject(scale);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Dragging = true; moved = false; start = Pointer(this, e);
            grab = new PixelPoint(start.X - Position.X, start.Y - Position.Y); e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            PixelPoint p = Pointer(this, e); if (!Dragging) return;
            if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 5) moved = true;
            if (moved) { Position = new PixelPoint(p.X - grab.X, p.Y - grab.Y); ClampTo(AreaAt(p)); }
        };
        PointerReleased += (_, e) =>
        {
            if (!Dragging) return; Dragging = false; e.Pointer.Capture(null); if (moved) Moved?.Invoke(); else Clicked?.Invoke(); e.Handled = true;
        };
    }
    private WorkArea AreaAt(PixelPoint p) => new(Screens.ScreenFromPoint(p)?.WorkingArea ?? Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 800));
    public void ResizeObject(int scale)
    {
        Width = (Kind == HomeKind.Bed ? 300 : 140) * scale / 4d; Height = (Kind == HomeKind.Bed ? 200 : 230) * scale / 4d;
        lastFood = -1; lastDispense = -10; Render(-1, Food);
    }
    public void ClampTo(WorkArea area) => Position = new PixelPoint((int)Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ObjectWidth)), (int)Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ObjectHeight)));
    public void Dispense(double time) { DispenseStart = time; DispenseUntil = time + 2.2; Food = 0; }
    public void Step(double time)
    {
        if (Kind == HomeKind.Bed) return;
        double dispensing = time < DispenseUntil ? time - DispenseStart : -1;
        if (dispensing >= 0) Food = Math.Min(1, dispensing / 1.7);
        // Feeding sprites update at 60 Hz; the static furniture need not be encoded
        // again until its pellets or portion change.
        double phase = dispensing < 0 ? -1 : Math.Floor(dispensing * 30) / 30;
        double portion = Math.Floor(Food * 48) / 48;
        if (phase != lastDispense || portion != lastFood) Render(phase, portion);
    }
    private void Render(double dispense, double food)
    {
        artwork = Kind == HomeKind.Bed ? MacArt.DrawBed((int)Width, (int)Height) : MacArt.DrawFeeder((int)Width, (int)Height, dispense, food);
        image.Source = artwork; lastDispense = dispense; lastFood = food;
    }
    internal override void UpdateHitTest(PixelPoint cursor) => NativeDesktop.IgnoreMouse(this, !Dragging && !MacArt.HitTest(artwork, cursor.X - Position.X, cursor.Y - Position.Y));
    protected override void OnClosed(EventArgs e) { base.OnClosed(e); image.Source = null; artwork = null; }
}

internal sealed class MacPet : OverlayWindow
{
    private readonly Random rng = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ContextMenu menu = new();
    private readonly Canvas canvas = new();
    private readonly Image dogImage = new() { Stretch = Stretch.Fill };
    private readonly Image beforeImage = new() { Stretch = Stretch.Fill };
    private readonly Image bedFront = new() { Stretch = Stretch.Fill };
    private readonly Border bubbleBox;
    private readonly TextBlock bubbleText;
    private readonly TrayIcon tray;
    private readonly bool smoke;
    private readonly List<DesktopToy> toys = new();
    private readonly DesktopHome bed, feeder;
    private readonly MealSchedule meals;
    private WorkArea area;
    private float x, y, tx, ty, toyRollSpeed;
    private double last, timeline, until, bubbleUntil, happyUntil, stateStart, previousAnimation, toyPlayUntil, toyChewSeconds, bedDuration;
    private Mood mood = Mood.Idle, previousMood = Mood.Idle, lastToyAction = Mood.Idle, homeNextMood, homeRiseMood;
    private bool right = true, dragging, paused, closing, moving, petResumePlay, tossReleased, follow, inBed, bedSleep, foodRight, sound = true;
    private int scale = 4, chaseCount, pendingMeals, ticks;
    private PixelPoint dragOffset, dragStart;
    private DesktopToy toy;
    private Bitmap frontArtwork, currentDog;
    private string bubble = "Привет! Я Кицу ♥";
    private readonly Dictionary<string, MenuItem> menuItems = new();
    private readonly Dictionary<string, NativeMenuItem> trayItems = new();
    internal Mood CurrentMood => mood;
    internal int ToyCount => toys.Count;

    public MacPet(bool smokeTest = false) : base("Кицу — шипперке", 304, 292)
    {
        smoke = smokeTest; Content = canvas;
        bubbleText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(54, 43, 48)), FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        bubbleBox = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 246, 227)), BorderBrush = new SolidColorBrush(Color.FromRgb(95, 76, 76)), BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = bubbleText, Height = 31 };
        canvas.Children.Add(beforeImage); canvas.Children.Add(dogImage); canvas.Children.Add(bedFront); canvas.Children.Add(bubbleBox);
        area = AreaAt(NativeDesktop.Pointer); x = area.Left + area.Width * .7f; y = area.Bottom - 12; tx = x; ty = y;
        ResizePet(); SetMood(Mood.Idle, 4); bubbleUntil = 6;
        bed = new DesktopHome(HomeKind.Bed, scale); feeder = new DesktopHome(HomeKind.Feeder, scale);
        bed.Clicked = () => GoToBed(false, double.PositiveInfinity); feeder.Clicked = StartMeal;
        bed.Moved = () => { SaveHome(); NativeDesktop.Front(this); };
        feeder.Moved = () => { if (FeedingActive()) RefreshFeedingSide(); SaveHome(); NativeDesktop.Front(this); };
        meals = new MealSchedule(smoke ? null : Path.Combine(MacProgram.DataDirectory, "meals.txt"));
        ResetHome(); LoadHome(); RebuildFront(); BuildMenus(); ContextMenu = menu;
        using (var iconData = new MemoryStream())
        {
            MacArt.DrawDog(Mood.Idle, 1, true, false, 0).Save(iconData); iconData.Position = 0;
            var icon = new WindowIcon(iconData); Icon = icon;
            tray = new TrayIcon { Icon = icon, ToolTipText = "Кицу — ваша маленькая шипперке", Menu = BuildNativeMenu(), IsVisible = !smoke };
        }
        if (!smoke && Application.Current != null) TrayIcon.SetIcons(Application.Current, new TrayIcons { tray });
        menu.Opened += (_, _) => { RefreshMenu(); NativeDesktop.IgnoreMouse(this, false); };
        menu.Closed += (_, _) => NativeDesktop.Front(this);
        PointerPressed += OnDown; PointerMoved += OnMove; PointerReleased += OnUp;
        Opened += (_, _) =>
        {
            bed.Show(); feeder.Show(); NativeDesktop.Front(this); Render();
            if (smoke) RunSmokeChecks();
            clock.Restart(); last = 0; timer.Start();
        };
        timer.Tick += Tick;
    }

    private WorkArea AreaAt(PixelPoint point) => new(Screens.ScreenFromPoint(point)?.WorkingArea ?? Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 800));
    private WorkArea ToyArea(DesktopToy item) => AreaAt(new PixelPoint((int)item.X, (int)item.Y));
    private void ResizePet()
    {
        Width = Math.Max(300, 52 * scale + 96); Height = 44 * scale + 116;
        foreach (Image image in new[] { dogImage, beforeImage }) { image.Width = 52 * scale; image.Height = 44 * scale; Canvas.SetLeft(image, (Width - image.Width) / 2); Canvas.SetTop(image, 48); }
        bubbleBox.Width = Width - 8; Canvas.SetLeft(bubbleBox, 4); Canvas.SetTop(bubbleBox, 3); Place();
    }
    private void Place() => Position = new PixelPoint((int)x - (int)Width / 2, (int)y - (39 * scale + 48));
    private void Clamp()
    {
        x = Math.Clamp(x, area.Left + 26 * scale, Math.Max(area.Left + 26 * scale, area.Right - 26 * scale));
        // The sprite's feet are 39*scale below its top. Empty speech-bubble space
        // may extend above the working area; a bed at its top still stays usable.
        y = Math.Clamp(y, area.Top + 39 * scale, Math.Max(area.Top + 39 * scale, area.Bottom - 4)); Place();
    }
    private void Speak(string text, double seconds) { bubble = text; bubbleUntil = timeline + seconds; }
    private void SetMood(Mood value, double seconds)
    {
        if (!IsToySequence(value)) DropHeldToy();
        if (value != Mood.BedLie && value != Mood.BedSleep && value != Mood.LeaveBed && value != Mood.Wake) inBed = false;
        previousMood = mood; previousAnimation = Math.Max(0, timeline - stateStart); mood = value; stateStart = timeline; until = stateStart + seconds; moving = false;
    }
    private void Command(Mood value, double seconds, string text) { Wake(); follow = false; DropHeldToy(); SetMood(value, seconds); Speak(text, Math.Min(3, seconds)); }
    private void Wake() { paused = false; if (mood == Mood.Sleep || mood == Mood.BedSleep) SetMood(Mood.Wake, 1.1); }
    private void ResumeWalking()
    {
        Wake(); follow = false; StopToyPlay();
        if (mood == Mood.Sit) SetMood(Mood.RiseSit, .85);
        else if (mood == Mood.BedLie) SetMood(Mood.LeaveBed, .95);
        else if (mood == Mood.Lie) SetMood(Mood.RiseLie, .95);
        else if (mood == Mood.Dead) SetMood(Mood.RiseDead, 1.05);
        else if (mood != Mood.Wake) Choose();
    }
    private void Pet()
    {
        Wake(); follow = false; DropHeldToy(); petResumePlay = toy != null && toyPlayUntil > timeline;
        happyUntil = timeline + 3; SetMood(Mood.Pet, 3);
        Speak(new[] { "♥", "М-м-м… ♥", "Ещё за ушком!", "Как приятно!" }[rng.Next(4)], 2.5);
    }
    private void OnDown(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount >= 2) { ThrowToy(ToyKind.Ball); e.Handled = true; return; }
        DropHeldToy(); if (IsToySequence(mood)) SetMood(Mood.Idle, 2);
        dragging = true; dragStart = Pointer(this, e); dragOffset = new PixelPoint(dragStart.X - (int)x, dragStart.Y - (int)y); e.Pointer.Capture(this);
    }
    private void OnMove(object sender, PointerEventArgs e)
    {
        PixelPoint p = Pointer(this, e); if (!dragging) return;
        if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) > 5) { x = p.X - dragOffset.X; y = p.Y - dragOffset.Y; area = AreaAt(p); inBed = false; Clamp(); }
    }
    private void OnUp(object sender, PointerReleasedEventArgs e)
    {
        if (!dragging) return; PixelPoint p = Pointer(this, e); dragging = false; e.Pointer.Capture(null);
        if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) <= 5) Pet();
        else { tx = x; ty = y; SetMood(Mood.Idle, 2); Speak("Ух, новое место!", 2); }
    }
    private static bool IsToySequence(Mood value) => value == Mood.ToyPickup || value == Mood.ToyCarry || value == Mood.ToyShake || value == Mood.ToyRoll || value == Mood.ToyToss || value == Mood.ToyChew || value == Mood.ToySettle || value == Mood.ToyRise;
    private void StopToyPlay() { DropHeldToy(); petResumePlay = false; toyPlayUntil = 0; tossReleased = false; }
    private void RemoveToys()
    {
        StopToyPlay(); toy = null; foreach (DesktopToy item in toys) { item.Released = null; item.Close(); } toys.Clear();
    }
    private void SelectToy(DesktopToy selected)
    {
        if (!ReferenceEquals(toy, selected)) DropHeldToy(); toy = selected; petResumePlay = false; chaseCount = 0; lastToyAction = Mood.Idle; tossReleased = false;
    }
    private void ThrowToy(ToyKind kind)
    {
        Wake(); follow = false;
        DesktopToy selected = toys.Find(item => item.Kind == kind);
        if (selected == null)
        {
            selected = new DesktopToy(kind); toys.Add(selected); DesktopToy releasedToy = selected;
            selected.Released = () => { if (closing || !toys.Contains(releasedToy)) return; Wake(); follow = false; SelectToy(releasedToy); toyPlayUntil = timeline + 180; StartToyFetch(); Speak(ToyGreeting(releasedToy.Kind), 2); NativeDesktop.Front(this); };
        }
        DropHeldToy(); SelectToy(selected); toy.Topmost = Topmost;
        toy.Release(MouthX(), MouthY(), (right ? 1 : -1) * rng.Next(240, 440), -380, area);
        toyPlayUntil = timeline + 180; StartToyFetch(); Speak(ToyGreeting(kind), 2); NativeDesktop.Front(this);
    }
    private static string ToyGreeting(ToyKind kind) => kind == ToyKind.Bone ? "Моя косточка!" : kind == ToyKind.Boar ? "Любимый кабанчик! ♥" : "Лови мячик!";
    private float MouthX() => x + (right ? 65 : -65) * scale / 4f;
    private float MouthY() => y - 75 * scale / 4f;
    private void DropHeldToy()
    {
        if (toy == null || !toy.Held) return;
        toy.Release(x + (right ? 70 : -70) * scale / 4f, Math.Min(area.Bottom - 18, y - 18), 0, 0, area); tossReleased = false; NativeDesktop.Front(this);
    }
    private void StartToyFetch()
    {
        if (toy == null) { Choose(); return; } if (toy.Held) DropHeldToy();
        if (toyPlayUntil <= timeline) toyPlayUntil = timeline + 60;
        SetMood(Mood.Play, Math.Max(1, toyPlayUntil - timeline));
    }
    private void BeginToyAction(Mood action)
    {
        if (toy == null) { Choose(); return; }
        if (action != Mood.ToyPickup) lastToyAction = action; tossReleased = false;
        if (action == Mood.ToyCarry)
        {
            float edge = 26 * scale; tx = Math.Clamp(x + (rng.Next(2) == 0 ? -1 : 1) * rng.Next(150, 330), area.Left + edge, area.Right - edge);
            ty = Math.Clamp(y + rng.Next(-80, 81), area.Top + 44 * scale + 48, area.Bottom - 4);
            double distance = Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y)); SetMood(action, Math.Clamp(distance / 170 + .12, .75, 2.5));
        }
        else if (action == Mood.ToyRoll)
        {
            right = x < area.Left + area.Width * .35f || x < area.Right - 300 && rng.Next(2) == 0; toyRollSpeed = rng.Next(120, 190);
            tx = Math.Clamp(x + (right ? 1 : -1) * toyRollSpeed * 1.2f, area.Left + 26 * scale, area.Right - 26 * scale); ty = y; SetMood(action, 1.25);
        }
        else if (action == Mood.ToyPickup) SetMood(action, .65);
        else if (action == Mood.ToyShake) SetMood(action, 1.35);
        else if (action == Mood.ToyToss) SetMood(action, .7);
        else { toyChewSeconds = rng.Next(6, 11); SetMood(Mood.ToySettle, .95); }
        if (!toy.Held) toy.Hold(MouthX(), MouthY());
    }
    private void NextToyAction()
    {
        if (toy == null) { Choose(); return; }
        Mood[] choices = { Mood.ToyCarry, Mood.ToyShake, Mood.ToyRoll, Mood.ToyToss, Mood.ToyChew };
        int[] weights = toy.Kind == ToyKind.Boar ? new[] { 23, 20, 15, 12, 30 } : toy.Kind == ToyKind.Bone ? new[] { 20, 20, 15, 15, 30 } : new[] { 25, 25, 20, 20, 10 };
        int total = 0; for (int i = 0; i < choices.Length; i++) if (choices[i] != lastToyAction) total += weights[i];
        int pick = rng.Next(total);
        for (int i = 0; i < choices.Length; i++) if (choices[i] != lastToyAction) { if (pick < weights[i]) { BeginToyAction(choices[i]); return; } pick -= weights[i]; }
    }
    private void TickToyPlay(double dt)
    {
        if (toy == null) { Choose(); return; }
        if (timeline > toyPlayUntil) { StopToyPlay(); Choose(); return; }
        if (mood == Mood.Play)
        {
            float approach = toy.X >= x ? 1 : -1; if (Math.Abs(toy.X - x) > 80) right = approach > 0;
            MoveTowards(toy.X - approach * 52 * scale / 4f, Math.Min(area.Bottom - 4, toy.Y + 18), 340, dt);
            if (!toy.Dragging && Math.Abs(toy.X - x) < 72 * scale / 4f && Math.Abs(toy.Y - y) < 65 && Math.Abs(toy.VY) < 140)
            { right = toy.X > x; chaseCount++; happyUntil = timeline + 1; toy.Hold(MouthX(), MouthY()); BeginToyAction(Mood.ToyPickup); }
            return;
        }
        if (mood == Mood.ToyCarry) MoveTowards(tx, ty, 170, dt);
        else if (mood == Mood.ToyRoll) MoveTowards(tx, ty, toyRollSpeed, dt);
        else if (mood == Mood.ToyToss && !tossReleased && timeline - stateStart + 1e-7 >= .35)
        {
            int direction = x < area.Left + 200 ? 1 : x > area.Right - 200 ? -1 : rng.Next(2) == 0 ? -1 : 1;
            float offsetX = toy.Kind == ToyKind.Boar ? 52 : toy.Kind == ToyKind.Bone ? 56 : 57;
            float offsetY = toy.Kind == ToyKind.Boar ? 129 : toy.Kind == ToyKind.Bone ? 119 : 110;
            toy.Release(x + (right ? offsetX : -offsetX) * scale / 4f, y - offsetY * scale / 4f, direction * rng.Next(240, 441), -rng.Next(260, 441), area);
            tossReleased = true; if (chaseCount % 3 == 0) Speak("Лови!", 1.5);
        }
        if (toy.Held) { toy.X = mood == Mood.ToyRoll ? x + (right ? 70 : -70) * scale / 4f : MouthX(); toy.Y = mood == Mood.ToyRoll ? y - 18 * scale / 4f : MouthY(); }
        if (timeline >= until)
        {
            if (mood == Mood.ToyToss) StartToyFetch();
            else if (mood == Mood.ToyRoll) BeginToyAction(Mood.ToyPickup);
            else if (mood == Mood.ToySettle) { SetMood(Mood.ToyChew, toyChewSeconds); Speak(toy.Kind == ToyKind.Boar ? "Люблю кабанчика ♥" : "Хрум-хрум", 2); }
            else if (mood == Mood.ToyChew) SetMood(Mood.ToyRise, .95);
            else NextToyAction();
        }
    }
    private void ResetHome()
    {
        bed.Position = new PixelPoint((int)area.Left + 25, (int)(area.Bottom - bed.Height - 8));
        feeder.Position = new PixelPoint((int)(area.Right - feeder.Width - 25), (int)(area.Bottom - feeder.Height - 8));
        bed.ClampTo(area); feeder.ClampTo(area);
    }
    private void LoadHome()
    {
        if (smoke) return;
        try
        {
            string[] values = File.ReadAllLines(Path.Combine(MacProgram.DataDirectory, "home.txt"));
            if (values.Length == 4 && int.TryParse(values[0], out int bx) && int.TryParse(values[1], out int by) && int.TryParse(values[2], out int fx) && int.TryParse(values[3], out int fy))
            { bed.Position = new PixelPoint(bx, by); feeder.Position = new PixelPoint(fx, fy); bed.ClampTo(AreaAt(bed.Position)); feeder.ClampTo(AreaAt(feeder.Position)); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void SaveHome()
    {
        if (smoke) return;
        try { Directory.CreateDirectory(MacProgram.DataDirectory); File.WriteAllLines(Path.Combine(MacProgram.DataDirectory, "home.txt"), new[] { bed.Position.X.ToString(), bed.Position.Y.ToString(), feeder.Position.X.ToString(), feeder.Position.Y.ToString() }); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void RebuildFront()
    {
        frontArtwork = MacArt.DrawBed((int)bed.Width, (int)bed.Height, true); bedFront.Source = frontArtwork;
    }
    private static bool IsFeeding(Mood value) => value == Mood.GoFood || value == Mood.FeedLower || value == Mood.FeedChew || value == Mood.FeedRaise || value == Mood.LeaveFood;
    private static bool IsHomeAction(Mood value) => value == Mood.HomeRise || value == Mood.GoBed || value == Mood.BedLie || value == Mood.BedSleep || value == Mood.LeaveBed || IsFeeding(value);
    private bool FeedingActive() => IsFeeding(mood) || mood == Mood.HomeRise && homeNextMood == Mood.GoFood;
    private void GoToBed(bool sleep, double seconds)
    {
        paused = false; DropHeldToy(); follow = false; toyPlayUntil = 0; bedSleep = sleep; bedDuration = seconds; PrepareHomeTravel(Mood.GoBed);
        Speak(sleep ? "Иду в свою лежанку спать…" : "Иду на место ♥", 3); NativeDesktop.Front(this);
    }
    private void PrepareHomeTravel(Mood destination)
    {
        Mood old = mood;
        if (old == Mood.Sleep || old == Mood.BedSleep || old == Mood.BedLie || old == Mood.Lie || old == Mood.Sit || old == Mood.Dead || old == Mood.ToyChew)
        {
            homeNextMood = destination; homeRiseMood = old == Mood.Sleep || old == Mood.BedSleep ? Mood.Wake : old == Mood.Sit ? Mood.RiseSit : old == Mood.Dead ? Mood.RiseDead : Mood.RiseLie;
            bool wasInBed = inBed; SetMood(Mood.HomeRise, homeRiseMood == Mood.Wake ? 1.1 : homeRiseMood == Mood.RiseDead ? 1.05 : .95); inBed = wasInBed;
        }
        else SetMood(destination, double.PositiveInfinity);
    }
    private void StartMeal()
    {
        if (FeedingActive()) return;
        paused = false; DropHeldToy(); follow = false; toyPlayUntil = 0; foodRight = x <= feeder.BowlPoint.X;
        RefreshFeedingSide();
        feeder.Dispense(timeline); PrepareHomeTravel(Mood.GoFood); Speak("Пора кушать!", 3); NativeDesktop.Front(this);
    }
    private void RefreshFeedingSide()
    {
        WorkArea diningArea = AreaAt(feeder.Position); Vec stand = feeder.FeedingPosition(foodRight);
        if (stand.X < diningArea.Left + 26 * scale || stand.X > diningArea.Right - 26 * scale) foodRight = !foodRight;
    }
    private void TickHome(double dt)
    {
        if (mood == Mood.HomeRise)
        {
            if (inBed) { Vec rest = bed.RestPoint; x = rest.X; y = rest.Y; }
            if (timeline >= until) SetMood(homeNextMood, double.PositiveInfinity);
        }
        else if (mood == Mood.GoBed)
        {
            Vec rest = bed.RestPoint; MoveTowards(rest.X, rest.Y, 210, dt);
            if (Math.Abs(x - rest.X) < 3 && Math.Abs(y - rest.Y) < 3)
            { x = rest.X; y = rest.Y; right = true; SetMood(bedSleep ? Mood.BedSleep : Mood.BedLie, bedDuration); inBed = true; Speak(bedSleep ? "Спокойной ночи…" : "Я на месте. Смотрю на тебя ♥", 3); }
        }
        else if (mood == Mood.BedLie || mood == Mood.BedSleep || mood == Mood.LeaveBed)
        {
            Vec rest = bed.RestPoint; x = rest.X; y = rest.Y; inBed = true;
            if (timeline >= until)
            {
                if (mood == Mood.BedSleep) SetMood(Mood.Wake, 1.1);
                else if (mood == Mood.BedLie) SetMood(Mood.LeaveBed, .95);
                else { inBed = false; Choose(); }
            }
        }
        else if (mood == Mood.GoFood)
        {
            Vec stand = feeder.FeedingPosition(foodRight); MoveTowards(stand.X, stand.Y, 230, dt);
            if (Math.Abs(x - stand.X) < 3 && Math.Abs(y - stand.Y) < 3 && timeline >= feeder.DispenseUntil)
            { x = stand.X; y = stand.Y; right = foodRight; SetMood(Mood.FeedLower, .48); }
        }
        else if (mood == Mood.FeedLower || mood == Mood.FeedChew || mood == Mood.FeedRaise)
        {
            Vec stand = feeder.FeedingPosition(foodRight); x = stand.X; y = stand.Y; right = foodRight;
            if (mood == Mood.FeedChew) feeder.Food = Math.Max(0, 1 - (timeline - stateStart) / 12);
            if (timeline >= until)
            {
                if (mood == Mood.FeedLower) SetMood(Mood.FeedChew, 12);
                else if (mood == Mood.FeedChew) { feeder.Food = 0; SetMood(Mood.FeedRaise, .48); Speak("Спасибо, вкусно! ♥", 2.5); }
                else { tx = Math.Clamp(x + (foodRight ? -170 : 170), area.Left + 26 * scale, area.Right - 26 * scale); ty = Math.Min(area.Bottom - 4, y + 15); SetMood(Mood.LeaveFood, 1.3); }
            }
        }
        else if (mood == Mood.LeaveFood) { MoveTowards(tx, ty, 160, dt); if (timeline >= until) Choose(); }
    }
    private void Choose()
    {
        if (follow) { SetMood(Mood.Follow, 3600); return; }
        if (toy != null && timeline < toyPlayUntil) { StartToyFetch(); return; }
        int n = rng.Next(100);
        if (n < 53)
        {
            tx = area.Left + 26 * scale + rng.Next(Math.Max(1, (int)area.Width - 52 * scale));
            ty = rng.Next(4) == 0 ? area.Top + Math.Max(44 * scale + 50, area.Height / 2) + rng.Next(Math.Max(1, (int)area.Height / 2 - 45)) : area.Bottom - rng.Next(5, 50);
            SetMood(n < 24 ? Mood.Run : Mood.Walk, rng.Next(5, 13));
        }
        else if (n < 70) SetMood(Mood.Sniff, rng.Next(3, 6));
        else if (n < 78) SetMood(Mood.Chase, 3.2);
        else if (n < 85) SetMood(Mood.Jump, 1.1);
        else if (n < 90) { if (rng.Next(2) == 0) GoToBed(true, rng.Next(18, 40)); else SetMood(Mood.Sleep, rng.Next(18, 40)); }
        else if (n < 94) GoToBed(false, rng.Next(12, 25));
        else SetMood(Mood.Idle, rng.Next(3, 7));
    }
    private void MoveTowards(float targetX, float targetY, double speed, double dt)
    {
        double dx = targetX - x, dy = targetY - y, distance = Math.Sqrt(dx * dx + dy * dy);
        if (Math.Abs(dx) > 2) right = dx > 0;
        if (distance > 2) { double step = Math.Min(distance, speed * dt); x += (float)(dx / distance * step); y += (float)(dy / distance * step); moving = step > 0; }
    }
    private void Tick(object sender, EventArgs e)
    {
        try
        {
            double wall = clock.Elapsed.TotalSeconds, dt = Math.Clamp(wall - last, 0, .05); last = wall; ticks++;
            if (smoke && wall > 3)
            {
                if (ticks < 30) throw new InvalidOperationException("The native UI pump did not render enough timer frames.");
                Console.WriteLine("PASS: macOS Avalonia overlay; " + ticks + " timer frames; sprites, alpha, commands, toys, bed and feeder validated."); Close(); return;
            }
            if (!smoke) pendingMeals += meals.Poll(DateTime.Now);
            PixelPoint cursor = NativeDesktop.Pointer; UpdateHitTest(cursor); bed.UpdateHitTest(cursor); feeder.UpdateHitTest(cursor); foreach (DesktopToy item in toys) item.UpdateHitTest(cursor);
            if (paused || dragging || bed.Dragging || feeder.Dragging || menu.IsOpen) { Render(); return; }
            timeline += dt; moving = false;
            area = mood == Mood.GoBed || inBed ? AreaAt(bed.Position) : IsFeeding(mood) && mood != Mood.LeaveFood ? AreaAt(feeder.Position) : mood == Mood.Play && toy != null ? ToyArea(toy) : AreaAt(new PixelPoint((int)x, (int)y));
            foreach (DesktopToy item in toys) item.Step(dt, ToyArea(item)); feeder.Step(timeline);
            if (pendingMeals > 0 && !FeedingActive()) { pendingMeals--; StartMeal(); }
            if (IsHomeAction(mood)) TickHome(dt);
            else if (mood == Mood.Play || IsToySequence(mood)) TickToyPlay(dt);
            else if (mood == Mood.Follow)
            {
                float mx = Math.Clamp(cursor.X, area.Left + 26 * scale, area.Right - 26 * scale), my = Math.Clamp(cursor.Y + 45, area.Top + 44 * scale + 48, area.Bottom - 4);
                if (Math.Abs(mx - x) > 65 || Math.Abs(my - y) > 60) MoveTowards(mx, my, 220, dt);
            }
            else if (mood == Mood.Walk || mood == Mood.Run)
            { MoveTowards(tx, ty, mood == Mood.Run ? 320 : 140, dt); if (Math.Abs(tx - x) < 3 && Math.Abs(ty - y) < 3) SetMood(Mood.Sniff, 2); }
            if (timeline > until)
            {
                if (mood == Mood.Pet && petResumePlay && toy != null) { petResumePlay = false; StartToyFetch(); }
                else if (mood == Mood.Sleep || mood == Mood.BedSleep) SetMood(Mood.Wake, 1.1);
                else if (mood == Mood.BedLie) SetMood(Mood.LeaveBed, .95);
                else if (mood == Mood.Dead) SetMood(Mood.RiseDead, 1.05);
                else if (!IsToySequence(mood)) Choose();
            }
            Clamp(); Render();
        }
        catch (Exception ex) { timer.Stop(); MacProgram.ReportError(ex); if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(1); }
    }
    private void Render()
    {
        double anim = Math.Max(0, timeline - stateStart); PixelPoint p = NativeDesktop.Pointer;
        bool happy = timeline < happyUntil || Math.Abs(p.X - x) < 110 && Math.Abs(p.Y - y + 50) < 100 && mood != Mood.Sleep;
        Mood renderMood = mood == Mood.HomeRise ? homeRiseMood : (mood == Mood.Follow || mood == Mood.GoFood) && !moving ? Mood.Idle : mood;
        ToyKind kind = toy != null && (toy.Held || mood == Mood.ToyToss) ? toy.Kind : ToyKind.None;
        currentDog = MacArt.DrawDog(renderMood, anim, right, happy, p.X > x ? 1 : -1, kind, until - stateStart); dogImage.Source = currentDog;
        bool quietToy = mood == Mood.ToyChew && previousMood == Mood.ToySettle || mood == Mood.ToyRise && previousMood == Mood.ToyChew;
        double dissolve = quietToy ? .18 : .12; bool fade = anim < dissolve && previousMood != mood && (quietToy || (mood == Mood.Idle || mood == Mood.Sniff) && (previousMood == Mood.Idle || previousMood == Mood.Sniff));
        beforeImage.IsVisible = fade; dogImage.Opacity = fade ? anim / dissolve : 1;
        if (fade) { beforeImage.Source = MacArt.DrawDog(previousMood, previousAnimation, right, false, 0, kind); beforeImage.Opacity = 1 - anim / dissolve; }
        bedFront.IsVisible = inBed; bedFront.Width = bed.Width; bedFront.Height = bed.Height;
        Canvas.SetLeft(bedFront, bed.Position.X - Position.X); Canvas.SetTop(bedFront, bed.Position.Y - Position.Y);
        bubbleText.Text = bubble; bubbleBox.IsVisible = timeline < bubbleUntil;
        // A clicked toy or appliance never covers the eating dog's muzzle or paws.
        if (inBed || IsFeeding(mood)) NativeDesktop.KeepAbove(this, bed, feeder);
    }
    internal override void UpdateHitTest(PixelPoint cursor)
    {
        if (dragging || menu.IsOpen) { NativeDesktop.IgnoreMouse(this, false); return; }
        double left = (Width - 52 * scale) / 2, top = 48;
        int bx = (int)((cursor.X - Position.X - left) * 4 / scale), by = (int)((cursor.Y - Position.Y - top) * 4 / scale);
        bool hit = MacArt.HitTest(currentDog, bx, by);
        if (bubbleBox.IsVisible && cursor.X >= Position.X + 4 && cursor.X < Position.X + Width - 4 && cursor.Y >= Position.Y + 3 && cursor.Y < Position.Y + 34) hit = true;
        if (inBed) hit |= MacArt.HitTest(frontArtwork, cursor.X - bed.Position.X, cursor.Y - bed.Position.Y);
        NativeDesktop.IgnoreMouse(this, !hit);
    }

    private void BuildMenus()
    {
        var items = new List<object> { new MenuItem { Header = "Кицу · шипперке ♀", IsEnabled = false } };
        void Add(string label, Action action) { var item = new MenuItem { Header = label, Command = new ActionCommand(action) }; items.Add(item); menuItems[label] = item; }
        Add("Погладить ♥", Pet); Add("Бросить мячик", () => ThrowToy(ToyKind.Ball)); Add("Дать игрушку-косточку", () => ThrowToy(ToyKind.Bone)); Add("Дать любимого резинового кабанчика", () => ThrowToy(ToyKind.Boar));
        Add("Убрать все игрушки", () => { RemoveToys(); Choose(); });
        var commands = CommandEntries(); items.Add(new MenuItem { Header = "Команды", ItemsSource = commands.Select(entry => new MenuItem { Header = entry.Label, Command = new ActionCommand(entry.Action) }).ToArray() });
        Add("Ловить хвост", () => Command(Mood.Chase, 3.2, "Сейчас поймаю!")); Add("Покормить Кицу", StartMeal);
        Add("Вернуть лежанку и кормушку на этот монитор", () => { ResetHome(); SaveHome(); });
        Add("Спать / проснуться", ToggleSleep); Add("Следовать за мышкой", ToggleFollow); items.Add(new Separator());
        Add("Звук лая", () => { sound = !sound; RefreshMenu(); }); Add("Пауза", () => { paused = !paused; RefreshMenu(); });
        Add("Поверх окон", () => { SetTop(!Topmost); RefreshMenu(); });
        items.Add(new MenuItem { Header = "Размер", ItemsSource = new[] { MakeSize("Маленькая", 3), MakeSize("Обычная", 4), MakeSize("Крупная", 5) } });
        Add("На другой монитор", NextMonitor); Add("Как играть", () => Speak("Клик — ласка • два клика — мяч", 5)); items.Add(new Separator()); Add("Закрыть Кицу", Close);
        menu.ItemsSource = items;
    }
    private MenuItem MakeSize(string label, int size) => new() { Header = label, Command = new ActionCommand(() => SetSize(size)) };
    private (string Label, Action Action)[] CommandEntries() => new (string, Action)[]
    {
        ("Поклон", () => Command(Mood.Bow, 5, "Поклон!")), ("Прыжок", () => Command(Mood.Jump, 1.1, "Оп!")),
        ("Голос", () => { Command(Mood.Bark, 1.5, "Гав-гав!"); Bark(); }), ("Кружись", () => Command(Mood.Spin, 1.6, "Кружусь!")),
        ("Дай лапу", () => { bool left = rng.Next(2) == 0; Command(left ? Mood.PawLeft : Mood.PawRight, 5, left ? "Левая лапка ♥" : "Правая лапка ♥"); }),
        ("Зайка", () => Command(Mood.Bunny, rng.Next(5, 16) + 1.5, "Зайка!")), ("Сидеть", () => Command(Mood.Sit, double.PositiveInfinity, "Сижу!")),
        ("Лежать", () => Command(Mood.Lie, double.PositiveInfinity, "Лежу!")), ("Умри", () => Command(Mood.Dead, 10, "Лапки вверх!")),
        ("Место", () => GoToBed(false, double.PositiveInfinity)), ("Иди спать", () => GoToBed(true, double.PositiveInfinity)), ("Гулять / отменить команду", ResumeWalking)
    };
    private NativeMenu BuildNativeMenu()
    {
        var result = new NativeMenu();
        void Add(string label, Action action) { var item = new NativeMenuItem(label) { Command = new ActionCommand(action) }; result.Items.Add(item); trayItems[label] = item; }
        Add("Погладить ♥", Pet); Add("Бросить мячик", () => ThrowToy(ToyKind.Ball)); Add("Дать косточку", () => ThrowToy(ToyKind.Bone)); Add("Дать кабанчика", () => ThrowToy(ToyKind.Boar)); Add("Убрать все игрушки", () => { RemoveToys(); Choose(); });
        var commands = new NativeMenu(); foreach (var entry in CommandEntries()) commands.Items.Add(new NativeMenuItem(entry.Label) { Command = new ActionCommand(entry.Action) });
        result.Items.Add(new NativeMenuItem("Команды") { Menu = commands }); Add("Ловить хвост", () => Command(Mood.Chase, 3.2, "Сейчас поймаю!")); Add("Покормить Кицу", StartMeal);
        Add("Спать / проснуться", ToggleSleep); Add("Следовать за мышкой", ToggleFollow); result.Items.Add(new NativeMenuItemSeparator());
        Add("Звук лая", () => { sound = !sound; RefreshMenu(); }); Add("Пауза", () => { paused = !paused; RefreshMenu(); }); Add("Поверх окон", () => { SetTop(!Topmost); RefreshMenu(); });
        var sizes = new NativeMenu(); foreach (int size in new[] { 3, 4, 5 }) { int selected = size; sizes.Items.Add(new NativeMenuItem(size == 3 ? "Маленькая" : size == 4 ? "Обычная" : "Крупная") { Command = new ActionCommand(() => SetSize(selected)) }); }
        result.Items.Add(new NativeMenuItem("Размер") { Menu = sizes }); Add("На другой монитор", NextMonitor); Add("Вернуть лежанку и кормушку", () => { ResetHome(); SaveHome(); }); result.Items.Add(new NativeMenuItemSeparator()); Add("Закрыть Кицу", Close); return result;
    }
    private void RefreshMenu()
    {
        foreach (var setting in new[] { ("Пауза", paused), ("Следовать за мышкой", follow), ("Звук лая", sound), ("Поверх окон", Topmost) })
        {
            if (menuItems.TryGetValue(setting.Item1, out var item)) item.Header = (setting.Item2 ? "✓ " : "") + setting.Item1;
            if (trayItems.TryGetValue(setting.Item1, out var native)) native.Header = (setting.Item2 ? "✓ " : "") + setting.Item1;
        }
    }
    private void ToggleSleep()
    {
        DropHeldToy(); StopToyPlay();
        if (mood == Mood.Sleep || mood == Mood.BedSleep) { Wake(); Speak("Уже встала!", 2); }
        else { follow = false; SetMood(Mood.Sleep, double.PositiveInfinity); Speak("Спокойной ночи…", 3); }
    }
    private void ToggleFollow() { Wake(); follow = !follow; if (follow) { StopToyPlay(); SetMood(Mood.Follow, 3600); Speak("Поиграем?", 2); } else Choose(); RefreshMenu(); }
    private void SetTop(bool top) { Topmost = top; bed.Topmost = top; feeder.Topmost = top; foreach (DesktopToy item in toys) item.Topmost = top; NativeDesktop.Front(this); }
    private void SetSize(int size)
    {
        scale = size; ResizePet(); bed.ResizeObject(size); feeder.ResizeObject(size); bed.ClampTo(AreaAt(bed.Position)); feeder.ClampTo(AreaAt(feeder.Position)); if (FeedingActive()) RefreshFeedingSide(); RebuildFront(); SaveHome(); Clamp();
    }
    private void NextMonitor()
    {
        StopToyPlay(); follow = false; var all = Screens.All.ToArray(); if (all.Length == 0) return;
        int index = Array.FindIndex(all, screen => screen.WorkingArea.Contains(new PixelPoint((int)x, (int)y))); area = new WorkArea(all[(index + 1) % all.Length].WorkingArea);
        x = area.Left + area.Width / 2; y = area.Bottom - 12; inBed = false; ResetHome(); SaveHome(); Choose(); Clamp();
    }
    private void Bark()
    {
        if (!sound || !OperatingSystem.IsMacOS()) return;
        // The bundled sample is synthesized locally once; afplay is part of macOS.
        try
        {
            string path = Path.Combine(MacProgram.DataDirectory, "bark.wav"); Directory.CreateDirectory(MacProgram.DataDirectory);
            if (!File.Exists(path)) WriteBark(path);
            var info = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add(path); Process.Start(info)?.Dispose();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
    private static void WriteBark(string path)
    {
        const int rate = 22050, samples = 15435; using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        var noise = new Random(3);
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)rate, local = t < .3 ? t : t - .38;
            double envelope = local >= 0 && local < .25 ? Math.Sin(Math.PI * local / .25) * Math.Exp(-local * 5) : 0;
            double frequency = 170 - local * 180, signal = Math.Sin(2 * Math.PI * frequency * local) + .45 * Math.Sin(2 * Math.PI * frequency * 2.7 * local) + .3 * (noise.NextDouble() * 2 - 1);
            writer.Write((short)Math.Clamp(signal * envelope * 14000, short.MinValue, short.MaxValue));
        }
    }
    private void RunSmokeChecks()
    {
        NativeDesktop.VerifyOverlay(this); NativeDesktop.VerifyOverlay(bed); NativeDesktop.VerifyOverlay(feeder);
        Console.WriteLine(MacArt.ValidateArtwork());
        int renders = 0;
        foreach (Mood state in Enum.GetValues<Mood>()) foreach (bool facing in new[] { true, false }) foreach (ToyKind kind in new[] { ToyKind.None, ToyKind.Ball, ToyKind.Bone, ToyKind.Boar })
        {
            Bitmap image = MacArt.DrawDog(state, .45, facing, false, 0, kind, 5); if (image.PixelSize.Width != 208 || image.PixelSize.Height != 176) throw new InvalidDataException("Wrong dog render size."); renders++;
        }
        var schedule = new MealSchedule(null); DateTime date = new DateTime(2026, 10, 9, 9, 59, 59);
        if (schedule.Poll(date) != 0 || schedule.Poll(date.AddSeconds(1)) != 1 || schedule.Poll(date.AddSeconds(20)) != 0 || schedule.Poll(date.Date.AddHours(22)) != 2 || schedule.Poll(date.Date.AddHours(10)) != 0) throw new InvalidDataException("Feeding schedule failed.");
        foreach (ToyKind kind in new[] { ToyKind.Ball, ToyKind.Bone, ToyKind.Boar }) ThrowToy(kind);
        if (toys.Count != 3) throw new InvalidDataException("Three toys must coexist.");
        var older = toys[0]; SelectToy(older); older.Hold(MouthX(), MouthY()); BeginToyAction(Mood.ToyChew);
        if (!older.Held || mood != Mood.ToySettle) throw new InvalidDataException("Toy settle must keep the toy in mouth.");
        SetMood(Mood.ToyChew, 8); SetMood(Mood.ToyRise, .95); if (!older.Held) throw new InvalidDataException("Rising toy lost.");
        DropHeldToy(); if (older.Held || !older.IsVisible) throw new InvalidDataException("Dropped toy must remain visible.");
        StopToyPlay();
        // Furniture at the top of a screen remains reachable. Empty bubble
        // padding must never clamp the dog's paws back out of her bed or bowl.
        bed.Position = new PixelPoint((int)area.Left + 25, (int)area.Top);
        Vec rest = bed.RestPoint; x = rest.X; y = rest.Y; SetMood(Mood.BedLie, 2); inBed = true; Clamp();
        if (Math.Abs(x - rest.X) > .01 || Math.Abs(y - rest.Y) > .01) throw new InvalidDataException("Bed at screen top is unreachable.");
        feeder.Position = new PixelPoint((int)area.Left + 132, (int)area.Top); foodRight = true;
        SetMood(Mood.FeedChew, 12); SetSize(5); RefreshFeedingSide(); Vec dining = feeder.FeedingPosition(foodRight);
        if (foodRight || dining.X < area.Left + 26 * scale || dining.X > area.Right - 26 * scale) throw new InvalidDataException("Feeding side did not adapt after resize.");
        x = dining.X; y = dining.Y; Clamp();
        if (Math.Abs(x - dining.X) > .01 || Math.Abs(y - dining.Y) > .01) throw new InvalidDataException("Food at screen top is unreachable.");
        SetSize(4); ResetHome(); x = area.Left + area.Width * .7f; y = area.Bottom - 12; SetMood(Mood.Idle, 4); feeder.Dispense(timeline);
        Console.WriteLine("Validated " + renders + " dog renders, schedule deduplication, three toys, mouth transitions and furniture edge positions.");
    }
    protected override void OnClosed(EventArgs e)
    {
        if (!closing) { closing = true; timer.Stop(); SaveHome(); RemoveToys(); bed.Close(); feeder.Close(); tray.IsVisible = false; tray.Dispose(); frontArtwork = null; }
        base.OnClosed(e);
    }
}
