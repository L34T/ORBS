﻿using SWTORCombatParser.DataStructures;
using SWTORCombatParser.Model.CloudRaiding;
using SWTORCombatParser.Model.LogParsing;
using SWTORCombatParser.Model.Overlays;
using SWTORCombatParser.Model.Timers;
using SWTORCombatParser.Utilities;
using SWTORCombatParser.ViewModels.Overlays.BossFrame;
using SWTORCombatParser.ViewModels.Overlays.Room;
using SWTORCombatParser.ViewModels.Timers;
using SWTORCombatParser.Views.Overlay;
using SWTORCombatParser.Views.Overlay.Room;
using SWTORCombatParser.Views.Timers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SWTORCombatParser.DataStructures.ClassInfos;
using SWTORCombatParser.Model.CombatParsing;
using SWTORCombatParser.ViewModels.Overlays.RaidHots;
using SWTORCombatParser.Views.Overlay.BossFrame;
using SWTORCombatParser.Views.Overlay.PvP;
using SWTORCombatParser.Views.Overlay.RaidHOTs;
using SWTORCombatParser.ViewModels.Overlays.PvP;

namespace SWTORCombatParser.ViewModels.Overlays
{
    public class OverlayViewModel : INotifyPropertyChanged
    {

        private List<OverlayInstanceViewModel> _currentOverlays = new List<OverlayInstanceViewModel>();
        private Dictionary<string, OverlayInfo> _overlayDefaults = new Dictionary<string, OverlayInfo>();
        private string _currentCharacterDiscipline = "None";
        private bool overlaysLocked = true;
        private LeaderboardType selectedLeaderboardType;
        private TimersCreationViewModel _timersViewModel;
        private OthersOverlaySetupViewModel _otherOverlayViewModel;
        private double maxScalar = 1.5d;
        private double minScalar = 0.5d;
        private double sizeScalar = 1d;
        private string sizeScalarString ="1";
        private bool historicalParseFinished = false;
        public event Action OverlayLockStateChanged = delegate{};

        public TimersCreationView TimersView { get; set; }
        public OtherOverlaySetupView OthersSetupView { get; set; }

        public ObservableCollection<OverlayType> AvailableDamageOverlays { get; set; } = new ObservableCollection<OverlayType>();
        public ObservableCollection<OverlayType> AvailableHealOverlays { get; set; } = new ObservableCollection<OverlayType>();
        public ObservableCollection<OverlayType> AvailableMitigationOverlays { get; set; } = new ObservableCollection<OverlayType>();
        public ObservableCollection<OverlayType> AvailableGeneralOverlays { get; set; } = new ObservableCollection<OverlayType>();
        public List<LeaderboardType> LeaderboardTypes { get; set; } = new List<LeaderboardType>();
        public LeaderboardType SelectedLeaderboardType
        {
            get => selectedLeaderboardType;
            set
            {
                selectedLeaderboardType = value;
                Leaderboards.UpdateLeaderboardType(selectedLeaderboardType);
            }
        }
        public string SizeScalarString
        {
            get => sizeScalarString; set
            {
                sizeScalarString = value;
                var stringVal = 0d;
                if (double.TryParse(sizeScalarString, out stringVal))
                {
                    SizeScalar = stringVal;
                }
            }
        }
        public double SizeScalar
        {
            get { return sizeScalar; }
            set
            {
                sizeScalar = value;
                if (sizeScalar > maxScalar)
                { 
                    SizeScalarString = maxScalar.ToString();
                    return;
                }
                if (sizeScalar < minScalar)
                { 
                    SizeScalarString = minScalar.ToString();
                    return;
                }
                _currentOverlays.ForEach(overlay => overlay.SizeScalar = sizeScalar);
                Settings.WriteSetting<double>("overlay_bar_scale",sizeScalar);
                OnPropertyChanged();
            }
        }
        public OverlayViewModel()
        {
            CombatLogStateBuilder.PlayerDiciplineChanged += UpdateOverlaysForDiscipline;
            CombatLogStreamer.HistoricalLogsFinished += FinishHistoricalParse;
            CombatLogStreamer.HistoricalLogsStarted += HistoricalLogsStarted;
            sizeScalar = Settings.ReadSettingOfType<double>("overlay_bar_scale");
            sizeScalarString = sizeScalar.ToString();
            LeaderboardTypes = EnumUtil.GetValues<LeaderboardType>().ToList();
            SelectedLeaderboardType = LeaderboardSettings.ReadLeaderboardSettings();
            DefaultCharacterOverlays.Init();
            DefaultGlobalOverlays.Init();
            var enumVals = EnumUtil.GetValues<OverlayType>().OrderBy(d => d.ToString());
            foreach (var enumVal in enumVals.Where(e => e != OverlayType.None))
            {
                if (enumVal == OverlayType.DPS || enumVal == OverlayType.BurstDPS || enumVal == OverlayType.FocusDPS)
                    AvailableDamageOverlays.Add(enumVal);
                if (enumVal == OverlayType.HPS || enumVal == OverlayType.EHPS || enumVal == OverlayType.BurstEHPS || enumVal == OverlayType.HealReactionTime || enumVal == OverlayType.HealReactionTimeRatio || enumVal == OverlayType.TankHealReactionTime)
                    AvailableHealOverlays.Add(enumVal);
                if (enumVal == OverlayType.Mitigation || enumVal == OverlayType.ShieldAbsorb || enumVal == OverlayType.ProvidedAbsorb || enumVal == OverlayType.DamageTaken || enumVal == OverlayType.DamageAvoided || enumVal == OverlayType.DamageSavedDuringCD)
                    AvailableMitigationOverlays.Add(enumVal);
                if (enumVal == OverlayType.APM || enumVal == OverlayType.InterruptCount || enumVal == OverlayType.ThreatPerSecond || enumVal == OverlayType.Threat)
                    AvailableGeneralOverlays.Add(enumVal);
            }
            TimersView = new TimersCreationView();
            _timersViewModel = new TimersCreationViewModel();
            TimersView.DataContext = _timersViewModel;

            _otherOverlayViewModel = new OthersOverlaySetupViewModel();
            OthersSetupView = new OtherOverlaySetupView();
            OthersSetupView.DataContext = _otherOverlayViewModel;
        }

