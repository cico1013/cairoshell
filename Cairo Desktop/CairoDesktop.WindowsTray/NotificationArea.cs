﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using static CairoDesktop.Interop.NativeMethods;

namespace CairoDesktop.WindowsTray
{
    public class NotificationArea : DependencyObject, IDisposable
    {
        IWindowsHooksWrapper hooksWrapper = new WindowsHooksWrapper();
        private SystrayDelegate trayDelegate;
        private object _lockObject = new object();
        public IntPtr Handle;
        public bool IsFailed = false;

        private DateTime _lastLClick = DateTime.Now;
        private DateTime _lastRClick = DateTime.Now;
        private IntPtr _lastClickHwnd;

        private static NotificationArea _instance = new NotificationArea();
        public static NotificationArea Instance
        {
            get { return _instance; }
        }

        public ObservableCollection<NotifyIcon> TrayIcons
        {
            get
            {
                return GetValue(iconListProperty) as ObservableCollection<NotifyIcon>;
            }
            set
            {
                SetValue(iconListProperty, value);
            }
        }

        private static DependencyProperty iconListProperty = DependencyProperty.Register("TrayIcons", typeof(ObservableCollection<NotifyIcon>), typeof(NotificationArea), new PropertyMetadata(new ObservableCollection<NotifyIcon>()));

        public void Initialize()
        {
            try
            {
                trayDelegate = new SystrayDelegate(SysTrayCallback);
                hooksWrapper.SetSystrayCallback(trayDelegate);
                Handle = hooksWrapper.InitializeSystray();
                hooksWrapper.Run();

                if (Configuration.Settings.EnableSysTrayRehook)
                {
                    DispatcherTimer trayRehook = new DispatcherTimer(DispatcherPriority.Background, this.Dispatcher);
                    trayRehook.Interval = new TimeSpan(0, 0, 10);
                    trayRehook.Tick += trayRehook_Tick;
                    trayRehook.Start();
                }
            }
            catch
            {
                IsFailed = true;
            }
        }

        private void trayRehook_Tick(object sender, EventArgs e)
        {
            // check if setting has changed
            if (Configuration.Settings.EnableSysTrayRehook)
            {
                Handle = hooksWrapper.InitializeSystray();
                hooksWrapper.Run();
            }
            else
            {
                (sender as DispatcherTimer).Stop();
            }
        }

        private bool SysTrayCallback(uint message, NOTIFYICONDATA nicData)
        {
            NotifyIcon trayIcon = new NotifyIcon((IntPtr)nicData.hWnd);
            trayIcon.UID = nicData.uID;

            lock (_lockObject)
            {
                if ((NIM)message == NIM.NIM_ADD || (NIM)message == NIM.NIM_MODIFY)
                {
                    try
                    {
                        bool exists = false;

                        if (nicData.dwState != 1)
                        {
                            foreach (NotifyIcon ti in TrayIcons)
                            {
                                if ((nicData.guidItem != Guid.Empty && nicData.guidItem == ti.GUID) || (ti.HWnd == (IntPtr)nicData.hWnd && ti.UID == nicData.uID))
                                {
                                    exists = true;
                                    trayIcon = ti;
                                    break;
                                }
                            }

                            trayIcon.Title = nicData.szTip;
                            try
                            {
                                trayIcon.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon((IntPtr)nicData.hIcon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            }
                            catch
                            {
                                if (trayIcon.Icon == null)
                                    trayIcon.Icon = Common.IconImageConverter.GetDefaultIcon();
                            }
                            trayIcon.HWnd = (IntPtr)nicData.hWnd;
                            trayIcon.UID = nicData.uID;
                            trayIcon.GUID = nicData.guidItem;
                            trayIcon.CallbackMessage = nicData.uCallbackMessage;

                            if (!exists)
                            {
                                TrayIcons.Add(trayIcon);
                                Trace.WriteLine("Added tray icon: " + trayIcon.Title);
                            }
                            else
                                Trace.WriteLine("Modified tray icon: " + trayIcon.Title);
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Unable to modify the icon in the collection. Error: " + ex.ToString());
                    }
                }
                else if ((NIM)message == NIM.NIM_DELETE)
                {
                    try
                    {
                        if (!TrayIcons.Contains(trayIcon))
                        {
                            // Nothing to remove.
                            return false;
                        }

                        // Woo! Using Linq to avoid iterating!
                        TrayIcons.Remove(trayIcon);

                        Trace.WriteLine("Removed tray icon: " + nicData.szTip);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Unable to remove the icon from the collection. Error: " + ex.ToString());
                    }
                }
            }
            return true;
        }

        public void Dispose()
        {
            if (!IsFailed && trayDelegate != null)
                hooksWrapper.ShutdownSystray();
        }

        public void IconMouseEnter(NotifyIcon icon, uint mouse)
        {
            if (!IsWindow(icon.HWnd))
            {
                TrayIcons.Remove(icon);
                return;
            }
            else
            {
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_MOUSEFIRST);
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_MOUSEHOVER);
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, NIN_POPUPOPEN);
            }
        }

        public void IconMouseLeave(NotifyIcon icon, uint mouse)
        {
            if (!IsWindow(icon.HWnd))
            {
                TrayIcons.Remove(icon);
                return;
            }
            else
            {
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_MOUSELEAVE);
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, NIN_POPUPCLOSE);
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_MOUSELAST);
            }
        }

        public void IconMouseClick(NotifyIcon icon, MouseButton button, uint mouse, int doubleClickTime)
        {
            Trace.WriteLine(String.Format("{0} mouse button clicked icon: {1}", button.ToString(), icon.Title));

            if (button == MouseButton.Left)
            {
                if (DateTime.Now.Subtract(_lastLClick).TotalMilliseconds <= doubleClickTime && _lastClickHwnd == icon.HWnd)
                {
                    PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_LBUTTONDBLCLK);
                }
                else
                {
                    PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_LBUTTONDOWN);
                }

                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, WM_LBUTTONUP);
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, (NIN_SELECT | ((uint)icon.UID << 16)));

                _lastLClick = DateTime.Now;
            }
            else if (button == MouseButton.Right)
            {
                if (DateTime.Now.Subtract(_lastRClick).TotalMilliseconds <= doubleClickTime && _lastClickHwnd == icon.HWnd)
                {
                    PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, (WM_RBUTTONDBLCLK | ((uint)icon.UID << 16)));
                }
                else
                {
                    PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, (WM_RBUTTONDOWN | ((uint)icon.UID << 16)));
                }

                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, (WM_RBUTTONUP | ((uint)icon.UID << 16)));
                PostMessage(icon.HWnd, (uint)icon.CallbackMessage, mouse, (WM_CONTEXTMENU | ((uint)icon.UID << 16)));

                _lastRClick = DateTime.Now;
            }

            _lastClickHwnd = icon.HWnd;

            SetForegroundWindow(icon.HWnd);
        }
    }
}