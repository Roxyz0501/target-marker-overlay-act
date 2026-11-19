using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Serialization;

namespace TargetMarkerOverlay
{
    public enum MarkerGroup { Attack, Bind, Stop, Shape, Unknown }
    public enum RoleGroup { Tank, Healer, Melee, Ranged, Caster, Other }
    public enum SortMode { MarkerFirst, RoleFirst, JobFirst }

    [Serializable]
    public sealed class PriorityEntry
    {
        [XmlAttribute] public string Key { get; set; }
        [XmlAttribute] public int Value { get; set; }
        public PriorityEntry() { }
        public PriorityEntry(string key, int value) { Key = key; Value = value; }
    }

    [Serializable]
    public sealed class PluginSettings
    {
        public string Language { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public string HostedLanguage { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public string EffectiveLanguage => Localization.Normalize(HostedLanguage ?? Language);
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string SkippedUpdateVersion { get; set; }
        public string LastUpdateCheckUtc { get; set; }
        public bool OverlayEnabled { get; set; } = true;
        public bool HideWhenEmpty { get; set; } = true;
        public bool ShowCharacterName { get; set; } = true;
        public bool AnonymousMode { get; set; }
        public bool Locked { get; set; }
        public bool EchoToggleEnabled { get; set; }
        public string EchoToggleText { get; set; } = "TargetMarker";
        public int OpacityPercent { get; set; } = 88;
        public int BackgroundOpacityPercent { get; set; } = 100;
        public int Left { get; set; } = 120;
        public int Top { get; set; } = 120;
        public int Width { get; set; } = 360;
        public int Height { get; set; } = 300;
        public SortMode SortMode { get; set; } = SortMode.MarkerFirst;
        public List<PriorityEntry> MarkerPriorities { get; set; } = DefaultMarkerPriorities();
        public List<PriorityEntry> RolePriorities { get; set; } = DefaultRolePriorities();
        public List<PriorityEntry> JobPriorities { get; set; } = DefaultJobPriorities();

        public int Priority(List<PriorityEntry> entries, string key, int fallback)
        {
            var item = entries == null ? null : entries.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            return item == null ? fallback : item.Value;
        }

        public void Normalize()
        {
            if (!string.IsNullOrWhiteSpace(Language)) Language = Localization.Normalize(Language);
            OpacityPercent = Math.Max(20, Math.Min(100, OpacityPercent));
            BackgroundOpacityPercent = Math.Max(0, Math.Min(100, BackgroundOpacityPercent));
            Width = Math.Max(ShowCharacterName ? 230 : 108, Width);
            Height = Math.Max(90, Height);
            if (MarkerPriorities == null || MarkerPriorities.Count == 0) MarkerPriorities = DefaultMarkerPriorities();
            if (RolePriorities == null || RolePriorities.Count == 0) RolePriorities = DefaultRolePriorities();
            if (JobPriorities == null || JobPriorities.Count == 0) JobPriorities = DefaultJobPriorities();
        }

        public static List<PriorityEntry> DefaultMarkerPriorities() => new List<PriorityEntry>
        {
            new PriorityEntry("Attack", 100), new PriorityEntry("Bind", 200),
            new PriorityEntry("Stop", 300), new PriorityEntry("Shape", 400),
        };

        public static List<PriorityEntry> DefaultRolePriorities() => new List<PriorityEntry>
        {
            new PriorityEntry("Tank", 100), new PriorityEntry("Healer", 200),
            new PriorityEntry("Melee", 300), new PriorityEntry("Ranged", 400),
            new PriorityEntry("Caster", 500), new PriorityEntry("Other", 900),
        };

        public static List<PriorityEntry> DefaultJobPriorities()
        {
            return JobCatalog.All.Select((x, i) => new PriorityEntry(x.Abbreviation, (i + 1) * 10)).ToList();
        }
    }

    public sealed class MarkerInfo
    {
        public int Code { get; set; }
        public MarkerGroup Group { get; set; }
        public int Number { get; set; }
        public string Label { get; set; }
    }

    public static class MarkerCatalog
    {
        public static MarkerInfo Get(int code)
        {
            // 6.4で追加されたAttack 6-8は、既存コードをずらさず14-16に追加された。
            if (code >= 0 && code <= 4) return New(code, MarkerGroup.Attack, code + 1, "ATTACK " + (code + 1));
            if (code >= 5 && code <= 7) return New(code, MarkerGroup.Bind, code - 4, "BIND " + (code - 4));
            if (code >= 8 && code <= 9) return New(code, MarkerGroup.Stop, code - 7, "STOP " + (code - 7));
            if (code == 10) return New(code, MarkerGroup.Shape, 1, "SQUARE");
            if (code == 11) return New(code, MarkerGroup.Shape, 2, "CIRCLE");
            if (code == 12) return New(code, MarkerGroup.Shape, 3, "PLUS");
            if (code == 13) return New(code, MarkerGroup.Shape, 4, "TRIANGLE");
            if (code >= 14 && code <= 16) return New(code, MarkerGroup.Attack, code - 8, "ATTACK " + (code - 8));
            return New(code, MarkerGroup.Unknown, code, "MARK " + code);
        }

        private static MarkerInfo New(int code, MarkerGroup group, int number, string label) =>
            new MarkerInfo { Code = code, Group = group, Number = number, Label = label };
    }

    public sealed class JobInfo
    {
        public int Id { get; set; }
        public string Abbreviation { get; set; }
        public string AssetName { get; set; }
        public RoleGroup Role { get; set; }
        public Color RoleColor { get; set; }
    }

    public static class JobCatalog
    {
        private static readonly Color Tank = Color.FromArgb(78, 132, 210);
        private static readonly Color Healer = Color.FromArgb(91, 178, 112);
        private static readonly Color Dps = Color.FromArgb(206, 91, 91);
        private static readonly Color Other = Color.FromArgb(145, 145, 160);

        public static readonly List<JobInfo> All = new List<JobInfo>
        {
            J(19,"PLD","paladin",RoleGroup.Tank,Tank), J(21,"WAR","warrior",RoleGroup.Tank,Tank),
            J(32,"DRK","darkknight",RoleGroup.Tank,Tank), J(37,"GNB","gunbreaker",RoleGroup.Tank,Tank),
            J(24,"WHM","whitemage",RoleGroup.Healer,Healer), J(28,"SCH","scholar",RoleGroup.Healer,Healer),
            J(33,"AST","astrologian",RoleGroup.Healer,Healer), J(40,"SGE","sage",RoleGroup.Healer,Healer),
            J(20,"MNK","monk",RoleGroup.Melee,Dps), J(22,"DRG","dragoon",RoleGroup.Melee,Dps),
            J(30,"NIN","ninja",RoleGroup.Melee,Dps), J(34,"SAM","samurai",RoleGroup.Melee,Dps),
            J(39,"RPR","reaper",RoleGroup.Melee,Dps), J(41,"VPR","viper",RoleGroup.Melee,Dps),
            J(23,"BRD","bard",RoleGroup.Ranged,Dps), J(31,"MCH","machinist",RoleGroup.Ranged,Dps),
            J(38,"DNC","dancer",RoleGroup.Ranged,Dps), J(25,"BLM","blackmage",RoleGroup.Caster,Dps),
            J(27,"SMN","summoner",RoleGroup.Caster,Dps), J(35,"RDM","redmage",RoleGroup.Caster,Dps),
            J(42,"PCT","pictomancer",RoleGroup.Caster,Dps), J(36,"BLU","bluemage",RoleGroup.Caster,Dps),
        };

        private static JobInfo J(int id, string abbr, string asset, RoleGroup role, Color color) =>
            new JobInfo { Id = id, Abbreviation = abbr, AssetName = asset, Role = role, RoleColor = color };

        public static JobInfo Get(int id)
        {
            var job = All.FirstOrDefault(x => x.Id == id);
            return job ?? new JobInfo { Id = id, Abbreviation = id > 0 ? "J" + id : "?", Role = RoleGroup.Other, RoleColor = Other };
        }
    }

    public sealed class CombatantInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int JobId { get; set; }
    }

    public sealed class MarkerAssignment
    {
        public int MarkerCode { get; set; }
        public uint TargetId { get; set; }
        public string TargetName { get; set; }
        public int JobId { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
