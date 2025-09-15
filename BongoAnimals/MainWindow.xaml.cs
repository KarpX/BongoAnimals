using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using WpfAnimatedGif;
using System.Windows.Interop;
using System.Dynamic;
using System.Data.SqlTypes;
using WF = System.Windows.Forms;
using System.Windows.Threading;

namespace BongoAnimals
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static IntPtr _hookID = IntPtr.Zero;
        private static IntPtr _keyboardHookID = IntPtr.Zero;
        private int _counter = 0;
        private int _selectedMonitor = 0;
        private bool _bindingToTaskbar = true;
        
        private bool _blockMove = false;
        private bool _canMove = true;
        private DispatcherTimer _longPressTimer;
        private DateTime _pressStart;
        private readonly TimeSpan _longPressThreshold = TimeSpan.FromSeconds(3);

        private static HashSet<int> _pressedKeys = new HashSet<int>();

        private bool _isDragging = false;
        private Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            _hookID = SetHook(WH_MOUSE_LL, _proc);
            _keyboardHookID = SetHook(WH_keyboard_LL, _keyboardProc);
            Closed += (sender, e) => {
                UnhookWindowsHookEx(_hookID);
                UnhookWindowsHookEx(_keyboardHookID);
            };

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("Assets/pet_gif.gif", UriKind.Relative);
            image.EndInit();

            ImageBehavior.SetAnimatedSource(PetImage, image);

            Loaded += MainWindow_Loaded;

            MouseMove += Window_MouseMove;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;
            this.Deactivated += Settings_deactivated;

            _longPressTimer = new DispatcherTimer();
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(50);
            _longPressTimer.Tick += LongPressTimer_Tick;

            Loaded += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Point savedLocation = Properties.Settings.Default.PetLocation;
                    Canvas.SetLeft(PetContainer, savedLocation.X);
                    Canvas.SetTop(PetContainer, savedLocation.Y);
                }, System.Windows.Threading.DispatcherPriority.Render);
            };

        }

        private void LongPressTimer_Tick(object sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _pressStart;
            double progress = elapsed.TotalMilliseconds / _longPressThreshold.TotalMilliseconds;
            UpdateHoldingProgress(progress);

            if (progress >= 1.0)
            {
                _longPressTimer.Stop();
                _canMove = true;
                HoldIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _longPressTimer.Stop();
            HoldIndicator.Visibility = Visibility.Collapsed;
            if (_blockMove)
            {
                _canMove = false;
            }
            ReleaseMouseCapture();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _canMove)
            {
                Point mousePos = e.GetPosition(this);
                var (scaleX, scaleY) = GetScaleFactor();
                var screen = GetCurrentScreen();

                double offsetX = mousePos.X - _dragStartPoint.X;
                double offsetY = mousePos.Y - _dragStartPoint.Y;

                double screenWidth = screen.Bounds.Width;
                double screenHeight = screen.Bounds.Height;

                double containerWidth = PetImage.ActualWidth * scaleX;
                double containerHeight = PetImage.ActualHeight * scaleY;

                double newLeft = (Canvas.GetLeft(PetContainer) + offsetX) * scaleX;
                double newTop = (Canvas.GetTop(PetContainer) + offsetY) * scaleY;

                newLeft = Math.Max(0, Math.Min(newLeft, screenWidth - containerWidth));
                newTop = Math.Max(0, Math.Min(newTop, screenHeight - containerHeight));

                Canvas.SetLeft(PetContainer, newLeft / scaleX);
                Canvas.SetTop(PetContainer, newTop / scaleY);

                UpdateSettingsPosition();

                _dragStartPoint = mousePos;
            }
        }

        private WF.Screen GetCurrentScreen()
        {
            var windowCenter = new System.Drawing.Point(
                (int)(this.Left + this.Width / 2),
                (int)(this.Top + this.Height / 2));

            var screen = WF.Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(windowCenter));

            return screen ?? WF.Screen.PrimaryScreen;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private double GetTaskBarHeight(WF.Screen screen)
        {
            var workingArea = screen.WorkingArea;

            var bounds = screen.Bounds;

            return bounds.Height - workingArea.Height;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            style &= ~(WS_THICKFRAME | WS_CAPTION);
            SetWindowLong(hwnd, GWL_EXSTYLE, style);

            LoadProgress();

            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        }

        private void LoadMonitors()
        {
            MonitorsList.SelectionChanged -= MonitorsList_SelectionChanged;

            MonitorsList.Items.Clear();
            int i = 1;
            foreach (var screen in WF.Screen.AllScreens)
            {
                MonitorsList.Items.Add($"Screen {i} ({screen.Bounds.Width}x{screen.Bounds.Height})");
                i += 1;
            }
            Console.WriteLine($"Selected screen: {_selectedMonitor}");

            MonitorsList.SelectedIndex = _selectedMonitor;

            MonitorsList.SelectionChanged += MonitorsList_SelectionChanged;

            if (WF.Screen.AllScreens.Length > 1)
            {
                ChangeMonCard.Visibility = Visibility.Visible;
            }
            else
            {
                ChangeMonCard.Visibility = Visibility.Collapsed;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;

            if (msg == WM_NCHITTEST)
            {
                handled = true;
                return (IntPtr)HTCLIENT;
            }

            return IntPtr.Zero;
        }

        private const int GWL_EXSTYLE = -16;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelMouseProc _proc = HookCallback;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _keyboardProc = KeyboardHookCallback;

        private static IntPtr SetHook(int hookId, Delegate proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr fp = Marshal.GetFunctionPointerForDelegate(proc);
                return SetWindowsHookEx(hookId, fp, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_WHEELBUTTONDOWN = 0x0207;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WH_keyboard_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static MainWindow _mainWindow;

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_WHEELBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN)
            {
                if (_mainWindow == null)
                {
                    _mainWindow = (MainWindow)Application.Current.MainWindow;
                }
                _mainWindow._counter++;
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow.CounterText.Text = _mainWindow._counter.ToString();
                });
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void PetImage_MouseEnter(object sender, MouseEventArgs e)
        {
            CounterText.Text = _counter.ToString();
            CounterPanel.Visibility = Visibility.Visible;
            SettingBtn.Visibility = Visibility.Visible;
        }

        private void PetImage_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDragging) return;

            if (!PetContainer.IsMouseOver)
            {
                CounterPanel.Visibility = Visibility.Hidden;
                SettingBtn.Visibility = Visibility.Hidden;
            }
        }

        private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    if (!_pressedKeys.Contains(vkCode))
                    {
                        _pressedKeys.Add(vkCode);

                        if (_mainWindow == null)
                        {
                            _mainWindow = (MainWindow)Application.Current.MainWindow;
                        }
                        _mainWindow._counter++;
                        _mainWindow.Dispatcher.Invoke(() =>
                        {
                            _mainWindow.CounterText.Text = _mainWindow._counter.ToString();
                        });
                    }

                }
                else if (wParam == (IntPtr)0x0101) // WM_KEYUP
                {
                    _pressedKeys.Remove(vkCode);
                }
            }

            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveProgress();
        }

        private void SaveProgress()
        {
            Properties.Settings.Default.Counter = _counter.ToString();
            Properties.Settings.Default.PetLocation = new Point(Canvas.GetLeft(PetContainer), Canvas.GetTop(PetContainer));
            Properties.Settings.Default.SelectedMonitor = _selectedMonitor;
            Properties.Settings.Default.Snapping = _bindingToTaskbar;
            Properties.Settings.Default.BlockMove = _blockMove;

            Properties.Settings.Default.Save();
        }

        private void LoadProgress()
        {
            _counter = Convert.ToInt32(Properties.Settings.Default.Counter);
            CounterText.Text = _counter.ToString();
            _selectedMonitor = Properties.Settings.Default.SelectedMonitor;
            _bindingToTaskbar = Properties.Settings.Default.Snapping;
            _blockMove = Properties.Settings.Default.BlockMove;

            TaskbarSnappingText.Text = _bindingToTaskbar ? "[X] Snapping" : "[ ] Snapping";
            MoveBlockText.Text = _blockMove ? "[X] Block moving" : "[ ] Block moving";
            MoveToScreen(_selectedMonitor);
        }



        #region Win32

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        #endregion

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private enum ABMsg : int
        {
            ABM_GETTASKBARPOS = 5
        }

        [DllImport("shell32.dll")]
        private static extern uint SHAppBarMessage(ABMsg dwMessage, ref APPBARDATA pData);

        public void SettingBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateSettingsPosition();
                }), System.Windows.Threading.DispatcherPriority.Loaded);

                LoadSettingsPage();

                SettingsPanel.Visibility = Visibility.Visible;
            }
        }

        private void LoadSettingsPage()
        {
            LoadMonitors();
        }

        private void UpdateSettingsPosition()
        {
            var screen = GetCurrentScreen();
            var (scaleX, scaleY) = GetScaleFactor();

            double petLeft = Canvas.GetLeft(PetContainer) * scaleX;
            double petTop = Canvas.GetTop(PetContainer) * scaleY;
            double petWidth = PetContainer.ActualWidth * scaleX;
            double petHeight = PetContainer.ActualHeight * scaleY;

            double panelWidth = SettingsPanel.ActualWidth * scaleX;
            double panelHeight = SettingsPanel.ActualHeight * scaleY;

            double screenWidth = screen.Bounds.Width;

            double margin = 10;

            double panelLeft = petLeft + petWidth / 2 - panelWidth / 2;
            double panelTop = petTop - panelHeight - margin;

            if (panelLeft + panelWidth > screenWidth)
            {
                panelLeft = petLeft - margin - panelWidth;
                panelTop = petTop - panelHeight + petHeight - 35;
            }
            else if (panelLeft < 0)
            {
                panelLeft = petLeft + petWidth + margin;
                panelTop = petTop - panelHeight + petHeight - 35;
            }
            if (panelTop < 0)
            {
                panelTop = petTop + petHeight + margin;
            }

            Canvas.SetLeft(SettingsPanel, panelLeft / scaleX);
            Canvas.SetTop(SettingsPanel, panelTop / scaleY);
        }

        private void Settings_deactivated(object sender, EventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void PetContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);

            if (_blockMove)
            {
                _pressStart = DateTime.Now;
                _canMove = false;
                _longPressTimer.Start();
                HoldIndicator.Visibility = Visibility.Visible;
                UpdateHoldingProgress(0);
            }
            else
            {
                _canMove = true;
            }


                PetContainer.CaptureMouse();
        }

        private void UpdateHoldingProgress(double progress)
        {
            double angle = 360 * progress;

            double radius = 56;
            double centerX = radius;
            double centerY = radius;

            double radians = (Math.PI / 180) * angle;

            double x = centerX + radius * Math.Sin(radians);
            double y = centerY - radius * Math.Cos(radians);

            bool isLargeArc = angle > 180;

            var figure = new PathFigure();
            figure.StartPoint = new Point(centerX, centerY);
            figure.Segments.Add(new LineSegment(new Point(centerX, centerY - radius), true));
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(x, y),
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise,
            });
            figure.Segments.Add(new LineSegment(new Point(centerX, centerY), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            HoldProgress.Data = geometry;
        }

        private void PetContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_bindingToTaskbar)
                PetLinkToTaskbar();

            _isDragging = false;
            PetContainer.ReleaseMouseCapture();
        }

        private void PetLinkToTaskbar()
        {
            var screen = GetCurrentScreen();
            var (scaleX, scaleY) = GetScaleFactor();

            double screenHeight = screen.Bounds.Height;

            double petLeft = Canvas.GetLeft(PetContainer) * scaleX;
            double petTop = Canvas.GetTop(PetContainer) * scaleY;

            if (petTop + PetImage.Height * scaleY > screenHeight - GetTaskBarHeight(screen) - 20)
            {
                double newTop = screenHeight - GetTaskBarHeight(screen) - PetImage.Height * scaleY;
                Canvas.SetTop(PetContainer, newTop / scaleY);
                UpdateSettingsPosition();
            }
        }

        private void MoveToScreen(int screenIndex)
        {

            var screens = WF.Screen.AllScreens;

            if (screenIndex < 0)
            {
                return;
            }

            if (screenIndex >= screens.Length)
            {
                screenIndex = 0;
                _selectedMonitor = 0;
            }

            var screen = screens[screenIndex];

            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;

            this.Left = screen.Bounds.X;
            this.Top = screen.Bounds.Y;

            if (this.WindowState == WindowState.Normal)
                this.WindowState = WindowState.Maximized;

            ChangePetLocation();
            UpdateSettingsPosition();
            Console.WriteLine($"{Canvas.GetTop(PetContainer)}, {Canvas.GetLeft(PetContainer)}");
        }

        private void MonitorsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedMonitor = MonitorsList.SelectedIndex;
            MoveToScreen(MonitorsList.SelectedIndex);
        }

        private void ChangePetLocation()
        {
            var screen = GetCurrentScreen();
            var (scaleX, scaleY) = GetScaleFactor();

            double petLeft = Canvas.GetLeft(PetContainer) * scaleX;
            double petTop = Canvas.GetTop(PetContainer) * scaleY;

            double petWidth = PetContainer.ActualWidth * scaleX;

            double taskBarHeight = GetTaskBarHeight(screen);
            double taskbarTop = screen.Bounds.Height - taskBarHeight;

            if (petTop > taskbarTop)
            {
                petTop = taskbarTop - PetImage.ActualHeight;
            }

            if (petLeft + petWidth > screen.Bounds.Width)
            {
                petLeft = screen.Bounds.Width - petWidth;
            }

            Canvas.SetTop(PetContainer, petTop / scaleY);
            Canvas.SetLeft(PetContainer, petLeft / scaleX);
        }

        private (double ScaleX, double ScaleY) GetScaleFactor()
        {
            var source = PresentationSource.FromVisual(this);
            if (source.CompositionTarget != null)
            {
                var matrix = source.CompositionTarget.TransformToDevice;
                return (matrix.M11, matrix.M22);
            }
            return (1.0, 1.0);
        }

        private void TaskBarBindingBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _bindingToTaskbar = !_bindingToTaskbar;
            TaskbarSnappingText.Text = _bindingToTaskbar ? "[X] Snapping" : "[ ] Snapping";
            PetLinkToTaskbar();
        }

        private void MoveBlockBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _blockMove = !_blockMove;
            MoveBlockText.Text = _blockMove ? "[X] Block moving" : "[ ] Block moving";

        }

        private void OpenDangerousOptions(object sender, EventArgs e)
        {
            if (DangerousOptionsList.Visibility == Visibility.Visible)
            {
                DangerousOptionsList.Visibility = Visibility.Collapsed;
                return;
            }
            DangerousOptionsList.Visibility = Visibility.Visible;
        }

        private void ClearCounter_MouseLeftButtonDown(object sender, EventArgs e)
        {
            _counter = 0;
        }
    }
}