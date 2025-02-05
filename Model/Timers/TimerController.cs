﻿using Newtonsoft.Json;
using SWTORCombatParser.DataStructures;
using SWTORCombatParser.DataStructures.EncounterInfo;
using SWTORCombatParser.Model.CombatParsing;
using SWTORCombatParser.Model.LogParsing;
using SWTORCombatParser.Utilities;
using SWTORCombatParser.ViewModels.Timers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using Timer = SWTORCombatParser.DataStructures.Timer;

namespace SWTORCombatParser.Model.Timers;

public static class TimerController
{
    private static bool historicalParseFinished;
    private static DateTime _startTime;
    private static string _currentDiscipline;
    private static List<TimerInstance> _availableTimers = new List<TimerInstance>();
    private static List<TimerInstance> _filteredTimers = new List<TimerInstance>();
    private static List<TimerInstanceViewModel> _currentlyActiveTimers = new List<TimerInstanceViewModel>();
    private static bool _timersEnabled;
    private static EncounterInfo _currentEncounter;
    private static object _timerLock = new object();

    private static List<IDisposable> _showTimerSubs = new List<IDisposable>();
    private static List<IDisposable> _hideTimerSubs = new List<IDisposable>();
    private static List<IDisposable> _reorderSubs = new List<IDisposable>();
    public static event Action<TimerInstanceViewModel, Action<TimerInstanceViewModel>> TimerExpired = delegate { };
    public static event Action<TimerInstanceViewModel, Action<TimerInstanceViewModel>> TimerTriggered = delegate { };
    public static event Action ReorderRequested = delegate { };
    public static void Init()
    {
        CombatLogStreamer.HistoricalLogsFinished += EnableTimers;
        CombatLogStreamer.HistoricalLogsStarted += HistoricalStarted;
        CombatLogStreamer.CombatUpdated += CombatStateUpdated;
        CombatLogStreamer.NewLineStreamed += NewLogStreamed;
        CombatLogStateBuilder.AreaEntered += AreaChanged;
        DefaultTimersManager.Init();
        RefreshAvailableTimers();
    }

    private static void AreaChanged(EncounterInfo obj)
    {
        _currentEncounter = obj;
        FilterTimers();
    }
    private static void TrySetEncounter(EncounterInfo obj)
    {
        if (obj != _currentEncounter)
        {
            _currentEncounter = obj;
            FilterTimers();
        }
    }
    private static void TrySetBoss((string, string, string) bossinfo)
    {
        if (bossinfo != _currentBoss && !string.IsNullOrEmpty(bossinfo.Item1))
        {
            _currentBoss = bossinfo;
            FilterTimers();
        }
    }
    private static bool CheckEncounterAndBoss(TimerInstance t)
    {
        var timerEncounter = t.SourceTimer.SpecificEncounter;
        if (_currentEncounter.Name != timerEncounter)
            return false;
        var supportedDifficulties = new List<string>();
        if (t.SourceTimer.ActiveForStory)
            supportedDifficulties.Add("Story");
        if (t.SourceTimer.ActiveForVeteran)
            supportedDifficulties.Add("Veteran");
        if (t.SourceTimer.ActiveForMaster)
            supportedDifficulties.Add("Master");
        var timerBoss = t.SourceTimer.SpecificBoss;
        if (timerEncounter == "All")
            return true;
        if (string.IsNullOrEmpty(_currentBoss.Item1))
        {
            return false;
        }
        if ((supportedDifficulties.Contains(_currentEncounter.Difficutly)) && _currentBoss.Item1.ToLower() == timerBoss.ToLower())
            return true;
        return false;
    }
    private static void FilterTimers()
    {
        if (Monitor.TryEnter(_timerLock, 100))
        {
            try
            {
                _filteredTimers.Clear();
                foreach (var timer in _availableTimers)
                {
                    if (timer.SourceTimer.TrackOutsideOfCombat)
                        _filteredTimers.Add(timer);
                    if (timer.SourceTimer.TimerSource.Contains("|") && _currentEncounter != null)
                    {
                        if (CheckEncounterAndBoss(timer) && !_filteredTimers.Any(t => t.SourceTimer.Id == timer.SourceTimer.Id))
                        {
                            _filteredTimers.Add(timer);
                        }
                    }
                    else
                    {
                        if (!_filteredTimers.Any(t => t.SourceTimer.Id == timer.SourceTimer.Id))
                            _filteredTimers.Add(timer);
                    }
                }
            }
            finally { Monitor.Exit(_timerLock); }
        }
    }

