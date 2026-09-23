using System;
using System.Linq;
using MilitaryShogi.Rules;
using static MilitaryShogi.Tests.Program;

namespace MilitaryShogi.Tests
{
    /// <summary>1.2.0: own-army placement presets (format, names, validation).</summary>
    internal static class PresetTests
    {
        public static void SlotsNamesAndRoundTrip()
        {
            var p = new FormationPresets();
            for (int i = 0; i < FormationPresets.SlotCount; i++)
            {
                Check(p.NameForSave(i) == "プリセット" + (i + 1), "default name of slot " + (i + 1));
                Check(p.Load(i, Side.South) == null, "slots start empty");
            }
            var f1 = FormationGenerator.Generate(Side.South, FormationStyle.Trap, 11);
            var f2 = FormationGenerator.Generate(Side.South, FormationStyle.Mobile, 22);
            p.Save(0, p.NameForSave(0), f1);                 // saved without editing the name
            Check(p.NameForSave(0) == "プリセット1", "unedited save keeps the default name");
            p.Save(2, "地雷籠城", f2);
            Check(p.NameForSave(2) == "地雷籠城", "a changed name becomes the next default");
            p.Save(2, "  ", f1);
            Check(p.NameForSave(2) == "地雷籠城", "blank name keeps the current name");
            p.Save(2, "地雷籠城", f2);

            // Persistence: serialize, parse (= restart), same placements and names.
            var text = p.Serialize();
            var q = FormationPresets.Parse(text);
            Check(q.LoadErrors.Count == 0, "clean file loads without errors: " + string.Join(";", q.LoadErrors));
            Check(q.Load(0, Side.South).Signature() == f1.Signature(), "slot 1 placement reproduced");
            Check(q.Load(2, Side.South).Signature() == f2.Signature(), "slot 3 placement reproduced");
            Check(q.NameForSave(2) == "地雷籠城" && q.NameForSave(0) == "プリセット1", "names survive restart");
            Check(q.Load(1, Side.South) == null && q.Load(4, Side.South) == null, "empty slots stay empty");
            Check(q.Serialize() == text, "stable serialization");

            // The stored data is the placement itself: kinds by name per camp cell, not a seed.
            Check(text.Contains("General") && !text.Contains("seed"), "stores kinds, not seeds");
            // The same preset loads for the other side too (camp-relative coordinates).
            var north = q.Load(0, Side.North);
            PlacementRules.Validate(north);
            Check(north.Pieces.Count == 31, "camp-relative: usable for either side");
        }

        public static void RejectsInvalidData()
        {
            var good = new FormationPresets();
            good.Save(0, "ok", FormationGenerator.Generate(Side.South, FormationStyle.Balanced, 5));
            string text = good.Serialize();

            Check(FormationPresets.Parse("garbage").LoadErrors.Count > 0, "non-preset file rejected");
            var future = FormationPresets.Parse(text.Replace("AFMS-PRESETS 1", "AFMS-PRESETS 9"));
            Check(future.LoadErrors.Any(e => e.Contains("version")) && future.Load(0, Side.South) == null, "unknown format version rejected");

            var line = text.Split('\n')[1];
            var cells = line.Split(' ')[3].Split(',');
            // Mine moved onto a gateway arm end (front row x=0 is depth 3, index 24).
            int mine = Array.IndexOf(cells, "Mine");
            var illegal = (string[])cells.Clone();
            string armEnd = illegal[24];
            illegal[24] = "Mine"; illegal[mine] = armEnd;
            var bad = FormationPresets.Parse(text.Replace(string.Join(",", cells), string.Join(",", illegal)));
            Check(bad.Load(0, Side.South) == null && bad.LoadErrors.Any(e => e.Contains("slot 1")), "illegal placement rejected");

            var twoGenerals = (string[])cells.Clone();
            twoGenerals[Array.IndexOf(cells, "Major")] = "General";
            Check(FormationPresets.Parse(text.Replace(string.Join(",", cells), string.Join(",", twoGenerals))).Load(0, Side.South) == null, "wrong army rejected");
            var unknownKind = (string[])cells.Clone();
            unknownKind[0] = "Dragon";
            Check(FormationPresets.Parse(text.Replace(string.Join(",", cells), string.Join(",", unknownKind))).Load(0, Side.South) == null, "unknown kind rejected");
            var numeric = (string[])cells.Clone();
            numeric[Array.IndexOf(cells, "General")] = "0";
            Check(FormationPresets.Parse(text.Replace(string.Join(",", cells), string.Join(",", numeric))).Load(0, Side.South) == null, "numeric kind codes rejected");
            Check(FormationPresets.Parse(text.Replace(string.Join(",", cells), string.Join(",", cells.Take(20)))).Load(0, Side.South) == null, "short cell list rejected");
            bool threw = false;
            try { good.Save(1, "x", new Formation(Side.South, new System.Collections.Generic.Dictionary<int, PieceType>())); } catch (ArgumentException) { threw = true; }
            Check(threw, "an illegal formation cannot be saved");
        }
    }
}
