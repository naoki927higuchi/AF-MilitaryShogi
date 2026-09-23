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

        public const float Width = 380f, Height = 392f;

        /// <summary>Draws inside a GUILayout area. Returns true when 閉じる was pressed.</summary>
        public bool Draw(UiKit ui)
        {
            bool changed = false, close = false;
            GUILayout.Label("設定", ui.Header);
            GUILayout.Space(4);
            GUILayout.Label("演出", ui.Label);
            GUILayout.Label("戦闘演出", ui.Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("通常", s.Effect == EffectMode.Normal ? ui.Selected : ui.Button, GUILayout.Height(30))) { s.Effect = EffectMode.Normal; changed = true; }
            if (GUILayout.Button("簡易", s.Effect == EffectMode.Simple ? ui.Selected : ui.Button, GUILayout.Height(30))) { s.Effect = EffectMode.Simple; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Label("演出速度", ui.Small);
            GUILayout.BeginHorizontal();
            foreach (float sp in new[] { 1f, 2f, 4f })
                if (GUILayout.Button("×" + sp.ToString("0"), s.EffectSpeed == sp ? ui.Selected : ui.Button, GUILayout.Height(30))) { s.EffectSpeed = sp; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("表示", ui.Label);
            GUILayout.Label("敵駒の観測情報（マウスを合わせたときの移動・戦闘の記録）", ui.Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ON", s.ObservationTooltip ? ui.Selected : ui.Button, GUILayout.Height(30))) { s.ObservationTooltip = true; changed = true; }
            if (GUILayout.Button("OFF", !s.ObservationTooltip ? ui.Selected : ui.Button, GUILayout.Height(30))) { s.ObservationTooltip = false; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("サウンド", ui.Label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("効果音", ui.Small, GUILayout.Width(60));
            if (GUILayout.Button("ON", s.SfxOn ? ui.Selected : ui.Button, GUILayout.Height(28))) { s.SfxOn = true; changed = true; }
            if (GUILayout.Button("OFF", !s.SfxOn ? ui.Selected : ui.Button, GUILayout.Height(28))) { s.SfxOn = false; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("音量 " + s.SfxVolume + "%", ui.Small, GUILayout.Width(80));
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(s.SfxVolume, 0, 100, GUILayout.Height(20)));
            if (v != s.SfxVolume) { s.SfxVolume = v; changed = true; }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("閉じる", ui.Button, GUILayout.Height(30))) close = true;
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
                if (GUILayout.Button((i + 1) + (empty ? "" : "●"), i == SelectedSlot ? ui.Selected : ui.Button, GUILayout.Height(28)) && !Saving) { SelectedSlot = i; Message = ""; }
            }
            GUILayout.EndHorizontal();
            var slot = game.Presets.Slot(SelectedSlot);
            GUILayout.Label(slot.Name + (slot.IsEmpty ? "（空き）" : ""), ui.Small);
            if (Saving)
            {
                GUILayout.Label("名前（そのままでも保存できます）", ui.Small);
                GUI.SetNextControlName("PresetName");
                NameDraft = GUILayout.TextField(NameDraft ?? "", 40, textField, GUILayout.Height(26));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("保存", ui.Button, GUILayout.Height(28))) CommitSave();
                if (GUILayout.Button("やめる", ui.Button, GUILayout.Height(28))) Saving = false;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = !slot.IsEmpty;
                if (GUILayout.Button("呼び出し", ui.Button, GUILayout.Height(28))) Load();
                GUI.enabled = true;
                if (GUILayout.Button("現在の配置を保存…", ui.Button, GUILayout.Height(28))) BeginSave();
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(Message)) GUILayout.Label(Message, ui.Small);
        }
    }
}
