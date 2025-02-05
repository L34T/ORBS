﻿using SWTORCombatParser.DataStructures;
using SWTORCombatParser.DataStructures.ClassInfos;
using SWTORCombatParser.Model.LogParsing;
using SWTORCombatParser.Model.Overlays;
using SWTORCombatParser.Utilities;
using SWTORCombatParser.Views.Overlay.RaidHOTs;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SWTORCombatParser.ViewModels.Overlays.RaidHots
{
    public class RaidHotsConfigViewModel : INotifyPropertyChanged
    {
        private string raidFrameRows = "4";
        private string raidFrameColumns = "2";
        private RaidFrameOverlay _currentOverlay;
        private RaidFrameOverlayViewModel _currentOverlayViewModel;
        private bool _isRaidFrameEditable = false;
        private bool raidHotsEnabled = false;
        private string _editText = "Reposition\nRaid Frame";
        private string _unEditText = "Lock Raid Frame";
        private string toggleEditText;
        private string _currentCharacter = "no character";
        private bool _decreasedSpecificity;
        private bool canDetect = true;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<bool> EnabledChanged = delegate { };

        public RaidHotsConfigViewModel()
        {
            RaidFrameOverlayManager.Init();
            HotkeyHandler.OnRefreshHOTsHotkey += AutoDetection;
            _currentOverlay = new RaidFrameOverlay();
            CombatLogStreamer.HistoricalLogsFinished += (t, b) =>
            {
                if (!b)
                    return;
                var playerName = CombatLogStateBuilder.CurrentState.LocalPlayer.Name;
                var classInfo = CombatLogStateBuilder.CurrentState.GetLocalPlayerClassAtTime(t);
                _currentCharacter = playerName + "/" + classInfo.Discipline;
                UpdateVisualsBasedOnRole(classInfo);
            };
            CombatLogStateBuilder.PlayerDiciplineChanged += SetClass;
            _currentOverlayViewModel = new RaidFrameOverlayViewModel(_currentOverlay) { Columns = int.Parse(RaidFrameColumns), Rows = int.Parse(RaidFrameRows), Width = 500, Height = 450, Editable = _isRaidFrameEditable };
            _currentOverlay.DataContext = _currentOverlayViewModel;

            ToggleEditText = _editText;

            var defaults = RaidFrameOverlayManager.GetDefaults(_currentCharacter);
            _currentOverlay.Width = defaults.WidtHHeight.X;
            _currentOverlay.Height = defaults.WidtHHeight.Y;
            _currentOverlay.Top = defaults.Position.Y;
            _currentOverlay.Left = defaults.Position.X;
            Task.Run(() =>
            {
                RaidFrameRows = defaults.Rows.ToString();
                Thread.Sleep(100);
                RaidFrameColumns = defaults.Columns.ToString();
            });


        }

        public bool DecreasedSpecificity
        {
            get => _decreasedSpecificity;
            set
            {
                _decreasedSpecificity = value;
                _currentOverlayViewModel.SetTextMatchAccuracy(_decreasedSpecificity);
            }
        }

        public ICommand ManuallyRefreshPlayersCommand => new CommandHandler(_ => { AutoDetection(); });
        public bool RaidFrameEditable => _isRaidFrameEditable;
        public string ToggleEditText
        {
            get => toggleEditText; set
            {
                toggleEditText = value;
                OnPropertyChanged();
            }
        }
        public string RaidFrameRows
        {
            get => raidFrameRows;
            set
            {
                if (value.Any(v => !char.IsDigit(v)))
                    return;
                raidFrameRows = value;
                if (raidFrameRows == "")
                    return;
                _currentOverlayViewModel.Rows = int.Parse(RaidFrameRows);
                RaidFrameOverlayManager.SetRowsColumns(_currentOverlayViewModel.Rows, _currentOverlayViewModel.Columns, _currentCharacter);
                OnPropertyChanged();
            }
        }
        public string RaidFrameColumns
        {
            get => raidFrameColumns; set
            {
                if (value.Any(v => !char.IsDigit(v)))
                    return;
                raidFrameColumns = value;
                if (raidFrameColumns == "")
                    return;
                _currentOverlayViewModel.Columns = int.Parse(RaidFrameColumns);
                RaidFrameOverlayManager.SetRowsColumns(_currentOverlayViewModel.Rows, _currentOverlayViewModel.Columns, _currentCharacter);
                OnPropertyChanged();
            }
        }
        public void HideRaidHots()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _currentOverlay.Hide();
            });
        }
        public bool CanDetect
        {
            get => canDetect; set
            {
                canDetect = value;
                OnPropertyChanged();
            }
        }
        public bool RaidHotsEnabled
        {
            get => raidHotsEnabled; set
            {
                if (raidHotsEnabled == value)
                    return;
                raidHotsEnabled = value;

                if (raidHotsEnabled)
                {
                    _currentOverlay.Show();
                }
                else
                {
                    _currentOverlay.Hide();
                    _currentOverlayViewModel.CurrentNames.Clear();
                }

                EnabledChanged(raidHotsEnabled);
                _currentOverlayViewModel.Active = raidHotsEnabled;
                RaidFrameOverlayManager.SetActiveState(RaidHotsEnabled, _currentCharacter);
                OnPropertyChanged();
            }
        }
        private void StartPositioning(bool isLocked)
        {
            if (!isLocked)
            {
                ToggleEditText = _unEditText;
                _isRaidFrameEditable = true;
                _currentOverlayViewModel.Editable = true;
            }
            else
            {
                ToggleEditText = _editText;
                _isRaidFrameEditable = false;
                _currentOverlayViewModel.Editable = false;
            }
            OnPropertyChanged("RaidFrameEditable");
        }

        private void AutoDetection()
        {
            if (!RaidHotsEnabled || !CanDetect)
                return;
            CanDetect = false;
            Task.Run(() =>
            {
                var raidFrameBitmap = RaidFrameScreenGrab.GetRaidFrameBitmapStream(_currentOverlayViewModel.TopLeft,
                    _currentOverlayViewModel.Width, _currentOverlayViewModel.Height, _currentOverlayViewModel.Rows);
                var names = AutoHOTOverlayPosition.GetCurrentPlayerLayoutLOCAL(_currentOverlayViewModel.TopLeft,
                    raidFrameBitmap, _currentOverlayViewModel.Rows, _currentOverlayViewModel.Columns, _currentOverlayViewModel.Height, _currentOverlayViewModel.Width).Result;
                raidFrameBitmap.Dispose();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _currentOverlayViewModel.UpdateNames(names);
                });
                CanDetect = true;

            });
        }

        internal void ToggleLock(bool overlaysLocked)
        {
            StartPositioning(overlaysLocked);
        }
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void UpdateVisualsBasedOnRole(SWTORClass mostRecentDiscipline)
        {
            if (mostRecentDiscipline == null)
                return;
            if (mostRecentDiscipline.Role == Role.Healer)
            {
                var defaults = RaidFrameOverlayManager.GetDefaults(_currentCharacter);
                RaidFrameRows = defaults.Rows.ToString();
                RaidFrameColumns = defaults.Columns.ToString();
                App.Current.Dispatcher.Invoke(() =>
                {
                    RaidHotsEnabled = defaults.Acive;
                    _currentOverlayViewModel.FirePlayerChanged(_currentCharacter);
                });
            }
            else
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    RaidHotsEnabled = false;
                });
            }
        }

        private void SetClass(Entity arg1, SWTORClass arg2)
        {
            if (_currentCharacter == arg1.Name + "/" + arg2.Discipline)
                return;
            _currentCharacter = arg1.Name + "/" + arg2.Discipline;
            UpdateVisualsBasedOnRole(arg2);
        }
    }
}