        private void UpdateOverlaysForDiscipline(Entity character, SWTORClass arg2)
        {
            var nextDiscipline = character.Name + "_" + arg2.Discipline;
            if (_currentCharacterDiscipline == nextDiscipline)
                return;
            ResetOverlays();
            _currentCharacterDiscipline = nextDiscipline;
            _timersViewModel.TryShow();
            _overlayDefaults = DefaultCharacterOverlays.GetCharacterDefaults(_currentCharacterDiscipline);
            if (historicalParseFinished)
            {
                UpdateOverlays();
            }
        }
        private void UpdateOverlays()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (_overlayDefaults.Count == 0)
                    return;
                if (_overlayDefaults.First().Value.Locked)
                {
                    OverlaysLocked = true;
                    OnPropertyChanged("OverlaysLocked");
                }
                var enumVals = EnumUtil.GetValues<OverlayType>();
                foreach (var enumVal in enumVals.Where(e => e != OverlayType.None))
                {
                    if (!_overlayDefaults.ContainsKey(enumVal.ToString()))
                        continue;
                    if (_overlayDefaults[enumVal.ToString()].Acive)
                        CreateOverlay(enumVal);
                }
                _currentOverlays.ForEach(o => o.CharacterDetected(_currentCharacterDiscipline));
            });
        }
        private void FinishHistoricalParse(DateTime combatEndTime, bool localPlayerIdentified)
        {
            historicalParseFinished = true;
            if (!localPlayerIdentified)
                return;
            var localPlayer = CombatLogStateBuilder.CurrentState.LocalPlayer;
            var currentDiscipline = CombatLogStateBuilder.CurrentState.GetLocalPlayerClassAtTime(combatEndTime);
            if (localPlayer == null)
                return;
            UpdateOverlaysForDiscipline(localPlayer, currentDiscipline);
            UpdateOverlays();
        }
        private void HistoricalLogsStarted()
        {
            historicalParseFinished = false;
        }

        public ICommand GenerateOverlay => new CommandHandler(CreateOverlay);

        private void CreateOverlay(object type)
        {
            OverlayType overlayType = (OverlayType)type;
            if (_currentOverlays.Any(o => o.CreatedType == overlayType))
                return;

            var viewModel = new OverlayInstanceViewModel(overlayType);
            DefaultCharacterOverlays.SetActiveStateCharacter(viewModel.Type.ToString(), true, _currentCharacterDiscipline);
            viewModel.OverlayClosed += RemoveOverlay;
            viewModel.OverlaysMoveable = !OverlaysLocked;
            viewModel.SizeScalar = SizeScalar;
            _currentOverlays.Add(viewModel);
            var overlay = new InfoOverlay(viewModel);
            overlay.SetPlayer(_currentCharacterDiscipline);
            var stringType = viewModel.Type.ToString();
            if (_overlayDefaults.ContainsKey(stringType))
            {
                overlay.Top = _overlayDefaults[stringType].Position.Y;
                overlay.Left = _overlayDefaults[stringType].Position.X;
                overlay.Width = _overlayDefaults[stringType].WidtHHeight.X;
                overlay.Height = _overlayDefaults[stringType].WidtHHeight.Y;
            }
            overlay.Show();
            viewModel.Refresh(CombatIdentifier.CurrentCombat);
            if (OverlaysLocked)
                viewModel.LockOverlays();
        }

        private void RemoveOverlay(OverlayInstanceViewModel obj)
        {
            _currentOverlays.Remove(obj);
        }
        public void HideOverlays()
        {
            ResetOverlays();
            _otherOverlayViewModel.HideAll();
        }
        public void ResetOverlays()
        {
            _currentCharacterDiscipline = "";
            foreach (var overlay in _currentOverlays.ToList())
            {
                overlay.RequestClose();
            }
            _timersViewModel.HideTimers();
            _currentOverlays.Clear();
        }
        public bool OverlaysLocked
        {
            get => overlaysLocked;
            set
            {
                overlaysLocked = value;
                _timersViewModel.UpdateLock(value);
                if (overlaysLocked)
                {
                    _otherOverlayViewModel.UpdateLock(overlaysLocked);
                }
                else
                {
                    _otherOverlayViewModel.UpdateLock(overlaysLocked);
                }
                    
                ToggleOverlayLock();
                OverlayLockStateChanged();
                OnPropertyChanged();
            }
        }

        private void ToggleOverlayLock()
        {
            if (!OverlaysLocked)
                _currentOverlays.ForEach(o => o.UnlockOverlays());
            else
                _currentOverlays.ForEach(o => o.LockOverlays());
            DefaultCharacterOverlays.SetLockedStateCharacter(OverlaysLocked, _currentCharacterDiscipline);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        internal void LiveParseStarted(bool state)
        {
            _otherOverlayViewModel.UpdateLiveParse(state);
        }
    }
}
