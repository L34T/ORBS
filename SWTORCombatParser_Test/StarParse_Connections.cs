using System;
using NUnit.Framework;
using SWTORCombatParser.Model.Timers;
using System.IO;

namespace SWTORCombatParser_Test
{
    [TestFixture]
    public class StarParse_Connections
    {
        [Test]
        public void CheckTimerConversion()
        {
            var timers = ImportSPTimers.ConvertXML(File.ReadAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"StarParse\app\client\app\starparse-timers.xml")));
            DefaultTimersManager.AddTimersForSource(timers, "StarParse Import");
            if (timers.Count > 0 && timers[0].Name.Length > 4 && timers[0].Ability.Length > 10)
            {
                Assert.Pass();
            } else {
                Assert.Fail();
            }
        }
    }
}