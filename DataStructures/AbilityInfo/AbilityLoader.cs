using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace SWTORCombatParser.DataStructures.AbilityInfo
{
    public class AbilityInfo
    {
        public string name { get; set; }
    }
    public static class AbilityLoader
    {
        public static Dictionary<double, AbilityInfo> AbsorbAbilities { get; set; } = 
            JsonConvert.DeserializeObject<Dictionary<double, AbilityInfo>>(File.ReadAllText(@"DataStructures/AbilityInfo/absorbs.json"));
        public static void SetAbsorbAbilities() { }
    }
}
