using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class MainWindow : Window
{
    private sealed record Tool(string Key, string Caption, string Title, string Description, string Icon, Func<UIElement> Create);

    // 기본 레일 순서. 단축키는 도구가 아니라 레일에 보이는 자리를 따른다(위에서부터 F1, F2, …)
    private static readonly Tool[] Tools =
    [
        // 정렬 → 글 다루기 → 인코딩·디코딩 → 요청 → 모으기 순
        new("json", "JSON", "JSON 정렬", "입력하는 대로 보기 좋게 정렬하고, 문법이 틀린 곳을 알려 줍니다.", "IconBraces", () => new FormatView(FormatKind.Json)),
        new("xml", "XML", "XML 정렬", "입력하는 대로 들여 쓰고, 문법이 틀린 곳을 알려 줍니다.", "IconCodeXml", () => new FormatView(FormatKind.Xml)),
        new("compare", "비교", "텍스트 비교", "두 글을 나란히 놓고 달라진 줄을 표시합니다. 원본과 수정본은 함께 스크롤됩니다.", "IconGitCompare", () => new TextCompareView()),
        new("regex", "정규식", "정규식 테스트", "패턴과 문자열을 넣으면 일치하는 부분을 바로 찾아 줍니다.", "IconRegex", () => new RegexView()),
        new("base64", "Base64", "Base64 변환", "글자를 Base64로 바꾸거나 원래 글자로 되돌립니다. URL 안전 형식도 풀 수 있습니다.", "IconBinary", () => new Base64View()),
        new("hash", "URL·해시", "URL·해시·GUID", "입력하는 대로 URL 인코딩·디코딩과 해시 값을 보여 줍니다. 파일을 끌어다 놓으면 파일 해시를 구합니다.", "IconHash", () => new HashView()),
        new("jwt", "JWT", "JWT 디코더", "토큰을 붙여 넣으면 헤더와 페이로드를 바로 풀어 보여 줍니다. 서명은 검증하지 않습니다.", "IconKeyRound", () => new JwtView()),
        new("rest", "REST", "REST 요청", "HTTP 요청을 보내고 응답을 확인합니다. 자주 쓰는 요청은 저장해 둘 수 있습니다.", "IconSend", () => new RestView()),
        new("clipboard", "클립보드", "클립보드 기록", "다른 프로그램에서 복사한 글을 모아 보여 줍니다. 두 번 누르거나 Enter를 누르면 다시 복사합니다.", "IconClipboardList", () => new ClipboardView()),
        new("ocr", "글 추출", "텍스트 추출", "캡처한 화면이나 이미지에서 글을 뽑아 줍니다. Ctrl+V로 붙여 넣거나 이미지 파일을 끌어다 놓아 주십시오.", "IconScanText", () => new OcrView()),
    ];

    private readonly Dictionary<string, UIElement> _views = [];
    private readonly List<RadioButton> _railButtons = []; // 모든 도구 단추(기본 순서). 화면 배치는 RailMenuList·RailHiddenList
    private bool _exiting; // true 일 때만 창을 실제로 닫는다(그 밖의 닫기는 트레이로 숨김)

    public MainWindow()
    {
        InitializeComponent();

        foreach (Tool tool in Tools)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("RailButton"),
                Tag = tool,
                Content = new Path { Style = (Style)FindResource("LucideButtonIcon"), Data = (Geometry)FindResource(tool.Icon), Width = 18, Height = 18 },
            };
            AutomationProperties.SetName(button, tool.Caption);
            button.Checked += (_, _) => Show(tool);
            _railButtons.Add(button);
        }
        InitializeRailMenuEditing();
        ApplyRailMenuLayout();

        Topmost = AppSettings.Current.Topmost;
        UpdateTopmostButton();
        UpdateThemeButton();

        SourceInitialized += (_, _) =>
        {
            WindowDwmHelper.Initialize(this);
            // 제목 줄(비클라이언트 영역)을 누르면(WM_NC*BUTTONDOWN) WPF 마우스 이벤트가 오지 않으므로 여기서 레일 편집을 끝낸다.
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((IntPtr _, int message, IntPtr _, IntPtr _, ref bool _) =>
            {
                if (_railEditing && message is 0x00A1 or 0x00A4 or 0x00A7) ExitRailEditMode();
                return IntPtr.Zero;
            });
        };
        StateChanged += (_, _) =>
        {
            WindowDwmHelper.ApplyCornerPreference(this);
            WindowDwmHelper.ApplyMaximizeMargin(this, RootPanel);
            MaximizeIcon.Data = (Geometry)FindResource(WindowState == WindowState.Maximized ? "IconWinRestore" : "IconWinMaximize");
        };

        // Windows 로그오프·종료 때는 숨기지 않고 끝낸다
        Application.Current.SessionEnding += (_, _) => _exiting = true;

        // 마지막에 쓴 도구를 숨겼으면 레일 맨 위 도구로 연다
        (VisibleRailButtons.FirstOrDefault(b => ((Tool)b.Tag).Key == AppSettings.Current.LastTool) ?? VisibleRailButtons[0]).IsChecked = true;

        // 트레이에 오래 머무는 앱이라 시작 때와 1시간마다 새 버전을 확인한다
        CheckForUpdate();
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        updateTimer.Tick += (_, _) => CheckForUpdate();
        updateTimer.Start();
    }

    // ── 자동 업데이트(배포.md) ──
    private UpdateService.Release? _update;

    private async void CheckForUpdate()
    {
        if (_update is not null) return;
        _update = await UpdateService.CheckAsync();
        if (_update is null) return;
        UpdateButton.Content = "업데이트";
        UpdateButton.ToolTip = $"v{_update.Version} 새 버전\n{_update.Notes}";
        UpdateButton.Visibility = Visibility.Visible;
    }

    // 가운데 팝업에서 노트·진행률을 보이며 받고, 설치 파일을 실행했으면 앱을 끝내 파일을 바꾸게 한다(입력은 Drafts 가 담아 둔다)
    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not null && new UpdateDialog(_update) { Owner = this }.ShowDialog() == true) ExitApp();
    }

    /// <summary>앱을 완전히 끝낸다(Ctrl+X 단추, 트레이 메뉴의 종료).</summary>
    public void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void Show(Tool tool)
    {
        if (!_views.TryGetValue(tool.Key, out UIElement? view))
        {
            _views[tool.Key] = view = tool.Create();
            // 지난번에 입력해 둔 글·선택을 되살리고, 고칠 때마다 다시 담는다
            Drafts.Restore(tool.Key, view);
            Drafts.Watch(view, SaveDrafts);
        }
        PageTitle.Text = tool.Title;
        PageDescription.Text = tool.Description;
        PageHost.Content = view;
        PageActions.Content = (view as TextCompareView)?.HeaderBar;
        AppSettings.Current.LastTool = tool.Key;
    }

    // X·Alt+F4·작업 표시줄의 창 닫기는 트레이로 숨긴다
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SaveDrafts();
        AppSettings.Save();
        base.OnClosing(e);
    }

    private void SaveDrafts()
    {
        foreach ((string key, UIElement view) in _views) Drafts.Capture(key, view);
        Drafts.Save();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _railEditing)
        {
            ExitRailEditMode();
            e.Handled = true;
            return;
        }
        // F1~F12: 레일에 보이는 순서대로 고른다
        if (Keyboard.Modifiers != ModifierKeys.None || e.Key is < Key.F1 or > Key.F12) return;
        List<RadioButton> visible = VisibleRailButtons;
        int index = e.Key - Key.F1;
        if (index >= visible.Count) return;
        visible[index].IsChecked = true;
        e.Handled = true;
    }

    // ── 레일 편집(eghisWorks MainWindow 레일 편집을 옮겼다) ──
    private readonly DispatcherTimer _railHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private RadioButton? _railPressed; // 길게 누르기를 기다리거나 편집 중 끌고 있는 메뉴
    private Point _railPressStart;
    private bool _railEditing, _railDragging;
    private double _railGrabY, _railPitch;
    private Panel _railDropList = null!; // 놓을 칸과 그 칸에서의 자리(끄는 메뉴 제외)
    private int _railDropIndex;
    private Dictionary<RadioButton, double> _railRowTops = []; // 끌기 시작할 때 각 메뉴의 칸 안 위치

    private List<RadioButton> VisibleRailButtons => RailMenuList.Children.OfType<RadioButton>().ToList();

    // 끌 때 칸을 차지하는 메뉴. 끄는 메뉴는 자리 계산에서 뺀다.
    private IEnumerable<RadioButton> RailRows(Panel list) =>
        list.Children.OfType<RadioButton>().Where(button => button != _railPressed && button.Visibility == Visibility.Visible);

    // 메뉴를 길게 누르면 아이폰 홈 화면처럼 편집 모드가 된다. 끌어서 순서를 바꾸고, X로 숨긴다.
    private void InitializeRailMenuEditing()
    {
        _railHoldTimer.Tick += (_, _) =>
        {
            _railHoldTimer.Stop();
            RadioButton? held = _railPressed;
            _railPressed = null;
            if (held == null || Mouse.LeftButton != MouseButtonState.Pressed) return;
            EnterRailEditMode();
            BeginRailDrag(held); // 손을 떼지 않고 바로 끌 수 있다.
        };
        RailMenuList.PreviewMouseLeftButtonDown += RailEditList_PreviewMouseLeftButtonDown;
        RailHiddenList.PreviewMouseLeftButtonDown += RailEditList_PreviewMouseLeftButtonDown;
        RailMenuList.PreviewMouseMove += RailMenuList_PreviewMouseMove;
        RailMenuList.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _railHoldTimer.Stop();
            if (Mouse.Captured == RailMenuList) RailMenuList.ReleaseMouseCapture();
            else _railPressed = null;
        };
        RailMenuList.LostMouseCapture += (_, e) =>
        {
            if (!_railEditing || _railPressed == null || !ReferenceEquals(e.OriginalSource, RailMenuList)) return;
            RadioButton dropped = _railPressed;
            _railPressed = null;
            if (!_railDragging) return;
            _railDragging = false;
            RailDragGhost.Visibility = Visibility.Collapsed;
            RailDragGhostImage.Background = null;
            dropped.ClearValue(OpacityProperty);
            _railButtons.ForEach(button => button.ClearValue(RenderTransformProperty));
            ResizeRailHiddenPanel(() =>
            {
                dropped.ClearValue(VisibilityProperty);
                MoveRailMenu(dropped, _railDropList, _railDropIndex);
            });
            SaveRailMenuLayout();
        };
        // 편집 영역 밖을 누르면 편집을 끝내고 누른 동작은 그대로 둔다.
        PreviewMouseDown += (_, e) =>
        {
            if (_railEditing && e.OriginalSource is Visual source &&
                !source.IsDescendantOf(RailMenuList) && !source.IsDescendantOf(RailHiddenPanel)) ExitRailEditMode();
        };
    }

    private void RailEditList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RadioButton? button = null;
        bool onBadge = false;
        for (DependencyObject? node = e.OriginalSource as Visual; node != null && button == null; node = VisualTreeHelper.GetParent(node))
        {
            onBadge |= node is FrameworkElement { Name: "RailRemoveBadge" };
            if (node is RadioButton candidate && _railButtons.Contains(candidate)) button = candidate;
        }
        if (button == null) return;
        if (!_railEditing)
        {
            // 평소에는 클릭을 그대로 두고 길게 누르는지만 지켜본다.
            _railPressed = button;
            _railPressStart = e.GetPosition(RailMenuList);
            _railHoldTimer.Start();
            return;
        }
        e.Handled = true; // 편집 중에는 도구를 열지 않는다.
        if (onBadge)
        {
            ResizeRailHiddenPanel(() => MoveRailMenu(button, RailHiddenList, 0)); // 새로 숨긴 메뉴는 맨 위에 쌓는다.
            if (button.IsChecked == true) VisibleRailButtons[0].IsChecked = true; // 보던 도구를 숨기면 레일 맨 위 도구로 간다
            SaveRailMenuLayout();
            return;
        }
        BeginRailDrag(button);
    }

    private void BeginRailDrag(RadioButton button)
    {
        _railPressStart = Mouse.GetPosition(RailMenuList);
        _railGrabY = Mouse.GetPosition(button).Y;
        _railPressed = Mouse.Capture(RailMenuList) ? button : null;
    }

    private void RailMenuList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_railPressed == null) return;
        Vector moved = e.GetPosition(RailMenuList) - _railPressStart;
        bool beyond = Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                      Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!_railEditing)
        {
            // 누른 채 움직이면 길게 누르기를 취소한다.
            if (beyond) { _railHoldTimer.Stop(); _railPressed = null; }
            return;
        }
        if (!_railDragging)
        {
            if (!beyond) return;
            StartRailDrag();
        }
        // 레일 메뉴는 끌어서 숨기지 않고(X로만 숨긴다), 숨긴 메뉴는 레일로 한 번 꺼내면 숨긴 칸으로 돌아가지 않는다.
        Panel target = _railDropList == RailHiddenList && e.GetPosition(RailHiddenPanel).X >= 0 ? RailHiddenList : RailMenuList;
        var host = (UIElement)RailDragGhost.Parent;
        var ghostMove = (TranslateTransform)RailDragGhost.RenderTransform;
        ghostMove.Y = e.GetPosition(host).Y - _railGrabY;
        if (target != _railDropList)
        {
            ghostMove.BeginAnimation(TranslateTransform.XProperty, RailShift(target.TranslatePoint(new Point(), host).X));
            // 꺼낸 메뉴 자리만큼 숨긴 메뉴 칸을 줄이고, 남은 메뉴는 줄어든 칸의 제자리에 둔다.
            RadioButton pressed = _railPressed;
            ResizeRailHiddenPanel(() => pressed.Visibility = Visibility.Collapsed);
            int slot = 0;
            foreach (RadioButton button in RailRows(RailHiddenList))
            {
                ((TranslateTransform)button.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
                _railRowTops[button] = slot++ * _railPitch;
            }
        }

        // 끄는 메뉴(고스트) 가운데가 놓인 칸을 놓을 자리로 한다. 메뉴 높이는 모두 같다.
        double center = e.GetPosition(target).Y - _railGrabY + _railPitch / 2;
        int index = Math.Clamp((int)Math.Floor(center / _railPitch), 0, RailRows(target).Count());
        if (target == _railDropList && index == _railDropIndex) return;
        _railDropList = target;
        _railDropIndex = index;

        // 원래 자리는 비우고, 놓을 자리가 벌어지도록 나머지 메뉴를 밀어낸다.
        foreach (Panel list in new Panel[] { RailMenuList, RailHiddenList })
        {
            int slot = 0;
            foreach (RadioButton button in RailRows(list))
            {
                double top = slot * _railPitch + (list == target && slot >= index ? _railPitch : 0);
                ((TranslateTransform)button.RenderTransform).BeginAnimation(TranslateTransform.YProperty, RailShift(top - _railRowTops[button]));
                slot++;
            }
        }
    }

    // 숨긴 메뉴 칸 높이가 바뀌는 변경을 적용하고, 칸이 보이면 이전 높이에서 새 높이로 부드럽게 옮긴다.
    // 끝나면 애니메이션을 풀어(FillBehavior.Stop) 다시 내용에 맞는 높이를 따른다.
    private void ResizeRailHiddenPanel(Action change)
    {
        double from = RailHiddenPanel.ActualHeight;
        change();
        if (RailHiddenPanel.Visibility != Visibility.Visible) return;
        // 안쪽 목록만 무효화된 상태라 칸을 직접 Measure하면 이전 크기가 그대로 나온다. 배치를 한 번 끝내 새 높이를 얻는다.
        RailHiddenPanel.BeginAnimation(HeightProperty, null);
        RailHiddenPanel.UpdateLayout();
        double to = RailHiddenPanel.ActualHeight;
        if (Math.Abs(to - from) < 0.5) return;
        RailHiddenPanel.BeginAnimation(HeightProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    private static DoubleAnimation RailShift(double to) =>
        new(to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    // 누른 메뉴를 레일 폭 그대로 띄우고, 다른 메뉴가 비켜날 수 있게 위치를 기억한다.
    private void StartRailDrag()
    {
        RadioButton pressed = _railPressed!;
        _railDragging = true;
        _railPitch = pressed.ActualHeight;
        _railRowTops = _railButtons.ToDictionary(button => button,
            button => button.TranslatePoint(new Point(), RailMenuList.Children.Contains(button) ? RailMenuList : RailHiddenList).Y);
        _railButtons.ForEach(button => button.RenderTransform = new TranslateTransform());
        _railDropList = RailMenuList.Children.Contains(pressed) ? RailMenuList : RailHiddenList;
        _railDropIndex = _railDropList.Children.IndexOf(pressed);

        RailDragGhost.Width = pressed.ActualWidth;
        RailDragGhost.Height = pressed.ActualHeight;
        RailDragGhostImage.Background = CaptureSnapshot(pressed);
        var host = (UIElement)RailDragGhost.Parent;
        var ghostMove = (TranslateTransform)RailDragGhost.RenderTransform;
        ghostMove.BeginAnimation(TranslateTransform.XProperty, null);
        ghostMove.X = _railDropList.TranslatePoint(new Point(), host).X;
        RailDragGhost.Visibility = Visibility.Visible;
        pressed.Opacity = 0;
    }

    // 끄는 동안 띄울 메뉴 그림(화면 배율 그대로)
    private static ImageBrush CaptureSnapshot(FrameworkElement element)
    {
        DpiScale scale = VisualTreeHelper.GetDpi(element);
        double width = Math.Max(1, element.ActualWidth), height = Math.Max(1, element.ActualHeight);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale.DpiScaleX), (int)Math.Ceiling(height * scale.DpiScaleY),
            96 * scale.DpiScaleX, 96 * scale.DpiScaleY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
            context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        bitmap.Render(visual);
        bitmap.Freeze();
        return new ImageBrush(bitmap);
    }

    private void MoveRailMenu(RadioButton button, Panel target, int index)
    {
        Panel source = RailMenuList.Children.Contains(button) ? RailMenuList : RailHiddenList;
        if (source == target && source.Children.IndexOf(button) == index) return;
        source.Children.Remove(button);
        target.Children.Insert(Math.Min(index, target.Children.Count), button);
        UpdateRailMenus();
    }

    // X 표시(편집 중 레일 메뉴만)와 자리 단축키(F1~)를 지금 배치에 맞춘다.
    private void UpdateRailMenus()
    {
        List<RadioButton> visible = VisibleRailButtons;
        foreach (RadioButton button in _railButtons)
        {
            var tool = (Tool)button.Tag;
            int index = visible.IndexOf(button);
            button.ToolTip = index is >= 0 and < 12 ? $"{tool.Title}  ·  F{index + 1}" : tool.Title;
            button.ApplyTemplate();
            // 레일이 비면 편집 모드로 들어갈 수 없으므로 마지막 하나는 숨기지 못한다.
            if (button.Template?.FindName("RailRemoveBadge", button) is UIElement badge)
                badge.Visibility = _railEditing && index >= 0 && visible.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void EnterRailEditMode()
    {
        if (_railEditing) return;
        _railEditing = true;
        RailHiddenPanel.Visibility = Visibility.Visible;
        RailHiddenPanel.IsHitTestVisible = true;
        RailHiddenPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        ((TranslateTransform)RailHiddenPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        UpdateRailMenus();
    }

    private void ExitRailEditMode()
    {
        if (!_railEditing) return;
        if (Mouse.Captured == RailMenuList) RailMenuList.ReleaseMouseCapture();
        _railEditing = false;
        RailHiddenPanel.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            if (_railEditing) return;
            RailHiddenPanel.Visibility = Visibility.Collapsed;
            RailHiddenPanel.IsHitTestVisible = true;
        };
        RailHiddenPanel.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)RailHiddenPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-16, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        UpdateRailMenus();
    }

    private void RailEditDoneButton_Click(object sender, RoutedEventArgs e) => ExitRailEditMode();

    // 레일 경계선에서 부드럽게 휘어 나왔다가 다시 레일로 돌아가는 모양. 왼쪽 변은 레일과 이어지도록 선을 긋지 않고,
    // 콘텐츠 면의 왼쪽 테두리까지 덮어 칸 안에는 구분선이 보이지 않게 한다.
    private void RailHiddenPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double curve = 22, left = -1;
        double right = e.NewSize.Width - 0.5, bottom = e.NewSize.Height;
        RailHiddenShape.Data = Geometry.Parse(FormattableString.Invariant(
            $"M{left},0 C{left},{curve * 0.6} {right},{curve * 0.4} {right},{curve} L{right},{bottom - curve} C{right},{bottom - curve * 0.4} {left},{bottom - curve * 0.6} {left},{bottom}"));
    }

    private void SaveRailMenuLayout()
    {
        AppSettings.Current.RailOrder = [.. VisibleRailButtons.Concat(RailHiddenList.Children.OfType<RadioButton>()).Select(b => ((Tool)b.Tag).Key)];
        AppSettings.Current.HiddenTools = [.. RailHiddenList.Children.OfType<RadioButton>().Select(b => ((Tool)b.Tag).Key)];
        AppSettings.Save();
    }

    // 저장된 순서와 숨김으로 메뉴를 레일과 숨긴 메뉴 칸에 놓는다. 저장에 없는 도구는 기본 순서로 레일 끝에 붙는다.
    private void ApplyRailMenuLayout()
    {
        List<string> order = AppSettings.Current.RailOrder;
        foreach (RadioButton button in _railButtons.OrderBy(b => order.IndexOf(((Tool)b.Tag).Key) is int i and >= 0 ? i : int.MaxValue))
            (AppSettings.Current.HiddenTools.Contains(((Tool)button.Tag).Key) ? RailHiddenList : RailMenuList).Children.Add(button);
        // 설정 파일이 모두 숨김으로 되어 있으면 레일이 비어 편집 모드로 들어갈 수 없으므로 하나를 꺼낸다.
        if (RailMenuList.Children.Count == 0) MoveRailMenu((RadioButton)RailHiddenList.Children[0], RailMenuList, 0);
        UpdateRailMenus();
    }

    // ── 알림 ──
    public void ShowToast(string message)
    {
        ToastHost.Content = message;
        // 긴 안내(오류)는 읽을 시간을 더 준다
        double hold = Math.Min(4000, 1300 + message.Length * 35);
        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140 + hold))));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400 + hold))));
        ToastHost.BeginAnimation(OpacityProperty, opacity);
        ToastShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ── 제목 줄 단추(eghisWorks 머리 부분 빠른 설정과 같다) ──
    // 왼쪽 클릭: 라이트·다크 중 반대쪽 테마로 바꾼다.
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeWipe.IsWiping) return;
        bool dark = ThemeService.IsDark(ThemeService.Current);
        SetThemeDefault(ThemeService.DefaultFor(!dark, dark ? AppSettings.Current.LightTheme : AppSettings.Current.DarkTheme), true);
    }

    // 오른쪽 클릭: 라이트·다크 각각에서 쓸 테마를 고른다. 지금 밝기 쪽을 고르면 바로 바뀐다.
    private void ThemeButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ThemeWipe.IsWiping) { e.Handled = true; return; }
        string current = ThemeService.Current;
        bool currentDark = ThemeService.IsDark(current);
        ThemeMenu.Items.Clear();
        foreach (bool dark in new[] { false, true })
        {
            if (dark) ThemeMenu.Items.Add(new Separator());
            ThemeMenu.Items.Add(new MenuItem { Header = dark ? "다크 테마" : "라이트 테마", FontWeight = FontWeights.SemiBold, IsEnabled = false, Opacity = 1 });
            string selected = dark == currentDark ? current : ThemeService.DefaultFor(dark, dark ? AppSettings.Current.DarkTheme : AppSettings.Current.LightTheme);
            foreach (ThemeOption option in ThemeService.Options.Where(o => o.Palette.IsDark == dark))
            {
                var item = new MenuItem { Header = option.Label, IsCheckable = true, IsChecked = option.Key == selected };
                item.Click += (_, _) => SetThemeDefault(option.Key, dark == currentDark);
                ThemeMenu.Items.Add(item);
            }
        }
    }

    // 지금 테마와 고른 테마를 각 밝기의 기본값으로 기억한다. apply 면 고른 테마로 바꾼다.
    private void SetThemeDefault(string theme, bool apply)
    {
        string current = ThemeService.Current;
        AppSettings settings = AppSettings.Current;
        if (ThemeService.IsDark(current)) settings.DarkTheme = current; else settings.LightTheme = current;
        if (ThemeService.IsDark(theme)) settings.DarkTheme = theme; else settings.LightTheme = theme;
        if (apply && theme != current)
        {
            // 다크로 갈 때는 오른쪽 위부터, 라이트로 돌아갈 때는 왼쪽 아래부터 바뀐다.
            ThemeWipe.Run(RootPanel, () =>
            {
                ThemeService.Apply(theme);
                UpdateThemeButton();
            }, ThemeService.IsDark(current) && !ThemeService.IsDark(theme));
            settings.Theme = theme;
        }
        AppSettings.Save();
    }

    private void UpdateThemeButton()
    {
        bool dark = ThemeService.IsDark(ThemeService.Current);
        ThemeIcon.Data = (Geometry)FindResource(dark ? "IconMoon" : "IconSun");
        ThemeButton.ToolTip = dark ? "라이트 테마로 전환" : "다크 테마로 전환";
    }

    private void TopmostButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        AppSettings.Current.Topmost = Topmost;
        AppSettings.Save();
        UpdateTopmostButton();
    }

    // 켜지면 아이콘만 강조색(배경은 그대로)
    private void UpdateTopmostButton()
    {
        TopmostButton.ToolTip = Topmost ? "항상 위 해제" : "항상 위";
        if (Topmost) TopmostButton.SetResourceReference(ForegroundProperty, "PrimaryBrush");
        else TopmostButton.ClearValue(ForegroundProperty);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // X 는 트레이로 숨기고, Ctrl 을 누른 채 누르면 완전히 끝낸다
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ExitApp();
        else Hide();
    }
}
