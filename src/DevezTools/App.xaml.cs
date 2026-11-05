using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using DevezTools.Infrastructure;
using DevezTools.Services;
using DevezTools.Views;
using Forms = System.Windows.Forms;

namespace DevezTools;

public partial class App : Application
{
    private const int AsfwAny = -1;
    private Forms.NotifyIcon? _trayIcon;
    private static Mutex? _instance;
    private static EventWaitHandle? _activate;

    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 트레이에 숨어 있을 때 다시 실행하면 새 창을 띄우지 않고 있던 창을 앞으로 꺼낸다
        _instance = new Mutex(true, @"Local\devezTools.Instance", out bool firstInstance);
        // 자동 업데이트로 다시 시작했으면 앞 버전이 끝나 잠금을 놓을 때까지 기다린다
        bool updated = e.Args.Contains("--updated");
        if (!firstInstance && updated)
        {
            try { firstInstance = _instance.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { firstInstance = true; }
        }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\devezTools.Activate");
        if (!firstInstance)
        {
            AllowSetForegroundWindow(AsfwAny); // 먼저 뜬 앱이 창을 앞으로 가져올 수 있게 허락
            _activate.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(RestoreMainWindow), null, Timeout.Infinite, false);
        // REST 응답의 EUC-KR 같은 코드 페이지 문자 집합을 읽을 수 있게 한다.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        AppSettings.Load();
        ThemeService.Apply(AppSettings.Current.Theme);
        EditorZoom.Apply(AppSettings.Current.EditorFontSize);

        DispatcherUnhandledException += (_, args) =>
        {
            // 도구 하나의 예외로 앱 전체가 꺼지지 않게 알리고 계속한다.
            Ui.Toast("문제가 생겨 작업을 끝내지 못했습니다: " + args.Exception.Message);
            args.Handled = true;
        };

        UpdateService.CleanUp();
        MainWindow = new MainWindow();
        MainWindow.Show();
        if (updated) ((MainWindow)MainWindow).ShowToast($"v{UpdateService.Current} 버전으로 업데이트했습니다.");
        ClipboardHistory.Start(MainWindow); // 클립보드 기록은 창을 숨겨도 계속 모은다

        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/devezTools;component/Assets/app.ico")).Stream;
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "DevezTools",
            Icon = new System.Drawing.Icon(iconStream, 32, 32),
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreMainWindow();
        _trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Right) ShowTrayMenu();
        };
    }

    // 트레이 메뉴도 앱 테마·Pretendard 로 그린다(eghisWorks 방식). 커서 자리에 1×1 투명 창을 띄워 활성화해 두어야
    // 메뉴 밖을 눌렀을 때 메뉴가 닫힌다.
    private void ShowTrayMenu()
    {
        var host = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
        };
        System.Drawing.Point cursor = Forms.Cursor.Position; // 화면 픽셀이라 배율 계산 없이 그 자리로 옮긴다
        host.SourceInitialized += (_, _) =>
            SetWindowPos(new WindowInteropHelper(host).Handle, IntPtr.Zero, cursor.X, cursor.Y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        host.Show();
        host.Activate();

        var menu = new ContextMenu { PlacementTarget = host, Placement = PlacementMode.Top };
        menu.Items.Add(TrayItem("열기", RestoreMainWindow));
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem("종료", () => ((MainWindow)MainWindow).ExitApp()));
        menu.Closed += (_, _) => host.Close();
        menu.IsOpen = true;
    }

    private static MenuItem TrayItem(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private void RestoreMainWindow()
    {
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}