    private static void HistoricalStarted()
    {
        historicalParseFinished = false;
    }

    private static List<string> _expirationTimers = new List<string>();
    public static void RefreshAvailableTimers()
    {
        _expirationTimers.Clear();
        _hideTimerSubs.ForEach(s => s.Dispose());
        _showTimerSubs.ForEach(s => s.Dispose());
        _reorderSubs.ForEach(s => s.Dispose());
        var allDefaults = DefaultTimersManager.GetAllDefaults();
        var timers = allDefaults.SelectMany(t => t.Timers);

        List<Timer> secondaryTimers = new List<Timer>();
        GetAllSubTimers(ref secondaryTimers, timers.ToList());
        var distinctTimers = secondaryTimers.DistinctBy(t => t.Id);


        _availableTimers = distinctTimers.Where(t => t.IsEnabled).Select(t => new TimerInstance(t.Copy())).ToList();
        foreach (var timerInstance in _availableTimers)
        {
            if (timerInstance.SourceTimer.IsSubTimer)
            {
                var parentTimer = _availableTimers.FirstOrDefault(t => t.SourceTimer.Id == timerInstance.ParentTimerId);
                if (parentTimer != null)
                {
                    timerInstance.ParentTimer = parentTimer;
                }
                else
                {
                    Logging.LogInfo("Parent timer not found for: " + JsonConvert.SerializeObject(timerInstance));
                }
            }
            if (!string.IsNullOrEmpty(timerInstance.ExperiationTimerId))
            {
                var trigger = _availableTimers.FirstOrDefault(t => t.SourceTimer.Id == timerInstance.ExperiationTimerId);
                if (trigger != null)
                {
                    //timerInstance.ExpirationTimer = trigger;
                    _expirationTimers.Add(trigger.SourceTimer.Id);
                }
                else
                {
                    Logging.LogInfo("Expiration timer not found for: " + JsonConvert.SerializeObject(timerInstance));
                }
            }
            if (!string.IsNullOrEmpty(timerInstance.CancellationTimerId))
            {
                var cancelTrigger = _availableTimers.FirstOrDefault(t => t.SourceTimer.Id == timerInstance.CancellationTimerId);
                if (cancelTrigger != null)
                {
                    timerInstance.CancelTimer = cancelTrigger;
                }
                else
                {
                    Logging.LogInfo("Cancel timer not found for: " + JsonConvert.SerializeObject(timerInstance));
                }
            }
        }

        _hideTimerSubs = _availableTimers.Select(t => Observable.FromEvent<Action<TimerInstanceViewModel, bool>, Tuple<TimerInstanceViewModel, bool>>(
            onNextHandler => (p1, p2) => onNextHandler(Tuple.Create(p1, p2)),
            manager => t.TimerOfTypeExpired += manager,
            manager => t.TimerOfTypeExpired -= manager
        ).Subscribe(args => OnTimerExpired(args.Item1, args.Item2))).ToList();
        _showTimerSubs = _availableTimers.Select(t =>
            Observable.FromEvent<TimerInstanceViewModel>(handler => t.NewTimerInstance += handler,
                handler => t.NewTimerInstance -= handler).Subscribe(AddTimerVisual)).ToList();
        _reorderSubs = _availableTimers.Select(t =>
            Observable.FromEvent(handler => t.ReorderRequested += handler,
                handler => t.ReorderRequested -= handler).Throttle(TimeSpan.FromMilliseconds(250)).Subscribe(_ => ReorderRequest())).ToList();
        FilterTimers();
    }

    private static void GetAllSubTimers(ref List<Timer> subTimers, List<Timer> baseTimers)
    {
        foreach (var timer in baseTimers)
        {
            foreach (var obj in FlattenNestedObjects(timer))
            {
                subTimers.Add(obj);
            }
        }
    }

    private static List<Timer> FlattenNestedObjects(Timer timer)
    {
        var result = new List<Timer> { timer };
        if (timer.Clause1 != null)
        {
            foreach (var nestedTimer in FlattenNestedObjects(timer.Clause1))
            {
                result.Add(nestedTimer);
            }
        }
        if (timer.Clause2 != null)
        {
            foreach (var nestedTimer in FlattenNestedObjects(timer.Clause2))
            {
                result.Add(nestedTimer);
            }
        }

        return result;
    }
    public static List<TimerInstanceViewModel> GetActiveTimers()
    {
        return _currentlyActiveTimers;
    }

