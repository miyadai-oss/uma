using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UmamusumeMultiwindowAdjuster
{
    internal static class Program
    {
        const int GWL_STYLE = -16;

        const int WS_BORDER = 0x00800000;
        const int WS_CAPTION = 0x00C00000;
        const int WS_SIZEBOX = 0x00040000;

        const int EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

        const int WINEVENT_OUTOFCONTEXT = 0;
        const int WINEVENT_SKIPOWNPROCESS = 2;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, int uFlags);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hWnd, int idObject, int idChild, uint dwEventThread,
            uint dmsEventTime);

        private static int umaProcessId;
        private static IntPtr umaWindowHandle;

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            umaWindowHandle = GetUmamusumeWindowHandle();
            Debug.WriteLine($"�E�}���̃E�B���h�E��\"{umaWindowHandle}\"�ł�");

            Adjust(umaWindowHandle);

            IntPtr hook = IntPtr.Zero;
            umaProcessId = GetUmamusumeProcessId();
            if (umaProcessId > 0)
            {
                WinEventDelegate callback = new WinEventDelegate(WinEventProc);
                hook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero, callback, (uint)umaProcessId, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

                NotifyIcon icon = new NotifyIcon();
                icon.Icon = Properties.Resources.Icon;
                icon.Text = "UMA";
                icon.Visible = true;

                ContextMenuStrip contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("�I��", null, new EventHandler(contextMenu_Exit));

                icon.ContextMenuStrip = contextMenu;

                Application.Run();

                UnhookWinEvent(hook);
            }
        }

        /// <summary>
        /// �E�}���̃v���Z�XID���擾����
        /// </summary>
        /// <returns>���݂��Ȃ��ꍇ��0��Ԃ��B�����Y������ꍇ�͍ŏ��̈��Ԃ�</returns>
        private static int GetUmamusumeProcessId()
        {
            Process[] processes = Process.GetProcessesByName("umamusume");
            return processes.Length == 0 ? 0 : processes[0].Id;
        }

        /// <summary>
        /// �E�}���̃E�B���h�E�n���h�����擾����
        /// </summary>
        /// <returns>���݂��Ȃ��ꍇ��<see cref="IntPtr.Zero"/>��Ԃ��B�����Y������ꍇ�͍ŏ��̈��Ԃ�</returns>
        private static IntPtr GetUmamusumeWindowHandle()
        {
            Process[] processes = Process.GetProcessesByName("umamusume");
            if (processes.Length == 0)
            {
                Debug.WriteLine("umamusume not exist");
                return IntPtr.Zero;
            }

            IntPtr handle = IntPtr.Zero;

            foreach (var process in processes)
            {
                EnumWindows((hWnd, lParam) =>
                {
                    IntPtr parent = GetParent(hWnd);

                    if (parent == IntPtr.Zero)
                    {
                        uint windowProcessId;
                        GetWindowThreadProcessId(hWnd, out windowProcessId);
                        if (windowProcessId == process.Id)
                        {
                            StringBuilder builder = new StringBuilder(256);
                            GetWindowText(hWnd, builder, builder.Capacity);
                            string windowTitle = builder.ToString();
                            if (!string.IsNullOrEmpty(windowTitle))
                            {
                                Debug.WriteLine($"Handle: {hWnd} Name: {windowTitle}");
                            }

                            handle = hWnd;
                            return false;
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }

            return handle;
        }

        /// <summary>
        /// �w�肵���A�X�y�N�g���Screen��z��Ŏ擾����
        /// </summary>
        /// <param name="width">16:9���擾����Ȃ�16���w�肷��</param>
        /// <param name="height">16:9���擾����Ȃ�9���w�肷��</param>
        /// <returns>�Y���Ȃ��̏ꍇ��̔z���Ԃ�</returns>
        private static Screen[] GetSpecificAspectRatioScreens(uint width, uint height)
        {
            List<Screen> screens = new List<Screen>();

            foreach (Screen screen in Screen.AllScreens)
            {
                int gcd = GetGCD(screen.Bounds.Width, screen.Bounds.Height);
                int aspectWidth = screen.Bounds.Width / gcd;
                int aspectHeight = screen.Bounds.Height / gcd;

                Debug.WriteLine($"Name: {screen.DeviceName} width: {screen.Bounds.Width} Height: {screen.Bounds.Height} AspectRatio: {aspectWidth}:{aspectHeight}");

                if (aspectWidth == width && aspectHeight == height)
                {
                    screens.Add(screen);
                }
            }

            return screens.ToArray();
        }
        
        /// <summary>
        /// 16:9��Screen��z��Ŏ擾����
        /// </summary>
        /// <returns>�Y���Ȃ��̏ꍇ��̔z���Ԃ�</returns>
        private static Screen[] GetHorizontalWideScreens()
        {
            return GetSpecificAspectRatioScreens(16, 9);
        }

        /// <summary>
        /// 9:16��Screen��z��Ŏ擾����
        /// </summary>
        /// <returns>�Y���Ȃ��̏ꍇ��̔z���Ԃ�</returns>
        private static Screen[] GetVerticalWideScreens()
        {
            return GetSpecificAspectRatioScreens(9, 16);
        }

        /// <summary>
        /// 2�̒l�̍ő���񐔂��擾����
        /// </summary>
        /// <param name="a">�ЂƂ߂̒l</param>
        /// <param name="b">�ӂ��߂̒l</param>
        /// <returns></returns>
        private static int GetGCD(int a, int b)
        { 
            int gcd = b == 0 ? a : GetGCD(b, a % b);
            Debug.WriteLine($"GCD: {gcd}");
            return gcd;
        }

        /// <summary>
        /// �w��̃E�B���h�E�͉������𔻒肷��
        /// </summary>
        /// <param name="hWnd">����Ώۂ̃E�B���h�E�n���h��</param>
        /// <returns>�����̏ꍇtrue��Ԃ��A�����`��c���̏ꍇfalse��Ԃ�</returns>
        private static bool IsHorizontal(IntPtr hWnd) {
            RECT rect;
            if (GetClientRect(hWnd, out rect))
            {
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                return width > height;
            } else
            {
                return false;
            }
        }

        /// <summary>
        /// �w��̃E�B���h�E�̘g���폜���A���T�C�Y���֎~����
        /// </summary>
        /// <param name="hWnd">�ݒ�Ώۂ̃E�B���h�E�n���h��</param>
        private static void DisableBorderAndResize(IntPtr hWnd)
        {
            int style = GetWindowLong(hWnd, GWL_STYLE);

            if ((style & WS_BORDER) != 0)
            {
                Debug.WriteLine("�{�[�_�[�͗L���ł�");
            }
            else
            {
                Debug.WriteLine("�{�[�_�[�͖����ł�");
            }

            if ((style & WS_CAPTION) != 0)
            {
                Debug.WriteLine("�L���v�V�����͗L���ł�");
            }
            else
            {
                Debug.WriteLine("�L���v�V�����͖����ł�");
            }

            style &= ~(WS_BORDER | WS_CAPTION | WS_SIZEBOX);
            SetWindowLong(hWnd, GWL_STYLE, style);

            int style2 = GetWindowLong(hWnd, GWL_STYLE);

            if ((style2 & WS_BORDER) != 0)
            {
                Debug.WriteLine("�{�[�_�[�͗L���ł�");
            }
            else
            {
                Debug.WriteLine("�{�[�_�[�͖����ł�");
            }

            if ((style2 & WS_CAPTION) != 0)
            {
                Debug.WriteLine("�L���v�V�����͗L���ł�");
            }
            else
            {
                Debug.WriteLine("�L���v�V�����͖����ł�");
            }
        }

        /// <summary>
        /// �Ώۂ̃E�B���h�E�ɃX�^�C����T�C�Y�̕ύX���K�v���𔻒肷��
        /// </summary>
        /// <param name="hWnd">����Ώۂ̃E�B���h�E�n���h��</param>
        /// <returns>�X�^�C����T�C�Y�����Ғl�ƈقȂ�ꍇtrue��Ԃ�</returns>
        private static bool IsAdjustRequired(IntPtr hWnd)
        {
            int style = GetWindowLong(hWnd, GWL_STYLE);
            if ((style & WS_BORDER) == 0 && (style & WS_CAPTION) == 0)
            {
                RECT rect;
                GetClientRect(hWnd, out rect);

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                if (IsHorizontal(hWnd))
                {
                    foreach (Screen screen in GetHorizontalWideScreens())
                    {
                        if (screen.Bounds.Width != width || screen.Bounds.Height != height)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    foreach (Screen screen in GetVerticalWideScreens())
                    {
                        if (screen.Bounds.Width != width || screen.Bounds.Height != height)
                        {
                            return true;
                        }
                    }
                }
            }
            else
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// �Ώۂ̃E�B���h�E�n���h���̃E�B���h�E�ɑ΂��Ē������s��
        /// </summary>
        /// <param name="hWnd">�����Ώۂ̃E�B���h�E�n���h��</param>
        private static void Adjust(IntPtr hWnd)
        {
            if (IsHorizontal(hWnd) && IsAdjustRequired(hWnd))
            {
                Debug.WriteLine("�E�}���͌��݉������\���ł�");

                foreach (Screen screen in GetHorizontalWideScreens())
                {
                    Debug.WriteLine($"16:9�̃��j�^�Ƃ���\"{screen.DeviceName}\"��������܂���");

                    DisableBorderAndResize(hWnd);
                    MoveWindow(hWnd, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height, true);
                    break;
                }
            }
            else if (!IsHorizontal(hWnd) && IsAdjustRequired(hWnd))
            {
                {
                    Debug.WriteLine("�E�}���͌��ݏc�����\���ł�");

                    foreach (Screen screen in GetVerticalWideScreens())
                    {
                        Debug.WriteLine($"9:16�̃��j�^�Ƃ���\"{screen.DeviceName}\"��������܂���");

                        DisableBorderAndResize(hWnd);
                        MoveWindow(hWnd, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height, true);
                        break;
                    }
                }
            }
        }

        private static void contextMenu_Exit(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType,
            IntPtr hWnd, int idObject, int idChild, uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hWnd == umaWindowHandle)
            {
                Debug.WriteLine($"eventType: {eventType:x} hwnd: {hWnd}");
                // Thread.Sleep(1000);
                Adjust(hWnd);
            }
        }
    }
}