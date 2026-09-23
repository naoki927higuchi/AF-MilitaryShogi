using MilitaryShogi.Rules;
using UnityEngine;

namespace MilitaryShogi.Game
{
    /// <summary>
    /// 設定: only things one may change during a game. 演出（通常/簡易・速度）, 表示（敵駒の観測情報）,
    /// サウンド（効果音・音量）. Mode switching lives in the header, CPU strength/temperament in the
    /// pre-game panel. Every change is applied immediately and saved for the next start.
    /// </summary>
    public sealed class SettingsPanel
    {
        private readonly GameSettings s;
        public SettingsPanel(GameSettings settings) { s = settings; }

        public const float Width = 380f, Height = 400f;
        /// <summary>Button row height. Android uses larger rows for fingers (1.3.0).</summary>
        public float Row = 30f;
        /// <summary>Touch wording (「タップしたとき」) on Android.</summary>
        public bool Touch;
        /// <summary>Panel height for the current row height.</summary>
        public float PreferredHeight { get { return Height + (Row - 30f) * 6f + (Touch ? 40f : 0f); } }

        /// <summary>Draws inside a GUILayout area. Returns true when 閉じる was pressed.</summary>
        public bool Draw(UiKit ui)
        {
            bool changed = false, close = false;
            GUILayout.Label("設定", ui.Header);
            GUILayout.Space(4);
            GUILayout.Label("演出", ui.Label);
            GUILayout.Label("戦闘演出", ui.Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("通常", s.Effect == EffectMode.Normal ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.Effect = EffectMode.Normal; changed = true; }
            if (GUILayout.Button("簡易", s.Effect == EffectMode.Simple ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.Effect = EffectMode.Simple; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Label("演出速度", ui.Small);
            GUILayout.BeginHorizontal();
            foreach (float sp in new[] { 1f, 2f, 4f })
                if (GUILayout.Button("×" + sp.ToString("0"), s.EffectSpeed == sp ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.EffectSpeed = sp; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("表示", ui.Label);
            GUILayout.Label(Touch ? "敵駒の観測情報（敵駒をタップしたときの移動・戦闘の記録）" : "敵駒の観測情報（マウスを合わせたときの移動・戦闘の記録）", ui.Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ON", s.ObservationTooltip ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.ObservationTooltip = true; changed = true; }
            if (GUILayout.Button("OFF", !s.ObservationTooltip ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.ObservationTooltip = false; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("サウンド", ui.Label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("効果音", ui.Small, GUILayout.Width(60));
            if (GUILayout.Button("ON", s.SfxOn ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.SfxOn = true; changed = true; }
            if (GUILayout.Button("OFF", !s.SfxOn ? ui.Selected : ui.Button, GUILayout.Height(Row))) { s.SfxOn = false; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);   // same group, so a small gap rather than a separator (1.3.0)
            GUILayout.BeginHorizontal();
            GUILayout.Label("音量 " + s.SfxVolume + "%", ui.Small, GUILayout.Width(80));
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(s.SfxVolume, 0, 100, GUILayout.Height(Touch ? Row : 20)));
            if (v != s.SfxVolume) { s.SfxVolume = v; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("閉じる", ui.Button, GUILayout.Height(Row))) close = true;
            UiKit.SpotLast("settings.close");
            if (changed) UserData.SaveSettings(s);
            return close;
        }
    }

    /// <summary>
    /// 自軍配置プリセット（5枠）. Pick a slot, then 呼び出し or 保存. Saving shows one name field
    /// prefilled with the slot's current name (初回は「プリセットn」), so it can be saved unedited.
    /// </summary>
    public sealed class PresetPanel
    {
        private readonly GameController game;
        public int SelectedSlot;
        public bool Saving;
        public string NameDraft = "";
        public string Message = "";
        /// <summary>Button row height (larger on Android).</summary>
        public float Row = 28f;

        public PresetPanel(GameController game) { this.game = game; }

        public void BeginSave()
        {
            Saving = true;
            NameDraft = game.Presets.NameForSave(SelectedSlot);
        }

        public void CommitSave()
        {
            game.SavePreset(SelectedSlot, NameDraft);
            Saving = false;
            Message = "「" + game.Presets.NameForSave(SelectedSlot) + "」に保存しました";
        }

        public void Load()
        {
            Message = game.LoadPreset(SelectedSlot) ? "「" + game.Presets.NameForSave(SelectedSlot) + "」を呼び出しました" : "このプリセットは空です";
        }

        public void Draw(UiKit ui, GUIStyle textField)
        {
            GUILayout.Label("配置プリセット", ui.Small);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < FormationPresets.SlotCount; i++)
            {
                bool empty = game.Presets.Slot(i).IsEmpty;
                if (GUILayout.Button((i + 1) + (empty ? "" : "●"), i == SelectedSlot ? ui.Selected : ui.Button, GUILayout.Height(Row)) && !Saving) { SelectedSlot = i; Message = ""; }
                UiKit.SpotLast("preset.slot" + (i + 1));
            }
            GUILayout.EndHorizontal();
            var slot = game.Presets.Slot(SelectedSlot);
            GUILayout.Label(slot.Name + (slot.IsEmpty ? "（空き）" : ""), ui.Small);
            if (Saving)
            {
                GUILayout.Label("名前（そのままでも保存できます）", ui.Small);
                GUI.SetNextControlName("PresetName");
                NameDraft = GUILayout.TextField(NameDraft ?? "", 40, textField, GUILayout.Height(Row));
                UiKit.SpotLast("preset.name");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("保存", ui.Button, GUILayout.Height(Row))) CommitSave();
                UiKit.SpotLast("preset.commit");
                if (GUILayout.Button("やめる", ui.Button, GUILayout.Height(Row))) Saving = false;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = !slot.IsEmpty;
                if (GUILayout.Button("呼び出し", ui.Button, GUILayout.Height(Row))) Load();
                UiKit.SpotLast("preset.load");
                GUI.enabled = true;
                if (GUILayout.Button("現在の配置を保存…", ui.Button, GUILayout.Height(Row))) BeginSave();
                UiKit.SpotLast("preset.save");
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(Message)) GUILayout.Label(Message, ui.Small);
        }
    }
}
