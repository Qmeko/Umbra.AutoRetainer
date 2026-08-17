using Dalamud.Plugin.Services;
using Umbra.Common;
using Umbra.Plugin.AutoRetainer.Services;
using Umbra.Widgets;
using Una.Drawing;

namespace Umbra.Plugin.AutoRetainer.Widgets;

[InteropToolbarWidget(
    "AutoRetainerStatus",
    "AutoRetainer",
    "AutoRetainer のリテイナー／潜水艦の回収数とベンチャー中の数を表示します。クリックで /ays を開きます。",
    "AutoRetainer",
    ["autoretainer", "retainer", "submarine", "venture", "ays"]
)]
public sealed class AutoRetainerStatusWidget(
    WidgetInfo info,
    string? guid = null,
    Dictionary<string, object>? configValues = null
) : StandardToolbarWidget(info, guid, configValues)
{
    protected override StandardWidgetFeatures Features => StandardWidgetFeatures.Text;

    private ICommandManager CommandManager { get; } = Framework.Service<ICommandManager>();
    private AutoRetainerStatusReader? Reader { get; set; }

    private readonly Node _retainerNode = new();
    private readonly Node _spacerNode = new() { NodeValue = "　" };
    private readonly Node _subNode = new();

    protected override IEnumerable<IWidgetConfigVariable> GetConfigVariables()
    {
        List<IWidgetConfigVariable> characterVariables = [];

        try
        {
            var characters = ArRoster.Load().Characters;
            var options = new Dictionary<string, string>
            {
                [""] = "（キャラクターを選ぶ）"
            };

            foreach (var character in characters)
                options[character.Cid.ToString("X16")] = character.DisplayName;

            characterVariables.Add(new SelectWidgetConfigVariable(
                "SelectedCharacter",
                "キャラクター",
                "設定するキャラクターを選びます。選ぶまでリテイナー一覧は出ません。",
                "",
                options)
            {
                Category = "キャラクター"
            });

            foreach (var character in characters)
            {
                var cidKey = character.Cid.ToString("X16");
                var charKey = ArRoster.CharacterKey(character.Cid);
                bool IsSelected() =>
                    HasConfigVariable("SelectedCharacter")
                    && GetConfigValue<string>("SelectedCharacter") == cidKey;

                characterVariables.Add(new BooleanWidgetConfigVariable(
                    charKey,
                    "このキャラを数える",
                    "OFFにすると、このキャラのリテイナーと潜水艦をツールバーの人数から外します。",
                    true)
                {
                    Category = "キャラクター",
                    DisplayIf = IsSelected
                });

                foreach (var retainer in character.Retainers)
                {
                    var retKey = ArRoster.RetainerKey(character.Cid, retainer.Name);
                    characterVariables.Add(new BooleanWidgetConfigVariable(
                        retKey,
                        retainer.Name,
                        "OFFにすると、このリテイナーをツールバーの人数から外します。",
                        true)
                    {
                        Category = "キャラクター",
                        DisplayIf = IsSelected
                    });
                }
            }
        }
        catch
        {
            // AutoRetainer の設定が読めないときは、キャラタブを空にする
        }

        return
        [
            ..base.GetConfigVariables(),
            new ColorWidgetConfigVariable(
                "ReadyColor",
                "回収できるときの文字色",
                "R または M のうち、回収できる側だけに使う色です。",
                0xFF55CC55)
            {
                Category = "文字色"
            },
            new ColorWidgetConfigVariable(
                "BusyColor",
                "通常の文字色",
                "R または M のうち、回収できるものがない側に使う色です。",
                0xFFFFFFFF)
            {
                Category = "文字色"
            },
            ..characterVariables,
        ];
    }

    protected override void OnLoad()
    {
        Reader = new AutoRetainerStatusReader();
        Node.OnClick += OpenAutoRetainer;
        SetText("\u200B");
        SingleLabelTextNode.Style.Gap = 0;
        SingleLabelTextNode.AppendChild(_retainerNode);
        SingleLabelTextNode.AppendChild(_spacerNode);
        SingleLabelTextNode.AppendChild(_subNode);
        SetTooltip("AutoRetainer の状態を読み込み中です。");
    }

    protected override void OnDraw()
    {
        if (Reader == null)
            return;

        var status = Reader.Read(IsCharacterEnabled, IsRetainerEnabled);
        SetText("\u200B");
        SetTextColor(new Color(0x00000000), null);
        SetTooltip(status.Tooltip);

        if (!status.Available)
        {
            _retainerNode.NodeValue = "ARオフ";
            _retainerNode.Style.Color = new Color(GetConfigValue<uint>("BusyColor"));
            _spacerNode.Style.IsVisible = false;
            _subNode.Style.IsVisible = false;
            return;
        }

        _spacerNode.Style.IsVisible = true;
        _subNode.Style.IsVisible = true;
        _retainerNode.NodeValue = $"R{status.RetainerReady}|{status.RetainerBusy}";
        _subNode.NodeValue = $"M{status.SubReady}|{status.SubBusy}";
        _retainerNode.Style.Color = new Color(GetConfigValue<uint>(status.HasRetainerReady ? "ReadyColor" : "BusyColor"));
        _subNode.Style.Color = new Color(GetConfigValue<uint>(status.HasSubReady ? "ReadyColor" : "BusyColor"));
    }

    protected override void OnUnload()
    {
        Node.OnClick -= OpenAutoRetainer;
        Reader?.Dispose();
        Reader = null;
    }

    private void OpenAutoRetainer(Node _)
    {
        CommandManager.ProcessCommand("/ays");
    }

    private bool IsCharacterEnabled(ulong cid)
    {
        var key = ArRoster.CharacterKey(cid);
        return !HasConfigVariable(key) || GetConfigValue<bool>(key);
    }

    private bool IsRetainerEnabled(ulong cid, string name)
    {
        var key = ArRoster.RetainerKey(cid, name);
        return !HasConfigVariable(key) || GetConfigValue<bool>(key);
    }
}
