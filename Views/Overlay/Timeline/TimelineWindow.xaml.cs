using SWTORCombatParser.Model.Overlays;
using SWTORCombatParser.Utilities;
using SWTORCombatParser.ViewModels.Overlays.Timeline;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SWTORCombatParser.Views.Overlay.Timeline
{
    public partial class TimelineWindow : Window
    {
        private TimelineWindowViewModel viewModel;
        private Canvas timelineCanvas;
        private bool _closed;
        private bool _hidden;
        private string _currentPlayerName;
        private object lockObj = new object();

        public TimeSpan MaxDuration { get; set; }
        public TimeSpan CurrentTime { get; set; }

        public TimelineWindow(TimelineWindowViewModel vm)
        {
            viewModel = vm;
            DataContext = vm;
            InitializeComponent();

            timelineCanvas = TimelineCanvas;
            viewModel.OnInit += SetCurrentTimeAndUpdate;
            viewModel.OnUpdateTimeline += SetCurrentTimeAndUpdate;
            viewModel.AreaEntered += SetAreaName;

            timelineCanvas.SizeChanged += (s, e) =>
            {
                if (viewModel?.AllTimelineElements?.Count > 0)
                    OnUpdateTimelinePositions();
            };

            vm.OnLocking += makeTransparent;
            vm.OnHiding += HideOverlay;
            vm.OnShowing += ShowOverlay;
            vm.CloseRequested += CloseOverlay;

            Loaded += OnLoaded;
            MainWindowClosing.Closing += CloseOverlay;
            HotkeyHandler.OnHideOverlaysHotkey += ToggleHide;
        }

        private void ToggleHide()
        {
            if (_hidden)
                ShowOverlay();
            else
                HideOverlay();
        }

        private void ShowOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!_closed)
                {
                    Show();
                    _hidden = false;
                }
            });
        }

        private void HideOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Hide();
                _hidden = true;
            });
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RemoveFromAppWindow();
        }

        private void RemoveFromAppWindow()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, (extendedStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }

        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int GWL_EXSTYLE = (-20);
        private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

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
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    BackgroundArea.Opacity = 0.45f;
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
                }
            });
        }

        public void SetPlayer(string playerName)
        {
            _currentPlayerName = playerName;
        }

        private void CloseOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                _closed = true;
                MainWindowClosing.Closing -= CloseOverlay;
                HotkeyHandler.OnHideOverlaysHotkey -= ToggleHide;
                Close();
            });
        }

        private void SetAreaName(string name, string difficulty, string playerCount)
        {
            EncounterName.Text = name + $" {{{difficulty} {playerCount}}}";
        }

        private void SetCurrentTimeAndUpdate(TimeSpan currentTime)
        {
            lock (lockObj)
            {
                CurrentTime = currentTime;
                OnUpdateTimelinePositions();
            }
        }

        private void OnUpdateTimelinePositions()
        {
            lock (lockObj)
            {
                if (viewModel == null || timelineCanvas == null)
                    return;
                timelineCanvas.Children.Clear();
                double maxDuration = viewModel.MaxDuration.TotalSeconds;
                double canvasWidth = timelineCanvas.ActualWidth;
                if (maxDuration == 0 || canvasWidth == 0)
                    return;

                foreach (var element in viewModel.AllTimelineElements)
                {
                    double elementStartTime = element.StartTime.TotalSeconds;
                    double positionLeft = (elementStartTime / maxDuration) * canvasWidth;

                    if (element.IsLeaderboard)
                    {
                        var border = new Border
                        {
                            Background = Brushes.OrangeRed,
                            CornerRadius = new CornerRadius(5),
                            Padding = new Thickness(5),
                            Width = (element.TTK.TotalSeconds / maxDuration) * canvasWidth,
                            Child = new TextBlock
                            {
                                Text = element.BossName,
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        };
                        Canvas.SetLeft(border, positionLeft);
                        Canvas.SetTop(border, 53);
                        timelineCanvas.Children.Add(border);
                    }
                    if (element.IsFreshKill)
                    {
                        var border = new Border
                        {
                            Background = Brushes.LimeGreen,
                            CornerRadius = new CornerRadius(5),
                            Padding = new Thickness(5),
                            Width = (element.TTK.TotalSeconds / maxDuration) * canvasWidth,
                            Child = new TextBlock
                            {
                                Text = element.BossName,
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        };
                        Canvas.SetLeft(border, positionLeft);
                        Canvas.SetTop(border, 20);
                        timelineCanvas.Children.Add(border);
                    }
                    if (!element.IsLeaderboard && !element.IsFreshKill)
                    {
                        var border = new Border
                        {
                            Background = Brushes.LightBlue,
                            CornerRadius = new CornerRadius(5),
                            Padding = new Thickness(5),
                            Width = (element.TTK.TotalSeconds / maxDuration) * canvasWidth,
                            Child = new TextBlock
                            {
                                FontSize = 10,
                                Text = element.BossName,
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        };
                        Canvas.SetLeft(border, positionLeft);
                        Canvas.SetTop(border, 20);
                        timelineCanvas.Children.Add(border);
                    }
                }

                UpdateCurrentTimeIndicator(maxDuration, canvasWidth);
            }
        }

        private void UpdateCurrentTimeIndicator(double maxDuration, double canvasWidth)
        {
            double positionLeft = (CurrentTime.TotalSeconds / maxDuration) * canvasWidth;

            var currentTimeIndicator = new Border()
            {
                CornerRadius = new CornerRadius(5),
                Width = 3,
                Height = 40,
                Background = Brushes.ForestGreen
            };

            Canvas.SetLeft(currentTimeIndicator, positionLeft);
            Canvas.SetTop(currentTimeIndicator, 30);
            DurationInfo.Text = $"{CurrentTime.Minutes}m {CurrentTime.Seconds}s";
            timelineCanvas.Children.Add(currentTimeIndicator);
        }

        public void DragWindow(object sender, MouseButtonEventArgs args)
        {
            DragMove();
        }

        public void UpdateDefaults(object sender, MouseButtonEventArgs args)
        {
            DefaultGlobalOverlays.SetDefault("TimelineOverlay", new Point() { X = Left, Y = Top }, new Point() { X = Width, Y = Height });
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
            DefaultGlobalOverlays.SetDefault("TimelineOverlay", new Point() { X = Left, Y = Top }, new Point() { X = Width, Y = Height });
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Thumb_MouseEnter(object sender, MouseEventArgs e)
        {
            if (viewModel.OverlaysMoveable)
                Mouse.OverrideCursor = Cursors.SizeNWSE;
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            if (viewModel.OverlaysMoveable)
                Mouse.OverrideCursor = Cursors.Hand;
        }

        private void Grid_MouseLeave(object sender, MouseEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            viewModel.UserDisabled();
            CloseOverlay();
        }
    }
}
