using SWTORCombatParser.DataStructures;
using SWTORCombatParser.DataStructures.EncounterInfo;
using SWTORCombatParser.DataStructures.Timeline;
using SWTORCombatParser.Model.CloudRaiding;
using SWTORCombatParser.Model.CombatParsing;
using SWTORCombatParser.Model.LogParsing;
using SWTORCombatParser.Model.Overlays;
using SWTORCombatParser.Utilities;
using SWTORCombatParser.ViewModels.Combat_Monitoring;
using SWTORCombatParser.Views.Overlay.Timeline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SWTORCombatParser.ViewModels.Overlays.Timeline
{
    public static class TimelineOverlayManager
    {
        private static bool _unlocked;
        private static TimelineWindow _timelineWindow;
        private static TimelineWindowViewModel _timelineWindowViewModel;
        private static bool _inBossInstance = false;
        private static string _currentBossName = "";
        private static DateTime _lastEncounterStartTime;
        private static EncounterInfo _currentEncounter;
        private static bool _timelineEnabled;

        public static void Init()
        {
            _timelineWindowViewModel = new TimelineWindowViewModel();
            _timelineWindow = _timelineWindowViewModel.MainContent;

            CombatLogStateBuilder.AreaEntered += TryBuildTimeline;
            CombatIdentifier.CombatFinished += CombatFinished;
            CombatLogStreamer.HistoricalLogsFinished += HistoricalLogsParsed;
            CombatSelectionMonitor.CombatSelected += ShowTimelineNonLive;

            var defaults = DefaultGlobalOverlays.GetOverlayInfoForType("TimelineOverlay");
            TimelineEnabled = defaults.Acive;
            _timelineWindow.Top = defaults.Position.Y;
            _timelineWindow.Left = defaults.Position.X;
            _timelineWindow.Width = defaults.WidtHHeight.X;
            _timelineWindow.Height = defaults.WidtHHeight.Y;
        }

        private static void UserDisabled()
        {
            TimelineEnabled = false;
        }

        private static void HistoricalLogsParsed(DateTime arg1, bool arg2)
        {
            var lastEncounter = CombatLogStateBuilder.CurrentState.EncounterEnteredInfo.LastOrDefault();
            if (lastEncounter.Value != null && lastEncounter.Value.IsBossEncounter &&
                CombatMonitorViewModel.IsLiveParseActive() && DateTime.Now - lastEncounter.Key < TimeSpan.FromMinutes(90))
            {
                TryBuildTimeline(lastEncounter.Value);
            }
            else
            {
                HideTimelineOverlay();
            }
        }

        private static void CombatFinished(Combat obj)
        {
            if (!_inBossInstance || !obj.IsCombatWithBoss || !_currentEncounter.BossInfos.Any(bi => bi.EncounterName == obj.EncounterBossDifficultyParts.Item1) || _lastEncounterStartTime > obj.StartTime)
                return;

            if (!obj.WasBossKilled)
                RemoveBoss(obj.EncounterBossDifficultyParts.Item1);
            if (obj.WasBossKilled)
            {
                _timelineWindowViewModel.BossKilled(obj.EncounterBossDifficultyParts.Item1, (obj.StartTime - _lastEncounterStartTime), (obj.EndTime - _lastEncounterStartTime));
            }
        }

        public static bool TimelineEnabled
        {
            get => _timelineEnabled;
            set
            {
                _timelineEnabled = value;
                if (value)
                {
                    DefaultGlobalOverlays.SetActive("TimelineOverlay", true);
                    if (_unlocked)
                    {
                        _timelineWindow.Show();
                        _timelineWindowViewModel.SetClickThrough(false);
                    }
                    if (_inBossInstance)
                        _timelineWindow.Show();
                }
                else
                {
                    DefaultGlobalOverlays.SetActive("TimelineOverlay", false);
                    _timelineWindow.Hide();
                }
            }
        }

        public static async Task UploadBossKill(string encounterName, string flashpointOrRaidName, string difficulty, string playerCount, DateTime startTime, DateTime endTime)
        {
            var timeTrialInfo = new TimeTrialLeaderboardEntry()
            {
                BossFight = encounterName,
                Timestamp = endTime,
                StartSeconds = (int)startTime.Subtract(_lastEncounterStartTime).TotalSeconds,
                EndSeconds = (int)endTime.Subtract(_lastEncounterStartTime).TotalSeconds,
                PlayerName = CombatLogStateBuilder.CurrentState.LocalPlayer.Name,
                Encounter = flashpointOrRaidName,
                Difficulty = difficulty,
                PlayerCount = playerCount
            };
            await API_Connection.AddNewTimeTrialEntry(timeTrialInfo);
            BuildTimelineFromEncounter();
        }

        public static void RemoveBoss(string bossName)
        {
            if (_inBossInstance)
                _timelineWindowViewModel.RemoveBoss(bossName);
        }

        public static void StartBoss(string bossName)
        {
            if (_inBossInstance)
            {
                _timelineWindowViewModel.StartNewBoss(bossName, DateTime.Now - _lastEncounterStartTime);
                _currentBossName = bossName;
            }
        }

        private static void ShowTimelineNonLive(Combat selectedCombat)
        {
            if (CombatMonitorViewModel.IsLiveParseActive() || !TimelineEnabled)
                return;
            var encounter = selectedCombat.ParentEncounter;
            if (encounter.IsBossEncounter)
            {
                if (_currentEncounter != encounter)
                {
                    _timelineWindowViewModel.Reset();
                }

                _currentEncounter = encounter;
                BuildTimelineFromEncounter(false);
                if (selectedCombat.WasBossKilled)
                    _timelineWindowViewModel.BossKilled(selectedCombat.EncounterBossDifficultyParts.Item1,
                    (selectedCombat.StartTime - _lastEncounterStartTime),
                    (selectedCombat.EndTime - _lastEncounterStartTime));
                else
                    _timelineWindowViewModel.AddBossWipe(selectedCombat.EncounterBossDifficultyParts.Item1,
                        (selectedCombat.StartTime - _lastEncounterStartTime),
                        (selectedCombat.EndTime - _lastEncounterStartTime));
            }
            else
            {
                HideTimelineOverlay();
            }
        }

        private static void TryBuildTimeline(EncounterInfo obj)
        {
            if (obj.IsBossEncounter)
            {
                if (_currentEncounter != obj)
                {
                    _timelineWindowViewModel.Reset();
                }
                _currentEncounter = obj;
                BuildTimelineFromEncounter();
            }
            else
            {
                _inBossInstance = false;
                HideTimelineOverlay();
            }
        }

        private static void BuildTimelineFromEncounter(bool showLive = true)
        {
            _inBossInstance = true;
            _lastEncounterStartTime = CombatLogStateBuilder.CurrentState.EncounterEnteredInfo.FirstOrDefault(kvp => kvp.Value == _currentEncounter).Key;
            var timeTrialLeaderboardEntries = _currentEncounter.BossInfos.Select(async bi =>
            {
                var timeTrialInfoForBoss = await API_Connection.GetTimeTrialEntriesForBoss(bi.EncounterName, _currentEncounter.Name, _currentEncounter.Difficutly, _currentEncounter.NumberOfPlayer);
                int tenthPercentileObject = (int)Math.Ceiling(timeTrialInfoForBoss.Count * 0.1) - 1;
                tenthPercentileObject = Math.Max(0, tenthPercentileObject);

                return new BossKillInfo()
                {
                    BossName = bi.EncounterName,
                    StartTime = timeTrialInfoForBoss.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(timeTrialInfoForBoss.OrderBy(t => t.StartSeconds).ElementAt(tenthPercentileObject).StartSeconds),
                    EndTime = timeTrialInfoForBoss.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(timeTrialInfoForBoss.OrderBy(t => t.EndSeconds).ElementAt(tenthPercentileObject).EndSeconds),
                    IsKilled = true,
                };
            });
            Task.WhenAll(timeTrialLeaderboardEntries).ContinueWith((t) =>
            {
                var bossKillInfos = t.Result;
                var maxDuration = bossKillInfos.Max(b => b.EndTime);
                DisplayTimelineOverlay(maxDuration, bossKillInfos.ToList(), showLive);
            });
        }

        public static void DisplayTimelineOverlay(TimeSpan maxDuration, List<BossKillInfo> previousKills, bool showLive)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _timelineWindowViewModel.ConfigureTimeline(maxDuration, previousKills, _currentEncounter.Name, _currentEncounter.Difficutly, _currentEncounter.NumberOfPlayer);
                if (TimelineEnabled)
                    _timelineWindow.Show();

                if (showLive)
                    StartEncounterTask();
            });
        }

        public static void HideTimelineOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _timelineWindow.Hide();
                Debug.WriteLine("Timeline hidden!");
            });
        }

        public static void UnlockOverlay()
        {
            _unlocked = true;
            if (_timelineEnabled)
                _timelineWindow.Show();
            _timelineWindowViewModel.SetClickThrough(false);
        }

        public static void LockOverlay()
        {
            _unlocked = false;
            _timelineWindowViewModel.SetClickThrough(true);
            if (!_inBossInstance)
                _timelineWindow.Hide();
        }

        private static void StartEncounterTask()
        {
            Task.Run(() =>
            {
                while (_inBossInstance)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _timelineWindowViewModel.UpdateTimeline(DateTime.Now - _lastEncounterStartTime);
                    });
                    Task.Delay(1000).Wait();
                }
            });
        }
    }
}
