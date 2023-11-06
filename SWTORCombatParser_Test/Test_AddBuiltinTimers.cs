using MoreLinq;
using Newtonsoft.Json;
using NUnit.Framework;
using SWTORCombatParser.DataStructures;
using SWTORCombatParser.Model.Timers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SWTORCombatParser_Test
{
    public class Test_AddBuiltinTimers
    {
        private readonly int _currentRev = 8;
        [Test]
        public void AddKeetsuneTimers()
        {
            var allTimers = JsonConvert.DeserializeObject<List<DefaultTimersData>>(
                File.ReadAllText(Path.Combine(Environment.CurrentDirectory, @"keetsuneTimers.json")));
            var allIndividualTimers = allTimers.SelectMany(t => t.Timers);
            var enumerable = allIndividualTimers as Timer[] ?? allIndividualTimers.ToArray();
            enumerable.ForEach(t =>
            {
                t.TimerSource = t.TimerSource.Count(t => t == '|') > 1 ? t.TimerSource.Split('|')[0] + "|" + t.TimerSource.Split('|')[1] : t.TimerSource;
                t.TimerRev = _currentRev;
                t.IsUserAddedTimer = false;
            });
            var timersWithAudio = enumerable.Where(t => !string.IsNullOrEmpty(t.CustomAudioPath));
            foreach (var timer in timersWithAudio)
            {
                timer.CustomAudioPath = timer.CustomAudioPath.Split('\\').Last();
                timer.UseAudio = false;
            }

            foreach (var encounter in allTimers)
            {
                encounter.Timers.RemoveAll(timer =>
                    timer.Name == "Missle Salvo" || timer.Name == "Red Circles" || timer.Name == "Knock-back" ||
                    timer.Name == "Platform Drop" || (timer.TimerSource.Contains("IP-CPT") && timer.Name.Contains("Beam") || timer.Name.Contains("Floor")));
            }

            var encounters = allTimers.Where(v => v.IsBossSource).GroupBy(v => v.TimerSource.Split('|').First());
            var targetDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Builtin Timers");
            Directory.CreateDirectory(targetDirectory);
            foreach (var encounter in encounters)
            {
                Directory.CreateDirectory(Path.Combine(targetDirectory, encounter.Key));
                File.WriteAllText(Path.Combine(targetDirectory, encounter.Key, encounter.Key) + ".json", JsonConvert.SerializeObject(encounter.ToList()));
            }
        }
        [Test]
        public void MakeKeetsuneTimersUser()
        {
            var allTimers = JsonConvert.DeserializeObject<List<DefaultTimersData>>(
                File.ReadAllText(Path.Combine(Environment.CurrentDirectory, @"keetsuneTimers.json")));
            var allIndividualTimers = allTimers.SelectMany(t => t.Timers);
            var enumerable = allIndividualTimers as Timer[] ?? allIndividualTimers.ToArray();
            string[] missedSounds = // TODO
            {
                "ConeSwipe.mp3",
                "Induction.mp3",
                "Phase 2.mp3", // "Phase2.mp3" exists
                "Phase 3.mp3", // "Phase3.mp3" exists
                "Phase 4.mp3",
                "Phase 5.mp3",
                "Reposition.mp3",
                "Interrupt.mp3",
                "Explosion_.mp3", // "Explosion.mp3" exists
            };
            enumerable.ForEach(t =>
            {
                t.TimerSource = t.TimerSource.Count(t => t == '|') > 1
                    ? t.TimerSource.Split('|')[0] + "|" + t.TimerSource.Split('|')[1]
                    : t.TimerSource;
                t.IsUserAddedTimer = true;
                t.CustomAudioPath = t.CustomAudioPath is not null &&
                                    t.CustomAudioPath.Contains(@"C:\Users\kitsu\AppData\Local\StarParse\app\client\app\sounds")
                    ? Path.Combine(
                        Directory.GetCurrentDirectory(),
                        t.CustomAudioPath.Replace(@"C:\Users\kitsu\AppData\Local\StarParse\app\client\app\sounds", @"resources\Audio\TimerAudio")
                    )
                    : t.CustomAudioPath;
                if (t.CustomAudioPath is not null && !File.Exists(t.CustomAudioPath)
                    && !missedSounds.Contains(t.CustomAudioPath.Split(new[] { @"\" }, StringSplitOptions.None).Last())
                )
                {
                    Assert.Fail($"File {t.CustomAudioPath} do not exists");
                }
            });

            var targetDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Keetsune Modified");
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(Path.Combine(targetDirectory, "timers_info_v3.json"), JsonConvert.SerializeObject(allTimers));

        }
    }
}
