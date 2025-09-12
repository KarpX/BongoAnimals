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

            MouseLeftButtonDown += Window_MouseLeftButtonDown;
            MouseMove += Window_MouseMove;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point mousePos = PointToScreen(e.GetPosition(MainCanvas));
                double newLeft = (mousePos.X - _dragStartPoint.X);
                double newTop = (mousePos.Y - _dragStartPoint.Y);

                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                double taskbarHeight = GetTaskBarHeight();

                double minX = 0;
                double minY = 0;
                double maxX = screenWidth - Width;
                double maxY = screenHeight+40;

                if (newTop + Height > screenHeight - taskbarHeight + 40)
                {
                    newTop = screenHeight - taskbarHeight - Height + 40;
                }

                Left = Math.Max(minX, Math.Min(maxX, newLeft));
                Top = Math.Max(minY, Math.Min(maxY, newTop));


            }
        }

        private double GetTaskBarHeight()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            SHAppBarMessage(ABMsg.ABM_GETTASKBARPOS, ref abd);

            return abd.rc.bottom - abd.rc.top;
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
        private const int WH_keyboard_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static MainWindow _mainWindow;

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
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
        }

        private void PetImage_MouseLeave(object sender, MouseEventArgs e)
        {
            if(!PetContainer.IsMouseOver && !_isDragging)
                CounterPanel.Visibility = Visibility.Collapsed;
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

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(PetContainer);
            CaptureMouse();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveProgress();
        }

        private void SaveProgress()
        {
            Properties.Settings.Default.Counter = _counter.ToString();
            Properties.Settings.Default.Save();
        }

        private void LoadProgress()
        {
            _counter = Convert.ToInt32(Properties.Settings.Default.Counter);
            CounterText.Text = _counter.ToString();
        }

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
    }
}
