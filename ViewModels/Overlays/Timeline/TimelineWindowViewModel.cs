using SWTORCombatParser.DataStructures;
using SWTORCombatParser.DataStructures.Timeline;
using SWTORCombatParser.Model.CombatParsing;
using SWTORCombatParser.Utilities;
using SWTORCombatParser.ViewModels.Combat_Monitoring;
using SWTORCombatParser.ViewModels.DataGrid;
using SWTORCombatParser.Views.DataGrid_Views;
using SWTORCombatParser.Views.Overlay.Timeline;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace SWTORCombatParser.ViewModels.Overlays.Timeline
{
    public class TimelineElement
    {
        public string BossName { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan TTK { get; set; }
        public bool IsLeaderboard { get; set; }
        public bool IsFreshKill { get; set; }
    }

    public class TimelineWindowViewModel : INotifyPropertyChanged
    {
        private object lockObj = new object();
        public event Action<TimeSpan> OnUpdateTimeline = delegate { };
        public event Action<TimeSpan> OnInit = delegate { };
        public event Action<string, string, string> AreaEntered = delegate { };

        public event Action<bool> OnLocking = delegate { };
        public event Action OnHiding = delegate { };
        public event Action OnShowing = delegate { };
        public event Action CloseRequested = delegate { };

        private InstanceInformation _instanceInfo;
        private bool _inBossInstance;
        private bool overlaysMoveable;
        private readonly DataGridViewModel _metricViewModel;

        public ObservableCollection<TimelineElement> AllTimelineElements { get; } = new ObservableCollection<TimelineElement>();
        public DataGridView MetricsView { get; set; }
        public TimelineWindow MainContent { get; set; }

        public TimeSpan CurrentTime { get; set; }
        public TimeSpan MaxDuration => _instanceInfo?.MaxDuration ?? TimeSpan.Zero;

        public bool InBossInstance
        {
            get => _inBossInstance;
            set
            {
                _inBossInstance = value;
                OnPropertyChanged();
                UpdateVisibility();
            }
        }

        public bool OverlaysMoveable
        {
            get => overlaysMoveable;
            set
            {
                overlaysMoveable = value;
                OnPropertyChanged();
            }
        }

        public TimelineWindowViewModel()
        {
            MainContent = new TimelineWindow(this);
            _metricViewModel = new DataGridViewModel();
            MetricsView = new DataGridView(_metricViewModel);
            OnPropertyChanged(nameof(MetricsView));

            _instanceInfo = new InstanceInformation()
            {
                MaxDuration = TimeSpan.Zero,
                PreviousBossKills = new List<BossKillInfo>(),
                CurrentBossKills = new List<BossKillInfo>()
            };
            EncounterMonitor.EncounterUpdated += UpdateEncounterLevelInfo;
        }

        private void UpdateEncounterLevelInfo(EncounterCombat obj)
        {
            Task.Run(() =>
            {
                var isActive = Application.Current.Dispatcher.Invoke(() => MainContent.IsVisible);
                if (!isActive || !TimelineOverlayManager.TimelineEnabled)
                    return;

                if (obj == null || obj.Combats.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() => _metricViewModel?.Reset());
                    return;
                }

                var overallCombat = obj.OverallCombat;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _metricViewModel?.UpdateCombat(overallCombat);
                });
            });
        }

        public void ConfigureTimeline(TimeSpan maxDuration, List<BossKillInfo> previousKills, string areaName, string difficulty, string playerCount)
        {
            lock (lockObj)
            {
                _instanceInfo.MaxDuration = maxDuration;
                _instanceInfo.PreviousBossKills = previousKills;
                UpdateBossKillElements();
                OnInit.Invoke(maxDuration);
                OnUpdateTimeline.Invoke(maxDuration);
                AreaEntered(areaName, difficulty, playerCount);
            }
        }

        public void UpdateBossKillElements()
        {
            AllTimelineElements.Clear();
            foreach (var boss in _instanceInfo.PreviousBossKills)
            {
                AllTimelineElements.Add(new TimelineElement
                {
                    BossName = boss.BossName,
                    StartTime = boss.StartTime,
                    TTK = boss.TTK,
                    IsLeaderboard = true
                });
            }

            foreach (var boss in _instanceInfo.CurrentBossKills)
            {
                AllTimelineElements.Add(new TimelineElement
                {
                    BossName = boss.BossName,
                    StartTime = boss.StartTime,
                    TTK = boss.TTK,
                    IsFreshKill = boss.IsKilled
                });
            }
        }

        public void SetClickThrough(bool canClickThrough)
        {
            OverlaysMoveable = !canClickThrough;
            OnLocking.Invoke(canClickThrough);
        }

        public void UpdateTimeline(TimeSpan currentTime)
        {
            lock (lockObj)
            {
                CurrentTime = currentTime;
                if (MaxDuration < currentTime)
                {
                    _instanceInfo.MaxDuration = currentTime;
                }
                OnUpdateTimeline.Invoke(currentTime);
                foreach (var boss in _instanceInfo.CurrentBossKills.Where(b => b.IsKilled == false))
                {
                    boss.EndTime = currentTime;
                }

                UpdateBossKillElements();
            }
        }

        public void Reset()
        {
            lock (lockObj)
            {
                _instanceInfo = new InstanceInformation()
                {
                    MaxDuration = TimeSpan.Zero,
                    PreviousBossKills = new List<BossKillInfo>(),
                    CurrentBossKills = new List<BossKillInfo>()
                };
                UpdateBossKillElements();
            }
        }

        public void RemoveInProgressBosses()
        {
            lock (lockObj)
            {
                _instanceInfo.CurrentBossKills.RemoveAll(b => !b.IsKilled);
                UpdateBossKillElements();
            }
        }

        public void RemoveBoss(string bossName)
        {
            lock (lockObj)
            {
                _instanceInfo.CurrentBossKills.RemoveAll(b => b.BossName == bossName && !b.IsKilled);
                UpdateBossKillElements();
            }
        }

        public void StartNewBoss(string bossName, TimeSpan startTime)
        {
            lock (lockObj)
            {
                _instanceInfo.CurrentBossKills.Add(new BossKillInfo
                {
                    BossName = bossName,
                    StartTime = startTime,
                    EndTime = startTime
                });
                UpdateBossKillElements();
            }
        }

        public void CombatEnded()
        {
            lock (lockObj)
            {
                RemoveInProgressBosses();
            }
        }

        public void BossKilled(string bossName, TimeSpan startTime, TimeSpan killTime)
        {
            lock (lockObj)
            {
                RemoveBoss(bossName);
                _instanceInfo.CurrentBossKills.Add(new BossKillInfo()
                {
                    BossName = bossName,
                    StartTime = startTime,
                    EndTime = killTime,
                    IsKilled = true
                });
                if (killTime > _instanceInfo.MaxDuration)
                {
                    _instanceInfo.MaxDuration = killTime;
                }
                UpdateBossKillElements();
            }
        }

        public void AddBossWipe(string bossName, TimeSpan startTime, TimeSpan killTime)
        {
            lock (lockObj)
            {
                RemoveBoss(bossName);
                _instanceInfo.CurrentBossKills.Add(new BossKillInfo()
                {
                    BossName = bossName,
                    StartTime = startTime,
                    EndTime = killTime,
                    IsKilled = false
                });
                if (killTime > _instanceInfo.MaxDuration)
                {
                    _instanceInfo.MaxDuration = killTime;
                }
                UpdateBossKillElements();
            }
        }

        public void UserDisabled()
        {
            TimelineOverlayManager.TimelineEnabled = false;
        }

        private void UpdateVisibility()
        {
            if (InBossInstance && TimelineOverlayManager.TimelineEnabled)
                OnShowing.Invoke();
            else
                OnHiding.Invoke();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
