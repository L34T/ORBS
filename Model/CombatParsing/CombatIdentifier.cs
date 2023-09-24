﻿//using MoreLinq;

using SWTORCombatParser.DataStructures;
using SWTORCombatParser.DataStructures.EncounterInfo;
using SWTORCombatParser.Model.CloudRaiding;
using SWTORCombatParser.Model.LogParsing;
using SWTORCombatParser.ViewModels.Timers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SWTORCombatParser.Model.CombatParsing
{
    public static class CombatIdentifier
    {
        public static event Action<Combat> NewCombatAvailable = delegate { };
        public static event Action NewCombatStarted = delegate { };
        private static object leaderboardLock = new object();

        private static bool _leaderboardsActive;
        public static Combat CurrentCombat { get; set; }
        public static void FinalizeOverlays(Combat combat)
        {
            _leaderboardsActive = false;
            UpdateOverlays(combat);
        }
        public static void UpdateOverlays(Combat combat)
        {
            if (combat.IsCombatWithBoss)
            {
                Task.Run(() =>
                {
                    if (!_leaderboardsActive)
                    {
                        Leaderboards.Reset();
                        Leaderboards.StartGetTopLeaderboardEntries(combat);
                        _leaderboardsActive = true;
                    }

                    Leaderboards.StartGetPlayerLeaderboardStandings(combat);
                });
            }
            FireEvent(combat);
        }
        public static void FinalizeOverlay(Combat combat)
        {
            if (combat.IsCombatWithBoss)
            {
                Task.Run(() =>
                {
                    Leaderboards.Reset();
                    Leaderboards.StartGetTopLeaderboardEntries(combat);
                    Leaderboards.StartGetPlayerLeaderboardStandings(combat);
                });
            }
            FireEvent(combat);
        }
        public static void FireEvent(Combat combat)
        {
            CurrentCombat = combat;
            NewCombatAvailable(combat);
        }
        public static void NotifyNewCombatStarted()
        {
            _leaderboardsActive = false;
            NewCombatStarted();
        }

        public static Combat GenerateNewCombatFromLogs(List<ParsedLogEntry> ongoingLogs, bool isRealtime = false, bool quietOverlays = false, bool combatEndUpdate = false)
        {

            var state = CombatLogStateBuilder.CurrentState;
            var orderedLogs = ongoingLogs.OrderBy(t => t.TimeStamp);
            var encounter = GetEncounterInfo(orderedLogs.First().TimeStamp);
            var currentPariticpants = orderedLogs.Where(l => l.Source.IsCharacter || l.Source.IsCompanion).Where(l => l.Effect.EffectType != EffectType.TargetChanged).Select(p => p.Source).Distinct().ToList();

            currentPariticpants.AddRange(orderedLogs.Where(l => l.Target.IsCharacter || l.Target.IsCompanion).Where(l => l.Effect.EffectType != EffectType.TargetChanged).Select(p => p.Target).Distinct().ToList());
            var participants = currentPariticpants.GroupBy(p => p.Id).Select(x => x.FirstOrDefault()).ToList();
            var participantInfos = orderedLogs.Select(p => p.SourceInfo).Distinct().ToList();
            var classes = participantInfos.GroupBy(p => p.Entity.Id).Select(x => x.FirstOrDefault()).ToDictionary(k => k.Entity, k => state.GetCharacterClassAtTime(k.Entity, orderedLogs.First().TimeStamp));

            var orderedLogsList = orderedLogs.ToList();
            var targets = GetTargets(orderedLogsList);
            var allEntities = new List<Entity>().Concat(targets).Concat(currentPariticpants).Distinct().ToList();

            // Create a HashSet of distinct LogIds from allEntities for faster lookups
            HashSet<long> distinctLogIds = new HashSet<long>(allEntities.Select(e => e.LogId));

            // Create the dictionary
            Dictionary<Entity, List<ParsedLogEntry>> entityLogs = allEntities
                .ToDictionary(
                    p => p,
                    p => ongoingLogs.AsParallel().WithDegreeOfParallelism(8)
                        .Where(l => distinctLogIds.Contains(l.Target.LogId) || distinctLogIds.Contains(l.Source.LogId)).OrderBy(t => t.TimeStamp)
                        .ToList()
                );

            var newCombat = new Combat()
            {
                CharacterParticipants = participants,
                CharacterClases = classes,
                StartTime = orderedLogs.First().TimeStamp,
                EndTime = orderedLogs.Last().TimeStamp,
                Targets = targets,
                AllLogs = orderedLogsList,
                LogsInvolvingEntity = entityLogs
            };
            if (encounter != null && encounter.BossInfos != null)
            {
                newCombat.ParentEncounter = encounter;
                newCombat.EncounterBossDifficultyParts = GetCurrentBossInfo(ongoingLogs, encounter);
                newCombat.RequiredDeadTargetsForKill = GetTargetsRequiredForKill(ongoingLogs, encounter);
            }
            if (newCombat.IsCombatWithBoss)
            {
                var parts = newCombat.EncounterBossDifficultyParts;
                if (isRealtime)
                    EncounterTimerTrigger.FireEncounterDetected(newCombat.ParentEncounter.Name, parts.Item1, newCombat.ParentEncounter.Difficutly);
            }
            if (newCombat.Targets.Any(t => t.LogId == 2857785339412480))
            {
                newCombat.ParentEncounter = new EncounterInfo() { Name = "Parsing", LogName = "Parsing", Difficutly = "Parsing", NumberOfPlayer = "1", EncounterType = EncounterType.Parsing, BossIds = new Dictionary<string, Dictionary<string, List<long>>>() { { "Training Dummy", new Dictionary<string, List<long>>() { { "Parsing 1", new List<long> { 2857785339412480 } } } } } };
                newCombat.EncounterBossDifficultyParts = GetCurrentBossInfo(ongoingLogs, encounter);
                newCombat.RequiredDeadTargetsForKill = new List<string> { "2857785339412480" };
            }
            CombatMetaDataParse.PopulateMetaData(newCombat);
            var absorbLogs = newCombat.IncomingDamageMitigatedLogs.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Where(l => l.Value.Modifier.ValueType == DamageType.absorbed).ToList());
            AddSheildingToLogs.AddShieldLogsByTarget(absorbLogs, newCombat);
            AddTankCooldown.AddDamageSavedDuringCooldown(newCombat);
            if (!quietOverlays)
                FireEvent(newCombat);
            return newCombat;
        }

        private static List<Entity> GetTargets(List<ParsedLogEntry> logs)
        {
            var combatStart = logs.First().TimeStamp;
            HashSet<Entity> targets = new HashSet<Entity>();

            foreach (var log in logs)
            {
                if (log.Effect.EffectType != EffectType.TargetChanged && log.Effect.EffectId == _7_0LogParsing._damageEffectId)
                {
                    if (log.Target != null && (!log.Target.IsCharacter || CombatLogStateBuilder.CurrentState.IsPvpOpponentAtTime(log.Target, combatStart)) && !log.Target.IsCompanion && log.Target.Name != null)
                    {
                        targets.Add(log.Target);
                    }

                    if (log.Source != null && (!log.Source.IsCharacter || CombatLogStateBuilder.CurrentState.IsPvpOpponentAtTime(log.Source, combatStart)) && !log.Source.IsCompanion && log.Source.Name != null)
                    {
                        targets.Add(log.Source);
                    }
                }
            }

            return targets.ToList();
        }
        private static EncounterInfo GetEncounterInfo(DateTime combatStartTime)
        {
            return CombatLogStateBuilder.CurrentState.GetEncounterActiveAtTime(combatStartTime);
        }
        public static (string, string, string) GetCurrentBossInfo(List<ParsedLogEntry> logs, EncounterInfo currentEncounter)
        {
            if (currentEncounter == null)
                return ("", "", "");

            var validLogs = logs.Where(l => !(l.Effect.EffectType == EffectType.TargetChanged && l.Source.IsCharacter) && !string.IsNullOrEmpty(l.Target.Name)).ToList();
            if (currentEncounter.Name.Contains("Open World"))
            {
                if (validLogs.Select(l => l.Target).DistinctBy(t => t.Id).Any(t => t.LogId == 2857785339412480))
                {
                    var dummyTarget = validLogs.Select(l => l.TargetInfo).First(t => t.Entity.LogId == 2857785339412480);
                    var dummyMaxHP = dummyTarget.MaxHP;
                    currentEncounter.Difficutly = dummyMaxHP.ToString();
                    return (dummyTarget.Entity.Name, dummyMaxHP + "HP", "");
                }
                else
                {
                    return ("", "", "");
                }
            }
            var bossesDetected = GetCurrentBossNames(validLogs, currentEncounter);
            if (!bossesDetected.Any())
                return ("", "", "");
            var boss = currentEncounter.BossInfos.FirstOrDefault(b => bossesDetected.All(t => b.TargetIds.Contains(t)));
            if (boss != null)
            {
                return (boss.EncounterName, currentEncounter.NumberOfPlayer.Replace("Player", "").Trim(), currentEncounter.Difficutly);
            }

            return ("", "", "");
        }
        private static List<string> GetCurrentBossNames(List<ParsedLogEntry> logs, EncounterInfo currentEncounter)
        {
            if (currentEncounter == null || currentEncounter.IsOpenWorld)
                return new List<string>();

            var bossIds = new HashSet<string>(currentEncounter.BossInfos.SelectMany(b => b.TargetIds));

            var bossNamesFound = new List<string>();
            foreach (var log in logs)
            {
                if (bossIds.Contains(log.Source.LogId.ToString()))
                {
                    log.Source.IsBoss = true;
                    bossNamesFound.Add(log.Source.LogId.ToString());
                }
                if (bossIds.Contains(log.Target.LogId.ToString()))
                {
                    log.Target.IsBoss = true;
                    bossNamesFound.Add(log.Target.LogId.ToString());
                }
            }

            return bossNamesFound.Distinct().ToList();
        }
        private static List<string> GetTargetsRequiredForKill(List<ParsedLogEntry> logs, EncounterInfo currentEncounter)
        {
            if (currentEncounter == null || currentEncounter.Name.Contains("Open World"))
                return new List<string>();

            var bossIds = new HashSet<string>(currentEncounter.BossInfos.SelectMany(b => b.TargetIds));

            var bossNamesFound = new List<string>();
            foreach (var log in logs)
            {
                if (bossIds.Contains(log.Source.LogId.ToString()))
                {
                    log.Source.IsBoss = true;
                    bossNamesFound.Add(log.Source.LogId.ToString());
                }
                if (bossIds.Contains(log.Target.LogId.ToString()))
                {
                    log.Target.IsBoss = true;
                    bossNamesFound.Add(log.Target.LogId.ToString());
                }
            }

            return bossNamesFound.Distinct().ToList();
        }

    }
}
