﻿using SWTORCombatParser.Model.Overlays;
using SWTORCombatParser.Model.Timers;
using SWTORCombatParser.ViewModels.Timers;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace SWTORCombatParser.Views.Timers
{
    /// <summary>
    /// Interaction logic for TimersWindow.xaml
    /// </summary>
    public interface ITimerWindow
    {
        double Height { get; set; }
        double Width { get; set; }
        double Top { get; set; }
        double Left { get; set; }
        void Hide();
        void Show();
        void makeTransparent(bool shouldLock);
        void SetPlayer(string player);
        void SetIdText(string text);
    }
    public partial class TimersWindow : Window, ITimerWindow
    {
        private ITimerWindowViewModel viewModel;
        private string _currentPlayerName;
        public TimersWindow(ITimerWindowViewModel vm)
        {
            viewModel = vm;
            DataContext = vm;
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Close,
    new ExecutedRoutedEventHandler(delegate (object sender, ExecutedRoutedEventArgs args) { this.Close(); })));
            MainWindowClosing.Closing += CloseOverlay;
            vm.OnLocking += makeTransparent;
            vm.OnCharacterDetected += SetPlayer;
            vm.CloseRequested += CloseOverlay;
            Loaded += OnLoaded;

        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RemoveFromAppWindow();
            makeTransparent(true);
        }

        private void RemoveFromAppWindow()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, (extendedStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }

        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int GWL_EXSTYLE = (-20);
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public void makeTransparent(bool shouldLock)
        {
            Dispatcher.Invoke(() =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (shouldLock)
                {
                    BackgroundArea.Opacity = 0.1f;
                    ScrollView.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, (extendedStyle | WS_EX_TRANSPARENT) & ~WS_EX_APPWINDOW);
                }
                else
                {
                    BackgroundArea.Opacity = 0.45f;
                    ScrollView.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    //Remove the WS_EX_TRANSPARENT flag from the extended window style
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);

                }
            });

        }
        private void CloseOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                viewModel.Closing();
            });

        }
        public void SetIdText(string text)
        {
            IdentiferText.Text = text;
        }
        public void SetPlayer(string playerName)
        {
            _currentPlayerName = playerName;
        }
        public void DragWindow(object sender, MouseButtonEventArgs args)
        {
            DragMove();
        }
        public void UpdateDefaults(object sender, MouseButtonEventArgs args)
        {
            if (_currentPlayerName == "Encounter")
                DefaultGlobalOverlays.SetDefault("Encounter", new Point() { X = Left, Y = Top }, new Point() { X = Width, Y = Height });
            else
                DefaultTimersManager.SetDefaults(new Point() { X = Left, Y = Top }, new Point() { X = Width, Y = Height }, _currentPlayerName);
        }

        private void Thumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var yadjust = Height + e.VerticalChange;
            var xadjust = Width + e.HorizontalChange;
            if (xadjust > 0)
                SetValue(WidthProperty, xadjust);
            if (yadjust > 0)
                SetValue(HeightProperty, yadjust);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_currentPlayerName == "Encounter")
                DefaultGlobalOverlays.SetDefault("Encounter", new Point() { X = Left, Y = Top }, new Point() { X = Width, Y = Height });
            else
                DefaultTimersManager.SetDefaults(new Point() { X = Left, Y = Top }, new Point() { X = Width, Y = Height }, _currentPlayerName);
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Thumb_MouseEnter(object sender, MouseEventArgs e)
        {
            if (viewModel.OverlaysMoveable)
            {
                Mouse.OverrideCursor = Cursors.SizeNWSE;
            }
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            if (viewModel.OverlaysMoveable)
            {
                Mouse.OverrideCursor = Cursors.Hand;
            }
        }

        private void Grid_MouseLeave(object sender, MouseEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            CloseOverlay();
        }
    }
}