    private static void ReorderRequest()
    {
        ReorderRequested();
    }
    private static void AddTimerVisual(TimerInstanceViewModel t)
    {
        if (t.SourceTimer.IsSubTimer)
            TimerAddedCallback(t);
        else
            TimerTriggered(t, TimerAddedCallback);
    }

    private static object _currentTimersModLock = new object();
    private static (string, string, string) _currentBoss;

    private static void TimerAddedCallback(TimerInstanceViewModel addedTimer)
    {
        lock (_timerLock)
        {
            if (_currentlyActiveTimers.Any(t => t.SourceTimer.Id == addedTimer.SourceTimer.Id))
                return;
            _currentlyActiveTimers.Add(addedTimer);
        }

    }
    private static void OnTimerExpired(TimerInstanceViewModel t, bool endedNatrually)
    {
        lock (_timerLock)
        {
            var id = t.SourceTimer.Id;
            if (_expirationTimers.Contains(id))
            {
                var timersThatCare = _filteredTimers.Where(t => t.ExperiationTimerId == id);
                var timerInstances = timersThatCare as TimerInstance[] ?? timersThatCare.ToArray();
                if (timerInstances.Any())
                {
                    foreach (var timer in timerInstances)
                    {
                        timer.ExpirationTimerEnded(t, endedNatrually);
                    }
                }
            }
        }
        TimerExpired(t, TimerRemovedCallback);
    }
    private static void TimerRemovedCallback(TimerInstanceViewModel removedTimer)
    {
        lock (_currentTimersModLock)
        {
            _currentlyActiveTimers.Remove(removedTimer);
        }
    }
    private static void EnableTimers(DateTime combatEndTime, bool localPlayerIdentified)
    {
        historicalParseFinished = true;
        _timersEnabled = true;
    }
    private static void NewLogStreamed(ParsedLogEntry log)
    {
        lock (_timerLock)
        {
            TrySetEncounter(CombatLogStateBuilder.CurrentState.GetEncounterActiveAtTime(log.TimeStamp));
            TrySetBoss(CombatIdentifier.GetCurrentBossInfo(new List<ParsedLogEntry>() { log }, _currentEncounter));
            _currentDiscipline ??= CombatLogStateBuilder.CurrentState.GetLocalPlayerClassAtTime(log.TimeStamp).Discipline;
            var currentTarget = CombatLogStateBuilder.CurrentState.GetLocalPlayerTargetAtTime(log.TimeStamp).Entity;
            var activeTimers = _currentlyActiveTimers.ToList();
            foreach (var timer in _filteredTimers)
            {
                if (!timer.TrackOutsideOfCombat && !CombatDetector.InCombat)
                    continue;
                timer.CheckForTrigger(log, _startTime, _currentDiscipline, activeTimers, _currentEncounter, _currentBoss, currentTarget);
            }
        }
    }

    private static void CombatStateUpdated(CombatStatusUpdate obj)
    {
        if (!historicalParseFinished)
            return;
        if (obj.Type == UpdateType.Start)
        {
            _currentBoss = ("", "", "");
            VariableManager.ResetVariables();
            _startTime = obj.CombatStartTime;
            UncancellBeforeCombat();
            _currentDiscipline = CombatLogStateBuilder.CurrentState.GetLocalPlayerClassAtTime(obj.CombatStartTime).Discipline;
        }
        if (obj.Type == UpdateType.Stop)
        {         
            CancelAfterCombat();
        }
    }
    private static void UncancellBeforeCombat()
    {
        foreach (var timer in _filteredTimers)
        {
            timer.UnCancel();
        }
    }
    private static void CancelAfterCombat()
    {
        foreach (var timer in _filteredTimers.Where(t => !t.SourceTimer.TrackOutsideOfCombat))
        {
            timer.Cancel();
        }
    }

    internal static void TryTriggerTimer(CombatModifier combatModifier)
    {
        var timer = _filteredTimers.First(t => t.SourceTimer.Effect == combatModifier.EffectId);
        timer.CreateTimerInstance(combatModifier.StartTime, combatModifier.Target.Name, combatModifier.Target.Id, combatModifier.ChargesAtTime.MaxBy(t => t.Key).Value);
    }
}