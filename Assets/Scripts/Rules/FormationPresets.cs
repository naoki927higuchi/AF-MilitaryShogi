using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MilitaryShogi.Rules
{
    /// <summary>One saved own-army placement: a name and the actual kind on each camp cell.</summary>
    public sealed class FormationPreset
    {
        public string Name;
        /// <summary>Kind per camp cell, indexed by depth * 8 + column (depth 0 = own back row); null = empty.</summary>
        public PieceType?[] Cells;
        public bool IsEmpty { get { return Cells == null; } }

        public Formation ToFormation(Side side)
        {
            var pieces = new Dictionary<int, PieceType>();
            for (int i = 0; i < Cells.Length; i++)
                if (Cells[i].HasValue) pieces[BoardGraph.CampCell(side, i % BoardGraph.Columns, i / BoardGraph.Columns)] = Cells[i].Value;
            var f = new Formation(side, pieces);
            PlacementRules.Validate(f);
            return f;
        }

        public static PieceType?[] CellsOf(Formation f)
        {
            var cells = new PieceType?[BoardGraph.Columns * BoardGraph.CampRows];
            for (int d = 0; d < BoardGraph.CampRows; d++)
                for (int x = 0; x < BoardGraph.Columns; x++)
                {
                    PieceType t;
                    if (f.TryGet(BoardGraph.CampCell(f.Side, x, d), out t)) cells[d * BoardGraph.Columns + x] = t;
                }
            return cells;
        }
    }

    /// <summary>
    /// Five own-army placement presets. The saved data is the placement itself (kind per cell in
    /// camp-relative coordinates, kinds written by name), not a formation seed, so presets survive
    /// changes to the random placement generator and to enum ordering.
    ///
    /// Text format (UTF-8):
    ///   AFMS-PRESETS 1
    ///   slot &lt;n&gt; &lt;url-escaped name&gt; &lt;32 comma-separated cells: kind name or "-"&gt;
    ///   slot &lt;n&gt; &lt;url-escaped name&gt; empty
    /// Every slot is validated on load (army composition + placement rules); invalid or unknown data
    /// is rejected slot by slot and reported, never applied.
    /// </summary>
    public sealed class FormationPresets
    {
        public const int SlotCount = 5;
        public const int FormatVersion = 1;
        private const string Header = "AFMS-PRESETS";
        private readonly FormationPreset[] slots = new FormationPreset[SlotCount];
        public readonly List<string> LoadErrors = new List<string>();

        public FormationPresets()
        {
            for (int i = 0; i < SlotCount; i++) slots[i] = new FormationPreset { Name = DefaultName(i) };
        }

        public static string DefaultName(int slot) { return "プリセット" + (slot + 1); }

        public FormationPreset Slot(int slot) { return slots[slot]; }

        /// <summary>Name to prefill in the save dialog: the slot's current name.</summary>
        public string NameForSave(int slot) { return slots[slot].Name; }

        public void Save(int slot, string name, Formation formation)
        {
            PlacementRules.Validate(formation);
            name = string.IsNullOrWhiteSpace(name) ? slots[slot].Name : name.Trim();
            if (name.Length > 40) name = name.Substring(0, 40);
            slots[slot] = new FormationPreset { Name = name, Cells = FormationPreset.CellsOf(formation) };
        }

        /// <summary>Formation stored in the slot, or null when empty. Always a legal placement.</summary>
        public Formation Load(int slot, Side side)
        {
            var p = slots[slot];
            return p.IsEmpty ? null : p.ToFormation(side);
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append(' ').Append(FormatVersion).Append('\n');
            for (int i = 0; i < SlotCount; i++)
            {
                var p = slots[i];
                sb.Append("slot ").Append(i + 1).Append(' ').Append(Uri.EscapeDataString(p.Name)).Append(' ');
                sb.Append(p.IsEmpty ? "empty" : string.Join(",", p.Cells.Select(c => c.HasValue ? c.Value.ToString() : "-")));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static FormationPresets Parse(string text)
        {
            var presets = new FormationPresets();
            if (string.IsNullOrEmpty(text)) return presets;
            var lines = text.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToArray();
            var head = lines.Length > 0 ? lines[0].Split(' ') : new string[0];
            int version;
            if (head.Length != 2 || head[0] != Header || !int.TryParse(head[1], out version))
            {
                presets.LoadErrors.Add("header missing: not a preset file");
                return presets;
            }
            if (version != FormatVersion)
            {
                presets.LoadErrors.Add("unsupported preset format version " + version);
                return presets;
            }
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(' ');
                int n;
                if (parts.Length != 4 || parts[0] != "slot" || !int.TryParse(parts[1], out n) || n < 1 || n > SlotCount)
                {
                    presets.LoadErrors.Add("malformed line ignored");
                    continue;
                }
                string name;
                try { name = Uri.UnescapeDataString(parts[2]); } catch (Exception) { name = DefaultName(n - 1); }
                if (string.IsNullOrWhiteSpace(name)) name = DefaultName(n - 1);
                if (parts[3] == "empty")
                {
                    presets.slots[n - 1] = new FormationPreset { Name = name };
                    continue;
                }
                var tokens = parts[3].Split(',');
                var cells = new PieceType?[BoardGraph.Columns * BoardGraph.CampRows];
                bool ok = tokens.Length == cells.Length;
                for (int i = 0; ok && i < tokens.Length; i++)
                {
                    PieceType t;
                    if (tokens[i] == "-") cells[i] = null;
                    else if (Enum.TryParse(tokens[i], false, out t) && Enum.IsDefined(typeof(PieceType), t) && tokens[i] == t.ToString()) cells[i] = t;
                    else ok = false;
                }
                var preset = new FormationPreset { Name = name, Cells = cells };
                if (ok)
                {
                    try { preset.ToFormation(Side.South); }
                    catch (ArgumentException e) { ok = false; presets.LoadErrors.Add("slot " + n + " rejected: " + e.Message); }
                }
                else presets.LoadErrors.Add("slot " + n + " rejected: unreadable cells");
                // A rejected slot keeps its name but no placement: nothing illegal is ever applied.
                presets.slots[n - 1] = ok ? preset : new FormationPreset { Name = name };
            }
            return presets;
        }
    }
}
